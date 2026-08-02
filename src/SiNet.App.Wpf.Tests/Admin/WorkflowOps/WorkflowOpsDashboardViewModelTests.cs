using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Admin.WorkflowOps;
using SiNet.Application.Identity;
using SiNet.Application.Runtime;
using SiNet.Application.Workflow;
using SiNet.Domain.Workflow;
using Xunit;

namespace SiNet.App.Wpf.Tests.Admin.WorkflowOps;

public sealed class WorkflowOpsDashboardViewModelTests
{
    [Fact]
    public async Task Refresh_marks_stalled_and_computes_summary_cards()
    {
        var completedLocal = DateTime.Today.AddHours(12);
        var createdLocal = DateTime.Today.AddHours(8);
        var active = MakeSnapshot(
            id: 11,
            name: "Proposal",
            status: WorkflowStatus.Active,
            createdUtc: DateTime.Today.AddHours(10).ToUniversalTime(),
            completedUtc: null,
            projectTitle: "Alpha",
            userName: "Dana");
        var completed = MakeSnapshot(
            id: 22,
            name: "Proposal",
            status: WorkflowStatus.Completed,
            createdUtc: createdLocal.ToUniversalTime(),
            completedUtc: completedLocal.ToUniversalTime(),
            projectTitle: "Beta",
            userName: "Avi");

        var query = new FakeQuery([active, completed], StageTaskProgressDto.Empty);
        var recovery = new FakeRecovery(
        [
            new WorkflowRecoveryCandidate(11, 1, 2, "Stage", 100, null, 1, 0),
        ]);
        var runtime = new FakeRuntime(
        [
            new SubsystemRuntimeStatus(
                "database", "מסד נתונים", SubsystemRuntimeState.Idle, null, "תקין", DateTimeOffset.UtcNow),
        ]);

        var services = new ServiceCollection().BuildServiceProvider();
        using var vm = new WorkflowOpsDashboardViewModel(query, services, recovery, runtime);

        await vm.RefreshAsync().ConfigureAwait(true);

        Assert.Equal("1", vm.ActiveCountText);
        Assert.Equal("1", vm.CompletedTodayText);
        Assert.Equal("0", vm.CancelledTodayText);
        Assert.Equal("1", vm.StalledCountText);
        Assert.Equal("Warning", vm.OverallStatusTone);
        Assert.Contains(vm.Rows, r => r.InstanceId == 11 && r.IsStalled);
        Assert.Contains(vm.Rows, r => r.InstanceId == 22 && !r.IsStalled);
        Assert.NotEqual("—", vm.AvgDurationText);
        Assert.Contains("תקינים", vm.InfraSummaryText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_filter_stalled_shows_only_stalled_rows()
    {
        var snap = MakeSnapshot(
            id: 7,
            name: "WF",
            status: WorkflowStatus.Active,
            createdUtc: DateTime.UtcNow.AddHours(-3),
            completedUtc: null,
            projectTitle: "P",
            userName: "U");
        var other = MakeSnapshot(
            id: 8,
            name: "WF",
            status: WorkflowStatus.Active,
            createdUtc: DateTime.UtcNow.AddHours(-1),
            completedUtc: null,
            projectTitle: "Q",
            userName: "V");

        var query = new FakeQuery([snap, other], StageTaskProgressDto.Empty);
        var recovery = new FakeRecovery(
        [
            new WorkflowRecoveryCandidate(7, 1, 1, "S", 1, null, 0, 0),
        ]);
        using var vm = new WorkflowOpsDashboardViewModel(
            query,
            new ServiceCollection().BuildServiceProvider(),
            recovery);

        await vm.RefreshAsync().ConfigureAwait(true);
        vm.StatusFilter = "חשוד כתקוע";

        Assert.Single(vm.Rows);
        Assert.Equal(7, vm.Rows[0].InstanceId);
        Assert.True(vm.Rows[0].IsStalled);
    }

    [Fact]
    public async Task Retry_and_cancel_commands_are_disabled_without_ports_or_selection()
    {
        var query = new FakeQuery([], StageTaskProgressDto.Empty);
        using var vm = new WorkflowOpsDashboardViewModel(
            query,
            new ServiceCollection().BuildServiceProvider());

        await vm.LoadAsync().ConfigureAwait(true);

        Assert.False(vm.RetryCommand.CanExecute(null));
        Assert.False(vm.CancelWorkflowCommand.CanExecute(null));
        Assert.False(vm.CanRetry);
        Assert.False(vm.CanCancelSelected);
    }

    [Fact]
    public async Task Retry_enabled_for_stalled_selection_when_recovery_and_user_present()
    {
        var snap = MakeSnapshot(
            id: 7,
            name: "WF",
            status: WorkflowStatus.Active,
            createdUtc: DateTime.UtcNow.AddHours(-3),
            completedUtc: null,
            projectTitle: "P",
            userName: "U");
        var query = new FakeQuery([snap], StageTaskProgressDto.Empty);
        var recovery = new FakeRecovery(
        [
            new WorkflowRecoveryCandidate(7, 1, 1, "S", 1, null, 0, 0),
        ]);
        using var vm = new WorkflowOpsDashboardViewModel(
            query,
            new ServiceCollection().BuildServiceProvider(),
            recovery,
            currentUser: new FakeUser(42));

        await vm.LoadAsync().ConfigureAwait(true);
        vm.Selected = vm.Rows.Single();

        Assert.True(vm.CanRetry);
        Assert.True(vm.RetryCommand.CanExecute(null));
    }

    private static WorkflowInstanceSnapshotDto MakeSnapshot(
        int id,
        string name,
        WorkflowStatus status,
        DateTime createdUtc,
        DateTime? completedUtc,
        string projectTitle,
        string userName)
    {
        var stage = new WorkflowStageDefinitionDto(2, "ST", "שלב", 1, false, false);
        var def = new WorkflowDefinitionDto(1, "DEF", name, true, [stage]);
        var instance = new WorkflowInstanceDto(
            id,
            1,
            100,
            status,
            2,
            createdUtc,
            completedUtc,
            null,
            def,
            stage,
            new WorkflowProjectRefDto(100, 12.3f, projectTitle),
            new WorkflowUserRefDto(5, userName),
            [
                new WorkflowStageTransitionDto(
                    1, null, 2, stage, new WorkflowUserRefDto(5, userName), createdUtc, null),
            ]);
        return new WorkflowInstanceSnapshotDto(instance, [stage], new HashSet<int> { 2 });
    }

    private sealed class FakeQuery(
        List<WorkflowInstanceSnapshotDto> snapshots,
        StageTaskProgressDto progress) : IWorkflowQueryService
    {
        public ValueTask<List<WorkflowDefinitionDto>> GetActiveDefinitionsAsync(CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<WorkflowDefinitionDto?> GetDefinitionAsync(int definitionId, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<List<WorkflowInstanceDto>> GetByProjectAsync(
            int projectId, WorkflowStatus? statusFilter, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<List<WorkflowInstanceDto>> GetActiveByProjectAsync(int projectId, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<WorkflowInstanceDto?> GetInstanceDetailAsync(int instanceId, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<List<WorkflowStageDefinitionDto>> GetAllowedNextStagesAsync(
            int instanceId, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<List<ProjectWorkflowSnapshotDto>> GetAllProjectWorkflowSnapshotsAsync(CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<List<WorkflowInstanceSnapshotDto>> GetAllWorkflowInstanceSnapshotsAsync(CancellationToken ct)
            => ValueTask.FromResult(snapshots);

        public ValueTask<List<string>> GetDistinctWorkflowNamesAsync(CancellationToken ct)
            => ValueTask.FromResult(snapshots.Select(s => s.Instance.WorkflowDefinition?.Name ?? "").Distinct().ToList());

        public ValueTask<StageTaskProgressDto> GetStageTaskProgressAsync(int instanceId, CancellationToken ct)
            => ValueTask.FromResult(progress);
    }

    private sealed class FakeRecovery(IReadOnlyList<WorkflowRecoveryCandidate> stalled) : IWorkflowRecoveryService
    {
        public ValueTask<IReadOnlyList<WorkflowRecoveryCandidate>> DetectStalledAsync(CancellationToken ct)
            => ValueTask.FromResult(stalled);

        public ValueTask<int> AttemptRecoveryAsync(
            IReadOnlyList<WorkflowRecoveryCandidate> stalledCandidates,
            int systemUserId,
            CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class FakeRuntime(IReadOnlyList<SubsystemRuntimeStatus> current) : IRuntimeSubsystemStatusService
    {
        public IReadOnlyList<SubsystemRuntimeStatus> Current { get; } = current;
        public event EventHandler? Changed;
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void StartPeriodicRefresh() { }
    }

    private sealed class FakeUser(int userId) : ICurrentUserContext
    {
        public int? UserId { get; } = userId;
    }

    private sealed class FakeCommands : IWorkflowCommandService
    {
        public ValueTask<WorkflowStartResultDto> StartAsync(StartWorkflowCommand command, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<WorkflowAdvanceResultDto> AdvanceAsync(AdvanceWorkflowCommand command, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<StageCompletionResultDto?> CheckAndAutoAdvanceAsync(TaskClosedCommand command, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<StageCompletionResultDto?> CheckAndAutoAdvanceStalledAsync(StalledWorkflowCommand command, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<StageCompletionResultDto?> CheckAndAdvanceOnActionCompletedAsync(ActionCompletedCommand command, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask<int> ReprovisionStalledStageTasksAsync(StalledWorkflowCommand command, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask PauseAsync(PauseWorkflowCommand command, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask ResumeAsync(ResumeWorkflowCommand command, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask CompleteInstanceAsync(CompleteWorkflowCommand command, CancellationToken ct)
            => throw new NotSupportedException();

        public ValueTask CancelAsync(CancelWorkflowCommand command, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
