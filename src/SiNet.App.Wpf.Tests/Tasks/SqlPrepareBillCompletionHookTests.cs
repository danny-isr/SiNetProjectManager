using System.IO;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Billing;
using SiNet.Application.Tasks;
using SiNet.App.Wpf.Tests.Support;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.Tasks;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Tasks;

public sealed class SqlPrepareBillCompletionHookTests
{
    private const int UserId = 14;

    [Fact]
    public void PrepareBill_completion_event_closes_associated_task()
    {
        var behavior = ReviewCompletionEventBehavior.TryGet(ReviewCompletionEvents.PrepareBillCompleted);
        Assert.NotNull(behavior);
        Assert.Contains(TaskTypeCodes.PrepareBill, behavior!.ApplicableTaskTypeCodes, StringComparer.Ordinal);
        Assert.True(behavior.ClosesAssociatedTask);
        Assert.False(behavior.RequestWorkflowAdvance);
        Assert.Empty(behavior.AllowedTaskResultCodes);

        var interaction = ReviewTaskInteractionRegistry.TryGet(TaskTypeCodes.PrepareBill);
        Assert.NotNull(interaction);
        Assert.Equal(TaskCompletionPolicy.ExplicitCompletionEvent, interaction!.CompletionPolicy);
        Assert.Empty(interaction.AllowedTaskResultCodes);
        Assert.Equal(TaskComponentKeys.ProjectWork, interaction.ComponentKey);
    }

    [Fact]
    public async Task Completing_PrepareBill_notifies_billing_once()
    {
        var spy = new BillingHookSpy();
        var (factory, prepareBillId, _) = await SeedAsync();
        var completion = new SqlTaskCompletionService(factory, new StubWorkflowCommandService(), billingPreparation: spy);

        var outcome = await completion.CompleteAsync(
            new CompleteTaskCommand(prepareBillId, ReviewCompletionEvents.PrepareBillCompleted, null, null, UserId),
            CancellationToken.None);

        Assert.True(outcome.Success && outcome.TaskClosed, outcome.ErrorMessage);
        Assert.Equal(1, spy.Calls);
        Assert.Equal(prepareBillId, spy.LastTaskId);
    }

    [Fact]
    public async Task Unrelated_task_type_does_not_notify_billing()
    {
        var spy = new BillingHookSpy();
        var (factory, _, outsourceId) = await SeedAsync();
        var completion = new SqlTaskCompletionService(factory, new StubWorkflowCommandService(), billingPreparation: spy);

        var outcome = await completion.CompleteAsync(
            new CompleteTaskCommand(outsourceId, ReviewCompletionEvents.OutsourceQuoteReceived, null, null, UserId),
            CancellationToken.None);

        Assert.True(outcome.Success && outcome.TaskClosed, outcome.ErrorMessage);
        Assert.Equal(0, spy.Calls);
    }

    [Fact]
    public async Task Failed_completion_does_not_notify_billing()
    {
        var spy = new BillingHookSpy();
        var (factory, prepareBillId, _) = await SeedAsync();
        var completion = new SqlTaskCompletionService(factory, new StubWorkflowCommandService(), billingPreparation: spy);

        var outcome = await completion.CompleteAsync(
            new CompleteTaskCommand(prepareBillId, ReviewCompletionEvents.OutsourceQuoteReceived, null, null, UserId),
            CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.False(outcome.TaskClosed);
        Assert.Equal(0, spy.Calls);
    }

    [Fact]
    public void Completion_service_wires_prepare_bill_hook()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiNet.Infrastructure.Sql", "Services", "Tasks", "SqlTaskCompletionService.cs"));
        Assert.Contains("OnPrepareBillTaskCompletedByTaskIdAsync", source, StringComparison.Ordinal);
        Assert.Contains("if (!taskClosed || _billingPreparation is null)", source, StringComparison.Ordinal);
        Assert.Contains("TaskTypeCodes.PrepareBill", source, StringComparison.Ordinal);
    }

    private static async Task<(StubDbContextFactory Factory, int PrepareBillId, int OutsourceId)> SeedAsync()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new SiNetSQLDbContext(options);
        db.Siusers.Add(new Siuser { Id = UserId, Name = "QueueUser", IsActive = true });
        db.Projects.Add(new Project { Id = 1, Title = "P1", Created = DateTime.UtcNow });
        db.ProjectAssignmentStatuses.AddRange(
            new ProjectAssignmentStatus { Code = TaskStatusCodes.Open, Name = "Open", IsOpen = true, IsActionable = true, SortOrder = 1 },
            new ProjectAssignmentStatus { Code = TaskStatusCodes.Completed, Name = "Completed", IsOpen = false, IsActionable = false, SortOrder = 2 });
        db.TaskTypes.AddRange(
            new TaskType { Code = TaskTypeCodes.PrepareBill, Name = "הכנת חשבון", IsActive = true, SortOrder = 1 },
            new TaskType { Code = TaskTypeCodes.ReceiveOutsourceQuote, Name = "Receive", IsActive = true, SortOrder = 2 });
        await db.SaveChangesAsync();

        var openId = db.ProjectAssignmentStatuses.Single(s => s.Code == TaskStatusCodes.Open).Id;
        var prepareType = db.TaskTypes.Single(t => t.Code == TaskTypeCodes.PrepareBill).Id;
        var outsourceType = db.TaskTypes.Single(t => t.Code == TaskTypeCodes.ReceiveOutsourceQuote).Id;
        db.ProjectAssignments.AddRange(
            new ProjectAssignment
            {
                Title = "הכנת חשבון",
                ProjectId = 1,
                AssignedToId = UserId,
                StatusId = openId,
                TaskTypeId = prepareType,
                WorkQueueBucket = WorkQueueBucketCodes.Medium,
                WorkPriority = 1,
                Created = DateTime.UtcNow,
            },
            new ProjectAssignment
            {
                Title = "OUT",
                ProjectId = 1,
                AssignedToId = UserId,
                StatusId = openId,
                TaskTypeId = outsourceType,
                WorkQueueBucket = WorkQueueBucketCodes.Medium,
                WorkPriority = 2,
                Created = DateTime.UtcNow,
            });
        await db.SaveChangesAsync();
        var prepareBillId = db.ProjectAssignments.Single(t => t.TaskTypeId == prepareType).Id;
        var outsourceId = db.ProjectAssignments.Single(t => t.TaskTypeId == outsourceType).Id;
        return (new StubDbContextFactory(options), prepareBillId, outsourceId);
    }

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private sealed class StubDbContextFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);

        public ValueTask<SiNetSQLDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CreateDbContext());
    }

    private sealed class BillingHookSpy : IBillingPreparationService
    {
        public int Calls { get; private set; }
        public int? LastTaskId { get; private set; }

        public Task OnPrepareBillTaskCompletedByTaskIdAsync(
            int taskId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastTaskId = taskId;
            return Task.CompletedTask;
        }

        public Task<BillingPreparationEnsureResult> EnsureFromPrepareBillAsync(
            int masterPlanProjectId, string? projectNumber, string? projectName, string? customerName,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<BillingPreparationRequestRecord>> ListForPreparationTabAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<BillingPreparationRequestRecord> RefreshFromSnapshotAsync(
            int requestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<BillingPreparationRequestRecord> SaveSelectionAsync(
            int requestId,
            IReadOnlyList<BillingPreparationStageLineSnapshot> stages,
            IReadOnlyList<BillingPreparationHoursLineSnapshot> hours,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<BillingPreparationRequestRecord> ApplyManualOverrideAsync(
            int requestId, string reason, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BillingPreparationApproveResult> ApproveAndCreateTaskAsync(
            int requestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<BillingPreparationRequestRecord> OnPrepareBillTaskCompletedAsync(
            int requestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<BillingPreparationRequestRecord> ConfirmHourlyManuallyAsync(
            int requestId, string? note, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BillingPreparationRequestRecord> ReevaluateStageConfirmationAsync(
            int requestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<BillingPreparationSnapshotLoad> LoadComponentsAsync(
            int requestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<int>> FindHourReportIdsInOtherRequestsAsync(
            IReadOnlyList<int> hourReportIds, int? excludeRequestId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
