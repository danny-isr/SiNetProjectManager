using SiNet.App.Wpf.Billing;
using SiNet.Application.Billing;
using Xunit;

namespace SiNet.App.Wpf.Tests.Billing;

public sealed class BillingPreparationLiveAmountTests
{
    private readonly MemoryBillingPreparationStore _store = new();
    private readonly MemoryBillingPreparationComponentSource _components = new();
    private readonly BillingPreparationService _service;

    public BillingPreparationLiveAmountTests()
    {
        _service = new BillingPreparationService(
            _store,
            _components,
            new MemoryBillingPreparationProjectMapper(12),
            new MemoryBillingPreparationTaskPort(),
            new FixedBillingPreparationActor(new BillingActor(7, "manager")),
            new FrozenUtcTimeProvider(new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Whole_shekels_omit_agorot()
    {
        Assert.Equal("₪ 52,430", BillingMoneyFormatter.FormatShekels(52430m));
        Assert.Equal("₪ 0", BillingMoneyFormatter.FormatShekels(0m));
        Assert.Equal("₪ 9,346.15", BillingMoneyFormatter.FormatShekels(9346.15m));
    }

    [Fact]
    public void Fixed_price_uses_delta_not_cumulative_progress()
    {
        var draft = Stage(billable: 2_200_000m, weight: 0.25m);
        var priced = BillingPreparationAmountCalculator.StageAddition(draft, 0.20m);
        Assert.True(priced.IsPriced);
        Assert.Equal(110_000m, priced.Amount);
    }

    [Fact]
    public void Discounted_fixed_price_reconciles_to_historical_bill_line()
    {
        var draft = Stage(billable: 155769.23m, weight: 0.10m, discount: 0.40m);
        var priced = BillingPreparationAmountCalculator.StageAddition(draft, 1m);
        Assert.Equal(9346.1538m, priced.Amount);
        Assert.Equal("₪ 9,346.15", BillingMoneyFormatter.FormatShekels(priced.Amount!.Value));
    }

    [Fact]
    public void Two_stage_additions_sum_without_normalizing_weights()
    {
        var a = Stage(1, 10, "א", billable: 2_200_000m, weight: 0.25m);
        var b = Stage(2, 10, "ב", billable: 2_200_000m, weight: 0.04m);
        var summary = BillingPreparationAmountCalculator.Summarize(
            [(a, 0.20m), (b, 0.35m)],
            []);
        Assert.Equal(110_000m + 30_800m, summary.PricedStageTotal);
        Assert.Equal(140_800m, summary.PricedTotal);
        Assert.False(summary.IsPartial);
    }

    [Fact]
    public void Hourly_unique_rate_is_hours_times_rate()
    {
        var draft = new BillingHourlySubContractDraft(
            11001, "תנועה", MasterPlanSnapshotFeeTypeIds.WorkingHours, UniqueHourlyRate: 280m);
        var priced = BillingPreparationAmountCalculator.Hourly(draft, 147.5m);
        Assert.Equal(41_300m, priced.Amount);
    }

    [Fact]
    public void Multi_rate_hourly_is_unavailable_not_zero()
    {
        var draft = new BillingHourlySubContractDraft(
            99, "מעורב", MasterPlanSnapshotFeeTypeIds.WorkingHours,
            AmountUnavailableReason: "תעריף לא ניתן לקביעה");
        var priced = BillingPreparationAmountCalculator.Hourly(draft, 10m);
        Assert.False(priced.IsPriced);
        var summary = BillingPreparationAmountCalculator.Summarize([], [(draft, 10m, "מעורב")]);
        Assert.True(summary.IsPartial);
        Assert.Equal(0m, summary.PricedHoursTotal);
        Assert.Contains("תעריף לא ניתן לקביעה", summary.Missing[0].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_fixed_price_is_unavailable_not_zero()
    {
        var draft = Stage(billable: null, weight: 0.25m);
        var priced = BillingPreparationAmountCalculator.StageAddition(draft, 0.20m);
        Assert.False(priced.IsPriced);
        var summary = BillingPreparationAmountCalculator.Summarize([(draft, 0.20m)], []);
        Assert.True(summary.IsPartial);
        Assert.Equal(0m, summary.PricedTotal);
        Assert.Equal("לא נמצא בסיס תמחור", summary.Missing[0].Reason);
    }

    [Fact]
    public void Indexation_is_unavailable()
    {
        var draft = Stage(billable: 2_200_000m, weight: 0.25m) with { HasUnpricedIndexation = true };
        var priced = BillingPreparationAmountCalculator.StageAddition(draft, 0.20m);
        Assert.False(priced.IsPriced);
        Assert.Contains("הצמדה", priced.UnavailableReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stage_addition_recalculates_immediately_without_save()
    {
        _components.Load = PricedCatalog();
        await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        Assert.Equal("₪ 0", vm.PreparationAmountMainText);
        Assert.Equal("סכום החשבון להכנה", vm.PreparationAmountTitle);

        var stage = vm.StageEdits.Single(s => s.MasterPlanStageId == 7390);
        stage.AdditionPercent = 20m;
        Assert.Contains("₪ 110,000", stage.AdditionAmountText, StringComparison.Ordinal);
        Assert.Equal("₪ 110,000", vm.PreparationStageAmountText);
        Assert.Equal("₪ 110,000", vm.PreparationAmountMainText);
        Assert.True(vm.IsDirty);
        Assert.Empty((await _store.GetByIdAsync(1))!.Stages);
    }

    [Fact]
    public async Task Second_stage_and_subcontract_totals_add()
    {
        _components.Load = PricedCatalog();
        await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        vm.StageEdits.Single(s => s.MasterPlanStageId == 7390).AdditionPercent = 20m;
        vm.StageEdits.Single(s => s.MasterPlanStageId == 7388).AdditionPercent = 35m;
        Assert.Equal("₪ 140,800", vm.PreparationStageAmountText);
        var group = vm.StageGroups.Single(g => g.SubContractId == 3853);
        Assert.Contains("₪ 140,800", group.HeaderAdditionMoneyText, StringComparison.Ordinal);
        Assert.Contains("₪ 140,800", group.AdditionMoneyText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Multiple_contracts_total_together()
    {
        _components.Load = TwoContractPricedCatalog();
        await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        vm.StageEdits.Single(s => s.MasterPlanStageId == 1).AdditionPercent = 10m;
        vm.StageEdits.Single(s => s.MasterPlanStageId == 2).AdditionPercent = 10m;
        Assert.Equal("₪ 150,000", vm.PreparationAmountMainText);
    }

    [Fact]
    public async Task Hourly_money_and_clear_all()
    {
        _components.Load = HourlyPricedCatalog();
        await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        vm.AddHourlyScopeCommand.Execute(null);
        var scope = Assert.Single(vm.HourlyScopeEdits);
        scope.Choices.Single(c => c.MasterPlanSubContractId == 100).IsSelected = true;
        scope.FromDate = new DateTime(2026, 8, 1);
        scope.ToDate = new DateTime(2026, 8, 31);
        Assert.Equal("₪ 700", vm.PreparationHoursAmountText);
        Assert.Equal("₪ 700", vm.PreparationAmountMainText);
        Assert.Contains("₪ 700", scope.HoursAmountText, StringComparison.Ordinal);
        Assert.True(scope.ShowUnifiedHourlyRate);

        scope.Choices.Single(c => c.MasterPlanSubContractId == 200).IsSelected = true;
        Assert.Equal("₪ 1,820", vm.PreparationHoursAmountText);

        scope.ClearAll();
        Assert.Equal("₪ 0", vm.PreparationHoursAmountText);
        Assert.Equal("₪ 0", vm.PreparationAmountMainText);
        Assert.True(vm.IsDirty);
        Assert.Empty((await _store.GetByIdAsync(1))!.Hours);
    }

    [Fact]
    public async Task Mixed_stage_and_hours_total_and_deselect_subtracts()
    {
        _components.Load = MixedPricedCatalog();
        await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        vm.StageEdits.Single(s => s.MasterPlanStageId == 7390).AdditionPercent = 20m;
        vm.AddHourlyScopeCommand.Execute(null);
        var scope = Assert.Single(vm.HourlyScopeEdits);
        scope.Choices.Single(c => c.MasterPlanSubContractId == 100).IsSelected = true;
        scope.FromDate = new DateTime(2026, 8, 1);
        scope.ToDate = new DateTime(2026, 8, 31);
        Assert.Equal("₪ 110,000", vm.PreparationStageAmountText);
        Assert.Equal("₪ 700", vm.PreparationHoursAmountText);
        Assert.Equal("₪ 110,700", vm.PreparationAmountMainText);

        scope.Included = false;
        Assert.Equal("₪ 110,000", vm.PreparationAmountMainText);
        Assert.Equal("₪ 0", vm.PreparationHoursAmountText);
        Assert.Empty((await _store.GetByIdAsync(1))!.Stages);
        Assert.Empty((await _store.GetByIdAsync(1))!.Hours);
    }

    [Fact]
    public async Task Unknown_pricing_is_partial_and_blocks_approve()
    {
        _components.Load = UnpricedCatalog();
        await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        vm.StageEdits[0].AdditionPercent = 20m;
        Assert.True(vm.LiveAmount.IsPartial);
        Assert.Equal("סכום מחושב חלקית", vm.PreparationAmountTitle);
        Assert.Equal("₪ 0", vm.PreparationAmountMainText);
        Assert.Contains("לא נמצא בסיס תמחור", vm.PreparationAmountMissingDetails, StringComparison.Ordinal);
        Assert.False(vm.ApprovePreparationCommand.CanExecute(null));
        Assert.True(vm.IsDirty);
        Assert.Empty((await _store.GetByIdAsync(1))!.Stages);
    }

    [Fact]
    public async Task Save_and_reload_reproduces_the_same_amount()
    {
        _components.Load = PricedCatalog();
        await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        vm.StageEdits.Single(s => s.MasterPlanStageId == 7390).AdditionPercent = 20m;
        await vm.SavePreparationAsync();
        Assert.False(vm.IsDirty);
        Assert.Single((await _store.GetByIdAsync(1))!.Stages);

        var reloaded = CreateVm();
        await reloaded.RefreshPreparationAsync();
        Assert.Equal(20m, reloaded.StageEdits.Single(s => s.MasterPlanStageId == 7390).AdditionPercent);
        Assert.Equal("₪ 110,000", reloaded.PreparationAmountMainText);
        Assert.False(reloaded.IsDirty);
    }

    [Fact]
    public void Calculator_does_not_use_a_contract_value_heuristic()
    {
        var draft = Stage(billable: 2_200_000m, weight: 0.25m);
        var priced = BillingPreparationAmountCalculator.StageAddition(draft, 0.20m);
        Assert.NotEqual(5_000_000m * 0.20m, priced.Amount);
        Assert.Equal(2_200_000m * 0.25m * 0.20m, priced.Amount);
    }

    private BillingDashboardViewModel CreateVm() =>
        new(new UnusedDashboardRead(), preparation: _service);

    private static BillingPreparationSnapshotLoad PricedCatalog() =>
        new(
            true,
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            "לקוח",
            "1844",
            "פרויקט",
            [
                Stage(7390, 3853, "פיקוח עליון על הביצוע", billable: 2_200_000m, weight: 0.25m, observed: 0.10m),
                Stage(7388, 3853, "שלב שני", billable: 2_200_000m, weight: 0.04m, observed: 0m)
            ],
            [],
            []);

    private static BillingPreparationSnapshotLoad TwoContractPricedCatalog() =>
        new(
            true,
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            "לקוח",
            "1844",
            "פרויקט",
            [
                Stage(1, 10, "א", billable: 1_000_000m, weight: 1m, contractId: 1, contractName: "א"),
                Stage(2, 20, "ב", billable: 500_000m, weight: 1m, contractId: 2, contractName: "ב")
            ],
            [],
            []);

    private static BillingPreparationSnapshotLoad HourlyPricedCatalog() =>
        new(
            true,
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            "לקוח",
            "1844",
            "פרויקט",
            [],
            [
                new BillingHourlySubContractDraft(
                    100, "תנועה", MasterPlanSnapshotFeeTypeIds.WorkingHours, UniqueHourlyRate: 280m),
                new BillingHourlySubContractDraft(
                    200, "כבישים", MasterPlanSnapshotFeeTypeIds.WorkingHours, UniqueHourlyRate: 280m)
            ],
            [
                new BillingHourReportFact(11, 4608, 100, 1, new DateTime(2026, 8, 1), 1, "A", 2.5m, "x"),
                new BillingHourReportFact(14, 4608, 200, 1, new DateTime(2026, 8, 10), 1, "B", 4m, "y")
            ]);

    private static BillingPreparationSnapshotLoad MixedPricedCatalog() =>
        PricedCatalog() with
        {
            HourlySubContracts = HourlyPricedCatalog().HourlySubContracts,
            HourReports = HourlyPricedCatalog().HourReports
        };

    private static BillingPreparationSnapshotLoad UnpricedCatalog() =>
        new(
            true,
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            "לקוח",
            "1844",
            "פרויקט",
            [Stage(7390, 3853, "פיקוח עליון על הביצוע", billable: null, weight: 0.25m)],
            [],
            []);

    private static BillingPreparationStageDraft Stage(
        int stageId = 7390,
        int subId = 3853,
        string name = "פיקוח עליון על הביצוע",
        decimal? billable = 2_200_000m,
        decimal weight = 0.25m,
        decimal discount = 0m,
        decimal observed = 0.10m,
        int contractId = 1,
        string? contractName = "חוזה")
    {
        var observedProgress = BillingStageProgressCalculator.Observe([observed]);
        return new BillingPreparationStageDraft(
            stageId,
            subId,
            name,
            "תכנון פיזי",
            weight,
            observedProgress,
            observedProgress.Value,
            false,
            MasterPlanSnapshotFeeTypeIds.FixedPrice,
            contractId,
            contractName,
            SubContractBillableAmount: billable,
            DiscountFraction: discount);
    }

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
}
