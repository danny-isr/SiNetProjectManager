using Microsoft.EntityFrameworkCore;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.Inspection;
using SiNet.Infrastructure.Sql.Services.SeedData;
using SiNet.Infrastructure.Sql.Services.Workflow;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Workflow;

public sealed class ReviewInspectionWorkTargetProvisioningTests
{
    [Fact]
    public void Review_seed_ManagerApproved_goes_to_SendReportToPlanner_not_AwaitingPlannerCorrections()
    {
        var managerApproved = ReviewWorkflowSeedData.Transitions
            .Where(t => t.FromStageCode == ReviewStageCodes.AwaitingManagerApproval
                        && t.TaskResultCode == TaskResultCodes.ManagerApproved)
            .ToList();

        Assert.Single(managerApproved);
        Assert.Equal(ReviewStageCodes.SendReportToPlanner, managerApproved[0].ToStageCode);
        Assert.DoesNotContain(
            managerApproved,
            t => t.ToStageCode == ReviewStageCodes.AwaitingPlannerCorrections);

        var commentsSent = ReviewWorkflowSeedData.Transitions
            .Where(t => t.FromStageCode == ReviewStageCodes.SendReportToPlanner
                        && t.TaskResultCode == TaskResultCodes.CommentsSentToPlanner)
            .ToList();
        Assert.Single(commentsSent);
        Assert.Equal(ReviewStageCodes.AwaitingPlannerCorrections, commentsSent[0].ToStageCode);

        var requestedChanges = ReviewWorkflowSeedData.Transitions
            .Where(t => t.FromStageCode == ReviewStageCodes.AwaitingManagerApproval
                        && t.TaskResultCode == TaskResultCodes.ManagerRequestedChanges)
            .ToList();
        Assert.Single(requestedChanges);
        Assert.Equal(ReviewStageCodes.ProfessionalReview, requestedChanges[0].ToStageCode);

        Assert.Contains(
            ReviewWorkflowSeedData.StageTasks,
            t => t.StageCode == ReviewStageCodes.SendReportToPlanner
                 && t.TaskTypeCode == TaskTypeCodes.SendReportToPlanner);
    }

    [Fact]
    public async Task Resolve_single_report_work_target_from_prior_workflow_task()
    {
        var factory = await SeedWorkflowLinksAsync(reportIds: [41], markWorkTarget: true);
        await using var db = await factory.CreateDbContextAsync();

        var resolved = await WorkflowStageTaskProvisioningService
            .ResolveSingleInspectionReportIdForWorkflowAsync(db, workflowInstanceId: 83, CancellationToken.None);

        Assert.Equal(41, resolved);
    }

    [Fact]
    public async Task Resolve_fails_closed_when_multiple_distinct_reports()
    {
        var factory = await SeedWorkflowLinksAsync(reportIds: [41, 42], markWorkTarget: true);
        await using var db = await factory.CreateDbContextAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WorkflowStageTaskProvisioningService
                .ResolveSingleInspectionReportIdForWorkflowAsync(db, 83, CancellationToken.None)
                .AsTask());

        Assert.Contains("distinct InspectionReport", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolve_returns_null_when_no_report_links()
    {
        var factory = await SeedWorkflowLinksAsync(reportIds: [], markWorkTarget: false);
        await using var db = await factory.CreateDbContextAsync();

        var resolved = await WorkflowStageTaskProvisioningService
            .ResolveSingleInspectionReportIdForWorkflowAsync(db, 83, CancellationToken.None);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task Repair_ensures_report_work_target_and_demotes_email_work_target()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var factory = new StubFactory(options);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.TaskLinks.AddRange(
                new TaskLink
                {
                    Id = 1,
                    TaskId = 299,
                    LinkedEntityType = TaskLinkEntityType.EmailInboxMessage,
                    LinkedEntityId = 21,
                    Role = TaskLinkRole.Related,
                    IsWorkTarget = true,
                    WorkStatus = WorkTargetStatus.Pending,
                    CreatedAtUtc = DateTime.UtcNow,
                },
                new TaskLink
                {
                    Id = 2,
                    TaskId = 299,
                    LinkedEntityType = TaskLinkEntityType.EmailInboxMessage,
                    LinkedEntityId = 21,
                    Role = TaskLinkRole.Source,
                    CreatedAtUtc = DateTime.UtcNow,
                });
            await db.SaveChangesAsync();
        }

        var sut = new SqlInspectionReportTaskLinkService(factory);
        await sut.RepairReportTaskWorkTargetsAsync(299, reportId: 4, emailSourceEntityId: 21, userId: 12);

        await using var verify = await factory.CreateDbContextAsync();
        var links = await verify.TaskLinks.Where(l => l.TaskId == 299).ToListAsync();

        Assert.Contains(links, l =>
            l.LinkedEntityType == TaskLinkEntityType.InspectionReport
            && l.LinkedEntityId == 4
            && l.Role == TaskLinkRole.Related
            && l.IsWorkTarget);

        Assert.Contains(links, l =>
            l.LinkedEntityType == TaskLinkEntityType.EmailInboxMessage
            && l.LinkedEntityId == 21
            && l.Role == TaskLinkRole.Source
            && !l.IsWorkTarget);

        Assert.DoesNotContain(links, l =>
            l.LinkedEntityType == TaskLinkEntityType.EmailInboxMessage && l.IsWorkTarget);
    }

    private static async Task<StubFactory> SeedWorkflowLinksAsync(IReadOnlyList<int> reportIds, bool markWorkTarget)
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var factory = new StubFactory(options);
        await using var db = await factory.CreateDbContextAsync();

        db.ProjectAssignments.Add(new ProjectAssignment { Id = 298, ProjectId = 136, Title = "PR" });
        db.TaskLinks.Add(new TaskLink
        {
            TaskId = 298,
            LinkedEntityType = TaskLinkEntityType.WorkflowInstance,
            LinkedEntityId = 83,
            Role = TaskLinkRole.Trigger,
            CreatedAtUtc = DateTime.UtcNow,
        });

        var id = 10;
        foreach (var reportId in reportIds)
        {
            db.TaskLinks.Add(new TaskLink
            {
                Id = id++,
                TaskId = 298,
                LinkedEntityType = TaskLinkEntityType.InspectionReport,
                LinkedEntityId = reportId,
                Role = TaskLinkRole.Related,
                IsWorkTarget = markWorkTarget,
                WorkStatus = WorkTargetStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
        return factory;
    }

    private sealed class StubFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);

        public Task<SiNetSQLDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
