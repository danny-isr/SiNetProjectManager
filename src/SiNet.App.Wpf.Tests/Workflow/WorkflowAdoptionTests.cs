using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Workflow;
using SiNet.App.Wpf.Tests.Support;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.SeedData;
using SiNet.Infrastructure.Sql.Services.Workflow;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Workflow;

public sealed class WorkflowAdoptionTests
{
    [Fact]
    public async Task Atomic_start_rolls_back_when_stage_task_has_no_assignee()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var ctx = await PrepareReviewProjectAsync(options, assignReviewers: false);
            var orchestrator = provider.GetRequiredService<WorkflowTaskOrchestrator>();

            await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
                orchestrator.StartWorkflowAtomicAsync(
                    ctx.DefinitionId,
                    ctx.ProjectId,
                    WorkflowTriggerType.System,
                    triggerEntityId: null,
                    ProposalWorkflowHarness.UserId,
                    "should roll back",
                    CancellationToken.None,
                    initialStageCode: ReviewStageCodes.ProfessionalReview,
                    jobTypeId: ctx.JobTypeId,
                    requireCurrentStageTask: true).AsTask());

            await using var db = new SiNetSQLDbContext(options);
            Assert.Equal(0, await db.WorkflowInstances.CountAsync());
            Assert.Equal(0, await db.WorkflowStageTransitions.CountAsync());
            Assert.Equal(0, await db.ProjectAssignments.CountAsync(t => t.ProjectId == ctx.ProjectId));
        }
    }

    [Fact]
    public async Task Adopt_Review_at_ProfessionalReview_creates_only_current_work()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var ctx = await PrepareReviewProjectAsync(options, assignReviewers: true);
            var adoption = provider.GetRequiredService<IWorkflowAdoptionService>();
            var request = Request(ctx, ReviewStageCodes.ProfessionalReview);

            var preview = await adoption.PreviewAsync(request, CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.ReadyToAdopt, preview.Disposition);
            Assert.True(preview.CanCommit);
            Assert.Contains(preview.WillCreateTasks, t => t.TaskTypeCode == TaskTypeCodes.PerformProfessionalReview);
            Assert.Contains(preview.WillNotCreate, t => t == TaskTypeCodes.OpenReviewProject);
            Assert.Contains(preview.HistoricalStages, s => s.Code == ReviewStageCodes.ProjectSetup);
            Assert.Contains(preview.HistoricalStages, s => s.Code == ReviewStageCodes.MaterialIntake);
            Assert.Contains(preview.WillNotCreate, t => t.Contains("child workflow", StringComparison.Ordinal));

            var committed = await adoption.CommitAsync(request, CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.Committed, committed.Disposition);
            Assert.NotNull(committed.WorkflowInstanceId);

            await using var db = new SiNetSQLDbContext(options);
            var instance = await db.WorkflowInstances
                .Include(i => i.CurrentStage)
                .Include(i => i.StageTransitions)
                .SingleAsync();
            Assert.Equal(WorkflowStatus.Active, instance.Status);
            Assert.Equal(ctx.JobTypeId, instance.JobTypeId);
            Assert.Equal(ReviewStageCodes.ProfessionalReview, instance.CurrentStage!.Code);
            Assert.Equal(WorkflowTriggerType.System, instance.TriggerType);
            Assert.Contains("[ADOPTED]", instance.Notes, StringComparison.Ordinal);
            Assert.Single(instance.StageTransitions);
            Assert.Null(instance.StageTransitions.Single().FromStageId);

            Assert.Equal(1, await CountTasksAsync(db, instance.Id, TaskTypeCodes.PerformProfessionalReview));
            Assert.Equal(0, await CountTasksAsync(db, instance.Id, TaskTypeCodes.OpenReviewProject));
            var matDefId = await db.WorkflowDefinitions
                .Where(d => d.Code == WorkflowCodes.MaterialIntake)
                .Select(d => d.Id)
                .FirstAsync();
            Assert.Equal(0, await db.WorkflowInstances.CountAsync(i => i.WorkflowDefinitionId == matDefId));
        }
    }

    [Fact]
    public async Task Second_commit_does_not_duplicate_instance_or_task()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var ctx = await PrepareReviewProjectAsync(options, assignReviewers: true);
            var adoption = provider.GetRequiredService<IWorkflowAdoptionService>();
            var request = Request(ctx, ReviewStageCodes.ProfessionalReview);

            var first = await adoption.CommitAsync(request, CancellationToken.None);
            var second = await adoption.CommitAsync(request, CancellationToken.None);

            Assert.Equal(WorkflowAdoptionDisposition.Committed, first.Disposition);
            Assert.Equal(WorkflowAdoptionDisposition.AlreadyExists, second.Disposition);
            Assert.Equal(first.WorkflowInstanceId, second.WorkflowInstanceId);

            await using var db = new SiNetSQLDbContext(options);
            Assert.Equal(1, await db.WorkflowInstances.CountAsync());
            Assert.Equal(1, await CountTasksAsync(db, first.WorkflowInstanceId!.Value, TaskTypeCodes.PerformProfessionalReview));
        }
    }

    [Fact]
    public async Task Existing_active_earlier_stage_requires_review_and_does_not_advance()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var ctx = await PrepareReviewProjectAsync(options, assignReviewers: true);
            var adoption = provider.GetRequiredService<IWorkflowAdoptionService>();
            var started = await adoption.CommitAsync(Request(ctx, ReviewStageCodes.ProjectSetup), CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.Committed, started.Disposition);

            var preview = await adoption.PreviewAsync(Request(ctx, ReviewStageCodes.ProfessionalReview), CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.RequiresReviewExistingWorkflow, preview.Disposition);
            Assert.False(preview.CanCommit);

            var commit = await adoption.CommitAsync(Request(ctx, ReviewStageCodes.ProfessionalReview), CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.RequiresReviewExistingWorkflow, commit.Disposition);

            await using var db = new SiNetSQLDbContext(options);
            var instance = await db.WorkflowInstances
                .Include(i => i.CurrentStage)
                .Include(i => i.StageTransitions)
                .SingleAsync();
            Assert.Equal(ReviewStageCodes.ProjectSetup, instance.CurrentStage!.Code);
            Assert.Single(instance.StageTransitions);
        }
    }

    [Fact]
    public async Task Existing_paused_and_completed_are_not_duplicated()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var ctx = await PrepareReviewProjectAsync(options, assignReviewers: true);
            var adoption = provider.GetRequiredService<IWorkflowAdoptionService>();
            var commands = provider.GetRequiredService<IWorkflowCommandService>();
            var request = Request(ctx, ReviewStageCodes.ProfessionalReview);
            var started = await adoption.CommitAsync(request, CancellationToken.None);

            await commands.PauseAsync(
                new PauseWorkflowCommand(started.WorkflowInstanceId!.Value, ProposalWorkflowHarness.UserId, null),
                CancellationToken.None);
            var paused = await adoption.PreviewAsync(request, CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.BlockedAlreadyPaused, paused.Disposition);

            await commands.ResumeAsync(
                new ResumeWorkflowCommand(started.WorkflowInstanceId.Value, ProposalWorkflowHarness.UserId, null),
                CancellationToken.None);
            await commands.CompleteInstanceAsync(
                new CompleteWorkflowCommand(started.WorkflowInstanceId.Value, ProposalWorkflowHarness.UserId, null),
                CancellationToken.None);
            var completed = await adoption.PreviewAsync(request, CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.RequiresReviewCompletedExists, completed.Disposition);

            await using var db = new SiNetSQLDbContext(options);
            Assert.Equal(1, await db.WorkflowInstances.CountAsync());
        }
    }

    [Fact]
    public async Task SubWorkflow_host_final_stage_and_inactive_jobtype_stage_are_blocked()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var ctx = await PrepareReviewProjectAsync(options, assignReviewers: true);
            var adoption = provider.GetRequiredService<IWorkflowAdoptionService>();

            var material = await adoption.PreviewAsync(Request(ctx, ReviewStageCodes.MaterialIntake), CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.BlockedInvalidStage, material.Disposition);

            var final = await adoption.PreviewAsync(Request(ctx, ReviewStageCodes.Completed), CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.BlockedInvalidStage, final.Disposition);

            await using (var db = new SiNetSQLDbContext(options))
            {
                var stageId = await db.WorkflowStageDefinitions
                    .Where(s => s.Code == ReviewStageCodes.ProfessionalReview)
                    .Select(s => s.Id)
                    .FirstAsync();
                var row = await db.ProjectTypeWorkflowStages
                    .FirstAsync(p => p.ProjectTypeId == ctx.JobTypeId && p.WorkflowStageDefinitionId == stageId);
                row.IsActive = false;
                var setup = await db.WorkflowStageDefinitions.FirstAsync(s => s.Code == ReviewStageCodes.ProjectSetup);
                setup.NodeType = "Start";
                await db.SaveChangesAsync();
            }

            var inactive = await adoption.PreviewAsync(Request(ctx, ReviewStageCodes.ProfessionalReview), CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.BlockedInvalidStage, inactive.Disposition);

            var startNode = await adoption.PreviewAsync(Request(ctx, ReviewStageCodes.ProjectSetup), CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.BlockedInvalidStage, startNode.Disposition);

            await using var verify = new SiNetSQLDbContext(options);
            Assert.Equal(0, await verify.WorkflowInstances.CountAsync());
        }
    }

    [Fact]
    public async Task Active_report_is_linked_and_historical_reports_are_not_duplicated()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var ctx = await PrepareReviewProjectAsync(options, assignReviewers: true);
            int report1;
            int report2;
            int report3;
            await using (var db = new SiNetSQLDbContext(options))
            {
                report1 = await AddReportAsync(db, ctx.ProjectId, 1);
                report2 = await AddReportAsync(db, ctx.ProjectId, 2);
                report3 = await AddReportAsync(db, ctx.ProjectId, 3);
            }

            var adoption = provider.GetRequiredService<IWorkflowAdoptionService>();
            var request = Request(
                ctx,
                ReviewStageCodes.ProfessionalReview,
                reports:
                [
                    new WorkflowAdoptionReportIntent(report1, WorkflowAdoptionReportMode.Historical),
                    new WorkflowAdoptionReportIntent(report2, WorkflowAdoptionReportMode.Historical),
                    new WorkflowAdoptionReportIntent(report3, WorkflowAdoptionReportMode.Active),
                ]);

            var preview = await adoption.PreviewAsync(request, CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.ReadyToAdopt, preview.Disposition);
            Assert.Contains(preview.Warnings, w => w.Contains("MarkReportAsSentAsync", StringComparison.Ordinal));

            var committed = await adoption.CommitAsync(request, CancellationToken.None);
            Assert.Equal(WorkflowAdoptionDisposition.Committed, committed.Disposition);
            Assert.NotNull(committed.ActiveReportLinkId);

            await adoption.CommitAsync(request, CancellationToken.None);

            await using var verify = new SiNetSQLDbContext(options);
            Assert.Equal(3, await verify.InspectionReports.CountAsync(r => r.ProjectId == ctx.ProjectId));
            var links = await verify.TaskLinks
                .Where(l =>
                    l.LinkedEntityType == TaskLinkEntityType.InspectionReport
                    && l.IsWorkTarget)
                .ToListAsync();
            var link = Assert.Single(links);
            Assert.Equal(report3, link.LinkedEntityId);
            Assert.Equal(TaskLinkRole.Related, link.Role);
            Assert.Equal(WorkTargetStatus.Pending, link.WorkStatus);
            Assert.True(await verify.InspectionReports.Where(r => r.ReportId == report1).AllAsync(r => !r.IsLockedAfterSend));
        }
    }

    [Fact]
    public async Task Historical_only_reports_leave_the_current_task_without_a_report_link()
    {
        var (provider, options) = await ProposalWorkflowHarness.BuildSeededProviderAsync();
        await using (provider)
        {
            var ctx = await PrepareReviewProjectAsync(options, assignReviewers: true);
            int reportId;
            await using (var db = new SiNetSQLDbContext(options))
                reportId = await AddReportAsync(db, ctx.ProjectId, 1);

            var adoption = provider.GetRequiredService<IWorkflowAdoptionService>();
            var committed = await adoption.CommitAsync(
                Request(
                    ctx,
                    ReviewStageCodes.ProfessionalReview,
                    reports: [new WorkflowAdoptionReportIntent(reportId, WorkflowAdoptionReportMode.Historical)]),
                CancellationToken.None);

            Assert.Equal(WorkflowAdoptionDisposition.Committed, committed.Disposition);
            Assert.Null(committed.ActiveReportLinkId);

            await using var verify = new SiNetSQLDbContext(options);
            Assert.Equal(0, await verify.TaskLinks.CountAsync(l =>
                l.LinkedEntityType == TaskLinkEntityType.InspectionReport && l.IsWorkTarget));
            Assert.Equal(1, await CountTasksAsync(verify, committed.WorkflowInstanceId!.Value, TaskTypeCodes.PerformProfessionalReview));
        }
    }

    private static WorkflowAdoptionRequest Request(
        ReviewAdoptionContext ctx,
        string stageCode,
        IReadOnlyList<WorkflowAdoptionReportIntent>? reports = null) =>
        new(
            ctx.ProjectId,
            ctx.DefinitionId,
            ctx.JobTypeId,
            stageCode,
            ProposalWorkflowHarness.UserId,
            OriginalStartedAt: new DateTime(2024, 3, 1),
            Notes: "manual adoption",
            Reports: reports);

    private static async Task<ReviewAdoptionContext> PrepareReviewProjectAsync(
        DbContextOptions<SiNetSQLDbContext> options,
        bool assignReviewers)
    {
        await using var db = new SiNetSQLDbContext(options);
        var active = await db.ProjectStatuses.FirstAsync(s => s.Code == ProjectStatusCodes.Active);
        var project = new Project { Title = "יבנה מזרח", Number = 1844, ProjectStatusId = active.Id };
        var jobType = new JobType { Title = "בדיקת תוכנית" };
        db.Projects.Add(project);
        db.JobTypes.Add(jobType);
        await db.SaveChangesAsync();
        db.TypeOfProjectInProjects.Add(new TypeOfProjectInProject
        {
            ProjectId = project.Id,
            ProjectTypeId = jobType.Id,
            Title = jobType.Title,
        });

        var definition = await db.WorkflowDefinitions.FirstAsync(d => d.Code == WorkflowCodes.Review && d.IsActive);
        var stages = await db.WorkflowStageDefinitions
            .Where(s => s.WorkflowDefinitionId == definition.Id)
            .ToListAsync();
        foreach (var stage in stages)
        {
            db.ProjectTypeWorkflowStages.Add(new ProjectTypeWorkflowStage
            {
                ProjectTypeId = jobType.Id,
                WorkflowStageDefinitionId = stage.Id,
                IsActive = true,
                IsRequired = true,
                SortOrder = stage.SortOrder,
            });
        }

        if (assignReviewers)
        {
            var groupCodes = ReviewWorkflowSeedData.StageGroupAssignments.Values
                .Append(UserGroupCodes.OfficeManagement)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var groups = await db.UserGroups
                .Where(g => g.Code != null && groupCodes.Contains(g.Code))
                .ToListAsync();
            foreach (var group in groups)
            {
                var member = await db.UserGroupMemberships
                    .AnyAsync(m => m.UserGroupId == group.Id && m.SiuserId == ProposalWorkflowHarness.UserId);
                if (!member)
                {
                    db.UserGroupMemberships.Add(new UserGroupMembership
                    {
                        UserGroupId = group.Id,
                        SiuserId = ProposalWorkflowHarness.UserId,
                    });
                }
            }
        }

        await EnsureReviewStageTasksAsync(db, stages);
        await db.SaveChangesAsync();
        return new ReviewAdoptionContext(project.Id, jobType.Id, definition.Id);
    }

    /// <summary>
    /// Workflow seed skips REV stage-task templates when the TaskType rows are absent.
    /// The task-type seed lives in a different service, so this fixture creates the
    /// missing types and templates from <see cref="ReviewWorkflowSeedData"/>.
    /// </summary>
    private static async Task EnsureReviewStageTasksAsync(
        SiNetSQLDbContext db,
        IReadOnlyList<WorkflowStageDefinition> stages)
    {
        foreach (var def in ReviewWorkflowSeedData.StageTasks)
        {
            var taskType = await db.TaskTypes.FirstOrDefaultAsync(t => t.Code == def.TaskTypeCode);
            if (taskType is null)
            {
                taskType = new TaskType { Code = def.TaskTypeCode, Name = def.TaskTypeCode, IsActive = true };
                db.TaskTypes.Add(taskType);
                await db.SaveChangesAsync();
            }

            var stage = stages.FirstOrDefault(s => s.Code == def.StageCode);
            if (stage is null)
                continue;

            var exists = await db.WorkflowStageTasks.AnyAsync(t =>
                t.StageDefinitionId == stage.Id && t.TaskTypeId == taskType.Id && t.IsActive);
            if (exists)
                continue;

            db.WorkflowStageTasks.Add(new WorkflowStageTask
            {
                StageDefinitionId = stage.Id,
                TaskTypeId = taskType.Id,
                SortOrder = def.SortOrder,
                IsRequired = def.IsRequired,
                Notes = def.Notes,
                IsActive = true,
            });
        }
    }

    private static async Task<int> AddReportAsync(SiNetSQLDbContext db, int projectId, int number)
    {
        var report = new InspectionReport
        {
            ProjectId = projectId,
            ReportNumber = number,
            InspectionDate = new DateTime(2024, 1, number),
        };
        db.InspectionReports.Add(report);
        await db.SaveChangesAsync();
        return report.ReportId;
    }

    private static async Task<int> CountTasksAsync(SiNetSQLDbContext db, int instanceId, string taskTypeCode) =>
        await db.ProjectAssignments.CountAsync(t =>
            t.TaskType!.Code == taskTypeCode
            && t.TaskLinks.Any(l =>
                l.LinkedEntityType == TaskLinkEntityType.WorkflowInstance
                && l.LinkedEntityId == instanceId));

    private sealed record ReviewAdoptionContext(int ProjectId, int JobTypeId, int DefinitionId);
}
