using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Settings;
using SiNet.Application.Tasks;
using SiNet.Application.Workflow;
using SiNet.App.Wpf.Tests.Support;
using SiNet.Infrastructure.Sql;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.DevTools;
using SiNet.Infrastructure.Sql.Services.Tasks;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Tasks;

/// <summary>
/// Proves the OUT production completion seam: unique completion events with empty
/// AllowedTaskResultCodes, ClosesAssociatedTask, and ITaskCompletionService walk to
/// OUT.Complete without inventing TaskResult codes.
/// </summary>
public sealed class OutsourcingCompletionSeamTests
{
    private const int UserId = 1;

    [Theory]
    [InlineData(TaskTypeCodes.ReceiveOutsourceQuote, ReviewCompletionEvents.OutsourceQuoteReceived)]
    [InlineData(TaskTypeCodes.ApproveOutsourceQuote, ReviewCompletionEvents.OutsourceOfferApproved)]
    [InlineData(TaskTypeCodes.MonitorOutsourcePayments, ReviewCompletionEvents.OutsourcePaymentsCompleted)]
    public void ResolveCompletionEventCode_with_null_result_maps_unique_out_event(
        string taskTypeCode,
        string expectedEvent)
    {
        var resolver = new SqlTaskCompletionMetadataResolver();
        Assert.Equal(expectedEvent, resolver.ResolveCompletionEventCode(taskTypeCode, taskResultCode: null));
    }

    [Theory]
    [InlineData(ReviewCompletionEvents.OutsourceQuoteReceived, TaskTypeCodes.ReceiveOutsourceQuote)]
    [InlineData(ReviewCompletionEvents.OutsourceOfferApproved, TaskTypeCodes.ApproveOutsourceQuote)]
    [InlineData(ReviewCompletionEvents.OutsourcePaymentsCompleted, TaskTypeCodes.MonitorOutsourcePayments)]
    public void Out_completion_events_close_associated_task_with_empty_allowed_results(
        string eventCode,
        string taskTypeCode)
    {
        var behavior = ReviewCompletionEventBehavior.TryGet(eventCode);
        Assert.NotNull(behavior);
        Assert.True(behavior!.ClosesAssociatedTask);
        Assert.True(behavior.RequestWorkflowAdvance);
        Assert.Empty(behavior.AllowedTaskResultCodes);
        Assert.Null(behavior.NewProjectStatusCode);
        Assert.Contains(taskTypeCode, behavior.ApplicableTaskTypeCodes);

        var interaction = ReviewTaskInteractionRegistry.TryGet(taskTypeCode);
        Assert.NotNull(interaction);
        Assert.Equal(TaskCompletionPolicy.ExplicitCompletionEvent, interaction!.CompletionPolicy);
        Assert.Empty(interaction.AllowedTaskResultCodes);
        Assert.Equal(TaskComponentKeys.ProjectWork, interaction.ComponentKey);
    }

    [Fact]
    public async Task ITaskCompletionService_walks_OUT_contract_A_to_Completed_without_TaskResult()
    {
        var (provider, options) = await BuildSeededProviderAsync();
        await using (provider)
        {
            var projectId = await SeedProjectAsync(options);
            var defId = await GetOutsourcingDefinitionIdAsync(options);

            var commands = provider.GetRequiredService<IWorkflowCommandService>();
            var completion = provider.GetRequiredService<ITaskCompletionService>();
            var metadata = provider.GetRequiredService<ITaskCompletionMetadataResolver>();

            var start = await commands.StartAsync(
                new StartWorkflowCommand(
                    defId,
                    projectId,
                    WorkflowTriggerTypeDto.Manual,
                    TriggerEntityId: null,
                    UserId,
                    Notes: "OUT completion seam test",
                    IsProjectBound: true),
                CancellationToken.None);

            var instanceId = start.Instance.Id;
            await AssertCurrentStageAsync(options, instanceId, OutsourcingStageCodes.ReceiveOffer);

            await CompleteOutTaskAsync(
                options,
                completion,
                metadata,
                instanceId,
                TaskTypeCodes.ReceiveOutsourceQuote,
                OutsourcingStageCodes.ApproveOffer);

            await CompleteOutTaskAsync(
                options,
                completion,
                metadata,
                instanceId,
                TaskTypeCodes.ApproveOutsourceQuote,
                OutsourcingStageCodes.MonitorPayments);

            await CompleteOutTaskAsync(
                options,
                completion,
                metadata,
                instanceId,
                TaskTypeCodes.MonitorOutsourcePayments,
                OutsourcingStageCodes.Complete);

            await using var db = new SiNetSQLDbContext(options);
            var instance = await db.WorkflowInstances
                .Include(i => i.CurrentStage)
                .FirstAsync(i => i.Id == instanceId);
            Assert.Equal(WorkflowStatus.Completed, instance.Status);
            Assert.Equal(OutsourcingStageCodes.Complete, instance.CurrentStage!.Code);

            var openTasks = await db.ProjectAssignments
                .Include(t => t.AssignmentStatus)
                .Include(t => t.TaskLinks)
                .Where(t => t.TaskLinks.Any(l =>
                    l.LinkedEntityType == TaskLinkEntityType.WorkflowInstance
                    && l.LinkedEntityId == instanceId
                    && l.Role == TaskLinkRole.Trigger)
                    && t.AssignmentStatus!.IsOpen)
                .CountAsync();
            Assert.Equal(0, openTasks);
        }
    }

    private static async Task CompleteOutTaskAsync(
        DbContextOptions<SiNetSQLDbContext> options,
        ITaskCompletionService completion,
        ITaskCompletionMetadataResolver metadata,
        int instanceId,
        string taskTypeCode,
        string expectedNextStage)
    {
        var open = await GetOpenStageTaskAsync(options, instanceId, taskTypeCode);
        var eventCode = metadata.ResolveCompletionEventCode(taskTypeCode, taskResultCode: null);
        Assert.False(string.IsNullOrWhiteSpace(eventCode), $"No completion event for {taskTypeCode}");

        var outcome = await completion.CompleteAsync(
            new CompleteTaskCommand(
                open.Id,
                eventCode!,
                TaskResultCode: null,
                CompletedTaskLinkIds: null,
                UserId),
            CancellationToken.None);

        Assert.True(outcome.Success, outcome.ErrorMessage);
        Assert.True(outcome.TaskClosed, $"{taskTypeCode} must close via ClosesAssociatedTask");
        Assert.Null(outcome.RecordedTaskResultCode);
        Assert.True(outcome.WorkflowAdvanced, $"{taskTypeCode} must auto-advance");

        await using var db = new SiNetSQLDbContext(options);
        var closed = await db.ProjectAssignments
            .Include(t => t.AssignmentStatus)
            .FirstAsync(t => t.Id == open.Id);
        Assert.False(closed.AssignmentStatus!.IsOpen);
        Assert.Null(closed.LastTaskResultId);

        await AssertCurrentStageAsync(options, instanceId, expectedNextStage);
    }

    private static async Task<(Microsoft.Extensions.DependencyInjection.ServiceProvider Provider, DbContextOptions<SiNetSQLDbContext> Options)> BuildSeededProviderAsync()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        var factory = new StubDbContextFactory(options);

        await SeedLookupsAsync(factory);
        await new SqlWorkflowSeedService(factory).SeedAllAsync(CancellationToken.None);

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<SiNetSQLDbContext>>(factory);
        services.AddSingleton<ISystemSettingsQueryService>(new PermissivePilotSystemSettingsQueryService(UserId));
        services.AddSiNetProcessBackbone();
        return (services.BuildServiceProvider(), options);
    }

    private static async Task SeedLookupsAsync(IDbContextFactory<SiNetSQLDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();

        db.ProjectStatuses.Add(new ProjectStatus
        {
            Code = ProjectStatusCodes.Active,
            Title = "פעיל",
            IsActive = true,
            SortOrder = 50,
        });

        db.ProjectAssignmentStatuses.AddRange(
            new ProjectAssignmentStatus
            {
                Code = TaskStatusCodes.Open,
                Name = "פתוח",
                IsActive = true,
                IsOpen = true,
                IsActionable = true,
                SortOrder = 10,
            },
            new ProjectAssignmentStatus
            {
                Code = TaskStatusCodes.Completed,
                Name = "הושלם",
                IsActive = true,
                IsOpen = false,
                IsActionable = false,
                SortOrder = 60,
            });

        db.TaskTypes.AddRange(
            new TaskType { Code = TaskTypeCodes.ReceiveOutsourceQuote, Name = "קבלת הצעת מיקור חוץ", IsActive = true },
            new TaskType { Code = TaskTypeCodes.ApproveOutsourceQuote, Name = "אישור הצעת מיקור חוץ", IsActive = true },
            new TaskType { Code = TaskTypeCodes.MonitorOutsourcePayments, Name = "מעקב תשלומי מיקור חוץ", IsActive = true });

        db.Siusers.Add(new Siuser { Id = UserId, Name = "Test User", IsActive = true });
        await db.SaveChangesAsync();

        var group = new UserGroup
        {
            Code = UserGroupCodes.OfficeManagement,
            Name = UserGroupCodes.OfficeManagement,
            IsActive = true,
            DefaultAssigneeId = UserId,
        };
        db.UserGroups.Add(group);
        await db.SaveChangesAsync();
        db.UserGroupMemberships.Add(new UserGroupMembership { SiuserId = UserId, UserGroupId = group.Id });
        await db.SaveChangesAsync();
    }

    private static async Task<int> SeedProjectAsync(DbContextOptions<SiNetSQLDbContext> options)
    {
        await using var db = new SiNetSQLDbContext(options);
        var active = await db.ProjectStatuses.FirstAsync(s => s.Code == ProjectStatusCodes.Active);
        var project = new Project { Title = "[SYS-CERT] OUT offline", ProjectStatusId = active.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project.Id;
    }

    private static async Task<int> GetOutsourcingDefinitionIdAsync(DbContextOptions<SiNetSQLDbContext> options)
    {
        await using var db = new SiNetSQLDbContext(options);
        return await db.WorkflowDefinitions
            .Where(d => d.Code == WorkflowCodes.Outsourcing && d.IsActive)
            .Select(d => d.Id)
            .FirstAsync();
    }

    private static async Task<ProjectAssignment> GetOpenStageTaskAsync(
        DbContextOptions<SiNetSQLDbContext> options,
        int instanceId,
        string taskTypeCode)
    {
        await using var db = new SiNetSQLDbContext(options);
        return await db.ProjectAssignments
            .Include(t => t.TaskType)
            .Include(t => t.TaskLinks)
            .Include(t => t.AssignmentStatus)
            .Where(t => t.TaskType!.Code == taskTypeCode
                        && t.AssignmentStatus!.IsOpen
                        && t.TaskLinks.Any(l =>
                            l.LinkedEntityType == TaskLinkEntityType.WorkflowInstance
                            && l.LinkedEntityId == instanceId))
            .OrderByDescending(t => t.Id)
            .FirstAsync();
    }

    private static async Task AssertCurrentStageAsync(
        DbContextOptions<SiNetSQLDbContext> options,
        int instanceId,
        string expectedStageCode)
    {
        await using var db = new SiNetSQLDbContext(options);
        var instance = await db.WorkflowInstances
            .Include(i => i.CurrentStage)
            .FirstAsync(i => i.Id == instanceId);
        Assert.Equal(expectedStageCode, instance.CurrentStage!.Code);
    }

    private sealed class StubDbContextFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);

        public Task<SiNetSQLDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SiNetSQLDbContext(options));
    }
}
