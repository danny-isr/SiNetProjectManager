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

    [Fact]
    public async Task Whole_project_summary_updates_live_and_discard_restores_without_db_write()
    {
        _components.Load = PricedCatalog();
        await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        var vm = CreateVm(new Project1844DashboardRead(), prompts: new DiscardPrompts());
        await vm.RefreshAsync();
        await vm.RefreshPreparationAsync();

        Assert.Equal("₪ 4,142,969.59", vm.ProjectTotalFeeText);
        Assert.Equal("₪ 1,189,568.98", vm.ProjectBalanceBeforeText);
        Assert.Equal("₪ 2,953,400.61", vm.ProjectAlreadyBilledText);
        Assert.Equal("₪ 0", vm.ProjectCurrentBillText);
        Assert.Equal("₪ 1,189,568.98", vm.ProjectBalanceAfterText);
        Assert.True(vm.ShowProjectFinancialBar);
        Assert.Equal("יתרה לפי snapshot MasterPlan מ־10/09/2026", vm.ProjectFinancialSourceText);
        Assert.True(vm.ShowProjectFinancialHourlyNote);
        Assert.Empty((await _store.GetByIdAsync(1))!.Stages);

        vm.StageEdits.Single(s => s.MasterPlanStageId == 7390).AdditionPercent = 20m;
        Assert.Equal("₪ 110,000", vm.ProjectCurrentBillText);
        Assert.Equal("₪ 1,079,568.98", vm.ProjectBalanceAfterText);
        Assert.Equal(110_000m, vm.ProjectAdditionBarShare);
        Assert.Equal(1_079_568.98m, vm.ProjectRemainingBarShare);
        Assert.True(vm.IsDirty);
        Assert.Empty((await _store.GetByIdAsync(1))!.Stages);

        vm.StageEdits.Single(s => s.MasterPlanStageId == 7390).AdditionPercent = 21m;
        Assert.Equal("₪ 115,500", vm.ProjectCurrentBillText);
        Assert.Equal("₪ 1,074,068.98", vm.ProjectBalanceAfterText);
        Assert.Equal(115_500m, vm.ProjectAdditionBarShare);
        Assert.True(vm.ProjectRemainingBarShare < 1_079_568.98m);
        Assert.Empty((await _store.GetByIdAsync(1))!.Stages);

        await vm.RefreshPreparationAsync();
        Assert.Equal(0m, vm.StageEdits.Single(s => s.MasterPlanStageId == 7390).AdditionPercent);
        Assert.Equal("₪ 0", vm.ProjectCurrentBillText);
        Assert.Equal("₪ 1,189,568.98", vm.ProjectBalanceAfterText);
        Assert.False(vm.IsDirty);
        Assert.Empty((await _store.GetByIdAsync(1))!.Stages);
    }

    [Fact]
    public async Task Whole_project_summary_stays_unavailable_when_dashboard_has_no_candidate()
    {
        _components.Load = PricedCatalog();
        await _service.EnsureFromPrepareBillAsync(4608, "1844", "פרויקט", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        vm.StageEdits.Single(s => s.MasterPlanStageId == 7390).AdditionPercent = 20m;

        Assert.Equal(BillingProjectFinancialSummaryCalculator.UnavailableFee, vm.ProjectTotalFeeText);
        Assert.Equal(BillingProjectFinancialSummaryCalculator.UnavailableBalance, vm.ProjectBalanceBeforeText);
        Assert.Equal("₪ 110,000", vm.ProjectCurrentBillText);
        Assert.Equal(BillingProjectFinancialSummaryCalculator.UnavailableBalance, vm.ProjectBalanceAfterText);
        Assert.False(vm.ShowProjectFinancialBar);
        Assert.DoesNotContain("₪ 0", vm.ProjectTotalFeeText, StringComparison.Ordinal);
    }

    private BillingDashboardViewModel CreateVm(
        IBillingDashboardReadService? dashboard = null,
        IBillingReviewPrompts? prompts = null) =>
        new(dashboard ?? new UnusedDashboardRead(), preparation: _service, prompts: prompts);

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

    private sealed class DiscardPrompts : IBillingReviewPrompts
    {
        public bool ConfirmPrepareBill(string projectLabel) => true;

        public BillingNotNowPromptResult? PromptNotNow(string projectLabel) => null;

        public bool ConfirmClearDecision(string projectLabel) => true;

        public BillingUnsavedEditsDecision ConfirmDiscardUnsavedPreparationEdits() =>
            BillingUnsavedEditsDecision.Discard;
    }

    private sealed class Project1844DashboardRead : IBillingDashboardReadService
    {
        public Task<BillingDashboardResult> GetAsync(
            BillingDashboardRequest request,
            CancellationToken cancellationToken = default)
        {
            var snapshotDate = new DateTime(2026, 9, 10);
            var fees = new BillingFeeTypeSummary(
                [MasterPlanSnapshotFeeTypeIds.FixedPrice, MasterPlanSnapshotFeeTypeIds.WorkingHours],
                [
                    new BillingFeeTypeCount(MasterPlanSnapshotFeeTypeIds.FixedPrice, "מחיר קבוע", 29),
                    new BillingFeeTypeCount(MasterPlanSnapshotFeeTypeIds.WorkingHours, "שעות עבודה", 3)
                ],
                BillingSnapshotFeeMix.Mixed);
            var row = new BillingCandidateRow(
                4608,
                "1844",
                "פרויקט",
                "לקוח",
                "פעיל",
                4_142_969.59m,
                new DateTime(2026, 9, 1),
                10m, 10m, 10m, 10m, 3,
                new DateTime(2026, 8, 31),
                10,
                13137,
                "2857",
                MasterPlanBillStatusIds.Submitted,
                "הוגש",
                294502.42m,
                0,
                2,
                0,
                2,
                1_189_568.9787m,
                391_942.4167m,
                0m,
                71.28m,
                snapshotDate,
                fees,
                BillingCandidateState.ReviewNow,
                "שעות אחרי חשבון");
            return Task.FromResult(new BillingDashboardResult(
                new BillingDashboardSummary(1, 0, null, 0m, snapshotDate, snapshotDate),
                [row],
                new BillingSourceFreshness(snapshotDate, snapshotDate, snapshotDate, snapshotDate, snapshotDate, null, snapshotDate, snapshotDate),
                [],
                ReplicaConnectionDiagnostics.Empty,
                BillingReplicaFreshnessStatus.Healthy,
                CandidatesBlocked: false));
        }
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
