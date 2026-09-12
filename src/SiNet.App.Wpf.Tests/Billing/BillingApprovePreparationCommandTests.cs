using SiNet.App.Wpf.Billing;
using SiNet.Application.Billing;
using Xunit;

namespace SiNet.App.Wpf.Tests.Billing;

public sealed class BillingApprovePreparationCommandTests
{
    private readonly MemoryBillingPreparationStore _store = new();
    private readonly MemoryBillingPreparationComponentSource _components = new();
    private readonly MemoryBillingPreparationTaskPort _tasks = new();
    private readonly BillingPreparationService _service;

    public BillingApprovePreparationCommandTests()
    {
        _service = new BillingPreparationService(
            _store,
            _components,
            new MemoryBillingPreparationProjectMapper(12),
            _tasks,
            new FixedBillingPreparationActor(new BillingActor(7, "manager")),
            new FrozenUtcTimeProvider(new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task ApprovePreparationCommand_executes_approve_once_and_replaces_selection()
    {
        _components.Load = MixedCatalog();
        await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        var spy = new ApproveCountingPreparation(_service);
        var vm = CreateVm(spy);
        await vm.RefreshPreparationAsync();
        await SaveCompleteSelectionAsync(vm);

        Assert.False(vm.IsDirty);
        Assert.False(vm.LiveAmount.IsPartial);
        Assert.Equal(BillingPreparationStatus.ReadyForApproval, vm.SelectedPreparation!.Status);
        Assert.True(vm.ApprovePreparationCommand.CanExecute(null));

        vm.ApprovePreparationCommand.Execute(null);
        await WaitUntilAsync(() =>
            vm.SelectedPreparation?.Status == BillingPreparationStatus.TaskOpen
            || !string.IsNullOrEmpty(vm.OperationErrorMessage));

        Assert.Equal(1, spy.ApproveCalls);
        Assert.Equal(1, _tasks.CreateCalls);
        Assert.Equal(BillingPreparationStatus.TaskOpen, vm.SelectedPreparation!.Status);
        Assert.NotNull(vm.SelectedPreparation.Source.TaskId);
        Assert.Contains(
            $"נפתחה משימת הכנת חשבון #{vm.SelectedPreparation.Source.TaskId}",
            vm.StatusMessage,
            StringComparison.Ordinal);
        Assert.Equal(string.Empty, vm.OperationErrorMessage);
    }

    [Fact]
    public async Task ApprovePreparationCommand_surfaces_service_exception_as_operation_error()
    {
        _components.Load = MixedCatalog();
        await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        var spy = new ApproveCountingPreparation(_service);
        var vm = CreateVm(spy);
        await vm.RefreshPreparationAsync();
        await SaveCompleteSelectionAsync(vm);
        _tasks.ThrowOnCreate = true;

        Assert.True(vm.ApprovePreparationCommand.CanExecute(null));
        vm.ApprovePreparationCommand.Execute(null);
        await WaitUntilAsync(() => !string.IsNullOrEmpty(vm.OperationErrorMessage));

        Assert.Equal(1, spy.ApproveCalls);
        Assert.Contains("יצירת משימת הכנת חשבון נכשלה", vm.OperationErrorMessage, StringComparison.Ordinal);
        Assert.Equal(BillingPreparationStatus.ReadyForApproval, vm.SelectedPreparation!.Status);
        Assert.Null(vm.SelectedPreparation.Source.TaskId);
    }

    private static async Task SaveCompleteSelectionAsync(BillingDashboardViewModel vm)
    {
        vm.StageEdits.Single(s => s.MasterPlanStageId == 7390).AdditionPercent = 20m;
        vm.AddHourlyScopeCommand.Execute(null);
        var scope = Assert.Single(vm.HourlyScopeEdits);
        scope.Choices.Single(c => c.MasterPlanSubContractId == 14317).IsSelected = true;
        scope.FromDate = new DateTime(2026, 6, 2);
        scope.ToDate = new DateTime(2026, 6, 2);
        await vm.SavePreparationAsync();
        Assert.Equal(string.Empty, vm.OperationErrorMessage);
    }

    private BillingDashboardViewModel CreateVm(IBillingPreparationService preparation) =>
        new(new UnusedDashboardRead(), preparation: preparation);

    private static async Task WaitUntilAsync(Func<bool> done)
    {
        for (var i = 0; i < 50 && !done(); i++)
            await Task.Delay(20);
        Assert.True(done());
    }

    private static BillingPreparationSnapshotLoad MixedCatalog() =>
        new(
            true,
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            "לקוח",
            "1844",
            "פרויקט",
            [
                new BillingPreparationStageDraft(
                    7390, 3853, "פיקוח עליון על הביצוע", "תכנון פיזי", 0.25m,
                    BillingStageProgressCalculator.Observe([0.10m]),
                    0.10m,
                    Included: false,
                    MasterPlanSnapshotFeeTypeIds.FixedPrice,
                    SubContractBillableAmount: 2_200_000m)
            ],
            [
                new BillingHourlySubContractDraft(
                    14317, "פיקוח עליון", MasterPlanSnapshotFeeTypeIds.WorkingHours, UniqueHourlyRate: 280m)
            ],
            [
                new BillingHourReportFact(57875, 4608, 14317, 7390, new DateTime(2026, 6, 2), 1, "A", 1m, "x")
            ]);

    private sealed class UnusedDashboardRead : IBillingDashboardReadService
    {
        public Task<BillingDashboardResult> GetAsync(
            BillingDashboardRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new BillingDashboardResult(
                new BillingDashboardSummary(0, 0, null, null, null, null),
                [],
                new BillingSourceFreshness(null, null, null, null, null, null, null, null),
                [],
                ReplicaConnectionDiagnostics.Empty,
                BillingReplicaFreshnessStatus.Healthy,
                CandidatesBlocked: false));
    }

    private sealed class ApproveCountingPreparation(IBillingPreparationService inner) : IBillingPreparationService
    {
        public int ApproveCalls { get; private set; }

        public Task<BillingPreparationEnsureResult> EnsureFromPrepareBillAsync(
            int masterPlanProjectId,
            string? projectNumber,
            string? projectName,
            string? customerName,
            CancellationToken cancellationToken = default) =>
            inner.EnsureFromPrepareBillAsync(
                masterPlanProjectId, projectNumber, projectName, customerName, cancellationToken);

        public Task<IReadOnlyList<BillingPreparationRequestRecord>> ListForPreparationTabAsync(
            CancellationToken cancellationToken = default) =>
            inner.ListForPreparationTabAsync(cancellationToken);

        public Task<BillingPreparationRequestRecord> RefreshFromSnapshotAsync(
            int requestId,
            CancellationToken cancellationToken = default) =>
            inner.RefreshFromSnapshotAsync(requestId, cancellationToken);

        public Task<BillingPreparationRequestRecord> SaveSelectionAsync(
            int requestId,
            IReadOnlyList<BillingPreparationStageLineSnapshot> stages,
            IReadOnlyList<BillingPreparationHoursLineSnapshot> hours,
            CancellationToken cancellationToken = default) =>
            inner.SaveSelectionAsync(requestId, stages, hours, cancellationToken);

        public Task<BillingPreparationRequestRecord> ApplyManualOverrideAsync(
            int requestId,
            string reason,
            CancellationToken cancellationToken = default) =>
            inner.ApplyManualOverrideAsync(requestId, reason, cancellationToken);

        public Task<BillingPreparationApproveResult> ApproveAndCreateTaskAsync(
            int requestId,
            CancellationToken cancellationToken = default)
        {
            ApproveCalls++;
            return inner.ApproveAndCreateTaskAsync(requestId, cancellationToken);
        }

        public Task<BillingPreparationRequestRecord> OnPrepareBillTaskCompletedAsync(
            int requestId,
            CancellationToken cancellationToken = default) =>
            inner.OnPrepareBillTaskCompletedAsync(requestId, cancellationToken);

        public Task<BillingPreparationRequestRecord> ConfirmHourlyManuallyAsync(
            int requestId,
            string? note,
            CancellationToken cancellationToken = default) =>
            inner.ConfirmHourlyManuallyAsync(requestId, note, cancellationToken);

        public Task<BillingPreparationRequestRecord> ReevaluateStageConfirmationAsync(
            int requestId,
            CancellationToken cancellationToken = default) =>
            inner.ReevaluateStageConfirmationAsync(requestId, cancellationToken);

        public Task OnPrepareBillTaskCompletedByTaskIdAsync(
            int taskId,
            CancellationToken cancellationToken = default) =>
            inner.OnPrepareBillTaskCompletedByTaskIdAsync(taskId, cancellationToken);

        public Task<BillingPreparationSnapshotLoad> LoadComponentsAsync(
            int requestId,
            CancellationToken cancellationToken = default) =>
            inner.LoadComponentsAsync(requestId, cancellationToken);

        public Task<IReadOnlyList<int>> FindHourReportIdsInOtherRequestsAsync(
            IReadOnlyList<int> hourReportIds,
            int? excludeRequestId,
            CancellationToken cancellationToken = default) =>
            inner.FindHourReportIdsInOtherRequestsAsync(hourReportIds, excludeRequestId, cancellationToken);
    }
}
