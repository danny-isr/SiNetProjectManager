using SiNet.App.Wpf.Billing;
using SiNet.Application.Billing;
using Xunit;

namespace SiNet.App.Wpf.Tests.Billing;

public sealed class BillingPreparationStageEditorFilterTests
{
    [Fact]
    public void Completed_100_percent_is_not_editable()
    {
        var draft = Stage(0.20m, BillingStageProgressCalculator.Observe([1.00m]));
        var decision = BillingPreparationStageEditorFilter.Classify(draft);
        Assert.False(decision.IsEditable);
        Assert.Equal(BillingPreparationStageExclusionKind.Completed, decision.Exclusion);
        Assert.False(decision.ShowInDataQualityWarning);
    }

    [Fact]
    public void Hourly_fee_type_is_not_in_stage_editor()
    {
        var draft = Stage(0.20m, BillingStageProgressCalculator.Observe([0.10m]), feeTypeId: 4);
        var decision = BillingPreparationStageEditorFilter.Classify(draft);
        Assert.False(decision.IsEditable);
        Assert.Equal(BillingPreparationStageExclusionKind.HourlyFeeType, decision.Exclusion);
    }

    [Fact]
    public void Zero_weight_is_not_editable_and_is_warned()
    {
        var draft = Stage(0m, BillingStageProgressCalculator.Observe([0.10m]));
        var decision = BillingPreparationStageEditorFilter.Classify(draft);
        Assert.False(decision.IsEditable);
        Assert.Equal(BillingPreparationStageExclusionKind.NonPositiveWeight, decision.Exclusion);
        Assert.True(decision.ShowInDataQualityWarning);
    }

    [Fact]
    public void Outlier_is_not_treated_as_zero()
    {
        var draft = Stage(0.20m, BillingStageProgressCalculator.Observe([1.06m]));
        var decision = BillingPreparationStageEditorFilter.Classify(draft);
        Assert.False(decision.IsEditable);
        Assert.Equal(BillingPreparationStageExclusionKind.DataQuality, decision.Exclusion);
        Assert.True(decision.ShowInDataQualityWarning);
        Assert.False(draft.Observed.CanUseForAutomaticCalculation);
    }

    [Fact]
    public void Unknown_observed_without_outliers_is_editable()
    {
        var draft = Stage(0.25m, BillingStageProgressCalculator.Observe([]));
        Assert.True(BillingPreparationStageEditorFilter.IsEditable(draft));
    }

    private static BillingPreparationStageDraft Stage(
        decimal weight,
        BillingStageProgressCalculator.ObservedCumulativeProgress observed,
        int feeTypeId = MasterPlanSnapshotFeeTypeIds.FixedPrice) =>
        new(1, 10, "פיקוח על הביצוע", "תכנון", weight, observed, observed.Value, false, feeTypeId);
}

public sealed class BillingStageConfirmationEvaluatorTests
{
    [Fact]
    public void Auto_confirm_compares_newer_progress_to_cumulative_target()
    {
        var approved = new BillingPreparationStageLineSnapshot(
            1, 10, "שלב", "הסכם", 0.25m,
            0.10m, 0.30m, 0.20m,
            false,
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            BillingConfirmationMode.None, null, null, null);
        var newer = BillingStageProgressCalculator.Observe([0.30m]);
        Assert.True(BillingStageConfirmationEvaluator.CanAutoConfirm(
            approved,
            newer,
            new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.False(BillingStageConfirmationEvaluator.CanAutoConfirm(
            approved,
            BillingStageProgressCalculator.Observe([0.29m]),
            new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)));
    }
}

public sealed class BillingPreparationStageAdditionUiTests
{
    private readonly MemoryBillingPreparationStore _store = new();
    private readonly MemoryBillingPreparationComponentSource _components = new();
    private readonly BillingPreparationService _service;

    public BillingPreparationStageAdditionUiTests()
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
    public async Task Observed_10_addition_20_saves_target_30()
    {
        _components.Load = Catalog(StageDraft(0.10m, 0.25m));
        await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        var stage = Assert.Single(vm.StageEdits);
        stage.AdditionPercent = 20m;
        Assert.Equal(10m, stage.ObservedPercent);
        Assert.Equal(30m, stage.AfterBillPercent);
        Assert.Equal(70m, stage.RemainingPercent);
        Assert.Equal(5m, stage.RelativeSharePercent);
        await vm.SavePreparationAsync();
        var saved = Assert.Single((await _store.GetByIdAsync(1))!.Stages);
        Assert.Equal(0.10m, saved.ObservedCumulativeProgress);
        Assert.Equal(0.20m, saved.RequestedDelta);
        Assert.Equal(0.30m, saved.TargetCumulativeProgress);
    }

    [Fact]
    public async Task Zero_addition_is_not_persisted()
    {
        _components.Load = Catalog(StageDraft(0.10m, 0.25m));
        await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        Assert.Single(vm.StageEdits);
        await vm.SavePreparationAsync();
        Assert.Empty((await _store.GetByIdAsync(1))!.Stages);
    }

    [Fact]
    public async Task Only_positive_additions_are_persisted()
    {
        _components.Load = Catalog(
            StageDraft(0.10m, 0.25m, stageId: 1, name: "א"),
            StageDraft(0.20m, 0.25m, stageId: 2, name: "ב"));
        await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        vm.StageEdits[0].AdditionPercent = 20m;
        vm.StageEdits[1].AdditionPercent = 0m;
        await vm.SavePreparationAsync();
        var saved = Assert.Single((await _store.GetByIdAsync(1))!.Stages);
        Assert.Equal(1, saved.MasterPlanStageId);
        Assert.Equal(0.20m, saved.RequestedDelta);
    }

    [Fact]
    public async Task Reload_preserves_addition_from_requested_delta()
    {
        _components.Load = Catalog(StageDraft(0.10m, 0.25m));
        await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        var first = CreateVm();
        await first.RefreshPreparationAsync();
        first.StageEdits[0].AdditionPercent = 20m;
        await first.SavePreparationAsync();

        var reloaded = CreateVm();
        await reloaded.RefreshPreparationAsync();
        var stage = Assert.Single(reloaded.StageEdits);
        Assert.Equal(20m, stage.AdditionPercent);
        Assert.Equal(30m, stage.AfterBillPercent);
        Assert.Equal(0.20m, stage.RequestedDelta);
    }

    [Fact]
    public async Task Completed_and_hourly_and_outlier_rows_are_filtered()
    {
        _components.Load = Catalog(
            StageDraft(0.10m, 0.25m, stageId: 1, name: "ניתן"),
            StageDraft(1.00m, 0.25m, stageId: 2, name: "הושלם"),
            StageDraft(0.10m, 0.25m, stageId: 3, name: "שעתי", feeTypeId: MasterPlanSnapshotFeeTypeIds.WorkingHours),
            StageDraft(1.06m, 0.25m, stageId: 4, name: "חריג", observeRaw: true),
            StageDraft(0.10m, 0m, stageId: 5, name: "ללא משקל"));
        await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        var editable = Assert.Single(vm.StageEdits);
        Assert.Equal("ניתן", editable.StageName);
        Assert.True(vm.ShowStageExclusionWarning);
        Assert.Contains("אינם זמינים לחיוב אוטומטי", vm.StageExclusionWarningHeader, StringComparison.Ordinal);
        Assert.Contains("חריג", vm.StageExclusionDetails, StringComparison.Ordinal);
        Assert.DoesNotContain("שעתי", vm.StageEdits.Select(s => s.StageName));
        Assert.DoesNotContain("הושלם", vm.StageEdits.Select(s => s.StageName));
    }

    [Fact]
    public async Task Invalid_addition_is_rejected_by_save()
    {
        _components.Load = Catalog(StageDraft(0.10m, 0.25m));
        await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        vm.StageEdits[0].AdditionPercent = 91m;
        await vm.SavePreparationAsync();
        Assert.False(string.IsNullOrEmpty(vm.OperationErrorMessage));
        Assert.Empty((await _store.GetByIdAsync(1))!.Stages);
    }

    private BillingDashboardViewModel CreateVm() =>
        new(new UnusedDashboardRead(), preparation: _service);

    private static BillingPreparationSnapshotLoad Catalog(params BillingPreparationStageDraft[] stages) =>
        new(
            true,
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            "לקוח",
            "2608",
            "פרויקט",
            stages,
            [],
            []);

    private static BillingPreparationStageDraft StageDraft(
        decimal observedOrOutlier,
        decimal weight,
        int stageId = 1,
        string name = "פיקוח על הביצוע",
        int feeTypeId = MasterPlanSnapshotFeeTypeIds.FixedPrice,
        bool observeRaw = false)
    {
        var observed = observeRaw
            ? BillingStageProgressCalculator.Observe([observedOrOutlier])
            : BillingStageProgressCalculator.Observe([observedOrOutlier]);
        return new BillingPreparationStageDraft(
            stageId, 10, name, "תכנון", weight, observed, observed.Value, false, feeTypeId);
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

public sealed class BillingPreparationHourlyMultiSelectUiTests
{
    private readonly MemoryBillingPreparationStore _store = new();
    private readonly MemoryBillingPreparationComponentSource _components = new();
    private readonly BillingPreparationService _service;

    public BillingPreparationHourlyMultiSelectUiTests()
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
    public async Task Two_subcontracts_persist_two_hours_lines()
    {
        _components.Load = MultiHourSnapshot();
        await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        vm.AddHourlyScopeCommand.Execute(null);
        var edit = Assert.Single(vm.HourlyScopeEdits);
        edit.FromDate = new DateTime(2026, 8, 1);
        edit.ToDate = new DateTime(2026, 8, 31);
        edit.ClearAll();
        edit.Choices.Single(c => c.MasterPlanSubContractId == 100).IsSelected = true;
        edit.Choices.Single(c => c.MasterPlanSubContractId == 200).IsSelected = true;
        Assert.Equal(2, edit.SelectedSubContractCount);
        Assert.Equal(3, edit.ReportCount);
        Assert.Equal(9.5m, edit.TotalHours);
        await vm.SavePreparationAsync();
        var saved = (await _store.GetByIdAsync(1))!.Hours;
        Assert.Equal(2, saved.Count);
        Assert.Equal([100, 200], saved.Select(h => h.MasterPlanSubContractId).OrderBy(id => id).ToArray());
        Assert.Equal(new DateTime(2026, 8, 1), saved[0].FromDate);
        Assert.Equal(new DateTime(2026, 8, 1), saved[1].FromDate);
        Assert.Equal([11, 12], saved.Single(h => h.MasterPlanSubContractId == 100).Reports.Select(r => r.HoursReportId).ToArray());
        Assert.Equal([14], saved.Single(h => h.MasterPlanSubContractId == 200).Reports.Select(r => r.HoursReportId).ToArray());
        Assert.Equal(saved.SelectMany(h => h.Reports.Select(r => r.HoursReportId)).Distinct().Count(),
            saved.SelectMany(h => h.Reports.Select(r => r.HoursReportId)).Count());
    }

    [Fact]
    public async Task Select_all_selects_every_hourly_subcontract_not_only_filtered()
    {
        _components.Load = MultiHourSnapshot();
        await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        vm.AddHourlyScopeCommand.Execute(null);
        var edit = Assert.Single(vm.HourlyScopeEdits);
        edit.SearchText = "תנועה";
        Assert.Single(edit.VisibleChoices);
        edit.SelectAll();
        Assert.Equal(3, edit.SelectedSubContractCount);
        Assert.Equal(3, edit.Choices.Count(c => c.IsSelected));
    }

    [Fact]
    public async Task Clear_all_saves_no_hourly_scope()
    {
        _components.Load = MultiHourSnapshot();
        await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        vm.AddHourlyScopeCommand.Execute(null);
        var edit = Assert.Single(vm.HourlyScopeEdits);
        edit.FromDate = new DateTime(2026, 8, 1);
        edit.ToDate = new DateTime(2026, 8, 31);
        edit.SelectAll();
        edit.ClearAll();
        await vm.SavePreparationAsync();
        Assert.Empty((await _store.GetByIdAsync(1))!.Hours);
    }

    [Fact]
    public async Task Zero_report_selected_subcontract_blocks_save_and_is_named()
    {
        _components.Load = MultiHourSnapshot();
        await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        vm.AddHourlyScopeCommand.Execute(null);
        var edit = Assert.Single(vm.HourlyScopeEdits);
        edit.FromDate = new DateTime(2026, 8, 1);
        edit.ToDate = new DateTime(2026, 8, 31);
        edit.ClearAll();
        edit.Choices.Single(c => c.MasterPlanSubContractId == 100).IsSelected = true;
        edit.Choices.Single(c => c.MasterPlanSubContractId == 300).IsSelected = true;
        await vm.SavePreparationAsync();
        Assert.Contains("לא נמצאו דיווחי שעות", vm.OperationErrorMessage, StringComparison.Ordinal);
        Assert.Contains("ריק", vm.OperationErrorMessage, StringComparison.Ordinal);
        Assert.Empty((await _store.GetByIdAsync(1))!.Hours);
    }

    [Fact]
    public async Task Overlap_covers_union_of_selected_subcontracts()
    {
        _components.Load = MultiHourSnapshot();
        await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        await _service.SaveSelectionAsync(1, [], [HoursLine(100, 11, 2.5m)]);
        await _service.EnsureFromPrepareBillAsync(5906, "2609", "אחר", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        vm.SelectedPreparation = vm.PreparationRows.Single(r => r.MasterPlanProjectId == 5906);
        await vm.LoadPreparationComponentsAsync();
        vm.AddHourlyScopeCommand.Execute(null);
        var edit = Assert.Single(vm.HourlyScopeEdits);
        edit.FromDate = new DateTime(2026, 8, 1);
        edit.ToDate = new DateTime(2026, 8, 31);
        edit.ClearAll();
        edit.Choices.Single(c => c.MasterPlanSubContractId == 100).IsSelected = true;
        edit.Choices.Single(c => c.MasterPlanSubContractId == 200).IsSelected = true;
        await vm.LoadPreparationComponentsAsync();
        Assert.True(edit.HasOverlapWarning);
        Assert.Contains("תנועה", edit.OverlapWarningText, StringComparison.Ordinal);
        Assert.False(BillingPreparationHoursScopeComposer.ContainsForbiddenUnbilledClaim(edit.OverlapWarningText));
    }

    [Fact]
    public async Task Saved_multi_select_reloads_as_one_grouped_scope()
    {
        _components.Load = MultiHourSnapshot();
        await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        var first = CreateVm();
        await first.RefreshPreparationAsync();
        first.AddHourlyScopeCommand.Execute(null);
        var edit = Assert.Single(first.HourlyScopeEdits);
        edit.FromDate = new DateTime(2026, 8, 1);
        edit.ToDate = new DateTime(2026, 8, 31);
        edit.ClearAll();
        edit.Choices.Single(c => c.MasterPlanSubContractId == 100).IsSelected = true;
        edit.Choices.Single(c => c.MasterPlanSubContractId == 200).IsSelected = true;
        await first.SavePreparationAsync();

        var reloaded = CreateVm();
        await reloaded.RefreshPreparationAsync();
        var loaded = Assert.Single(reloaded.HourlyScopeEdits);
        Assert.Equal(2, loaded.SelectedSubContractCount);
        Assert.Contains(100, loaded.SelectedIds);
        Assert.Contains(200, loaded.SelectedIds);
        Assert.Equal(new DateTime(2026, 8, 1), loaded.FromDate);
        Assert.Equal(2, (await _store.GetByIdAsync(1))!.Hours.Count);
    }

    [Fact]
    public void Single_subcontract_compose_remains_compatible()
    {
        var composed = BillingPreparationHoursScopeComposer.Compose(
            100,
            new DateTime(2026, 8, 1),
            new DateTime(2026, 8, 31),
            [new BillingHourlySubContractDraft(100, "תנועה", MasterPlanSnapshotFeeTypeIds.WorkingHours)],
            [
                new BillingHourReportFact(11, 5905, 100, 1, new DateTime(2026, 8, 1), 1, "A", 2.5m, "x")
            ],
            [],
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(100, composed.MasterPlanSubContractId);
        Assert.Single(composed.Reports);
    }

    [Fact]
    public void Task_instructions_group_hourly_scopes()
    {
        var body = BillingPreparationTaskInstructions.Build(
            EmptyRequest() with
            {
                Hours =
                [
                    HoursLine(100, 11, 2.5m, "תנועה"),
                    HoursLine(200, 14, 4m, "כבישים")
                ]
            });
        Assert.Contains("תקופה: 01/08/2026–31/08/2026", body, StringComparison.Ordinal);
        Assert.Contains("• תנועה — 1 דיווחים, 2.5 שעות", body, StringComparison.Ordinal);
        Assert.Contains("• כבישים — 1 דיווחים, 4 שעות", body, StringComparison.Ordinal);
        Assert.Contains("2 הסכמי משנה", body, StringComparison.Ordinal);
        Assert.DoesNotContain("לא מחויבות", body, StringComparison.Ordinal);
    }

    private BillingDashboardViewModel CreateVm() =>
        new(new UnusedDashboardRead(), preparation: _service);

    private static BillingPreparationSnapshotLoad MultiHourSnapshot() =>
        new(
            true,
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            "לקוח",
            "2608",
            "פרויקט",
            [],
            [
                new BillingHourlySubContractDraft(100, "תנועה", MasterPlanSnapshotFeeTypeIds.WorkingHours),
                new BillingHourlySubContractDraft(200, "כבישים", MasterPlanSnapshotFeeTypeIds.WorkingHours),
                new BillingHourlySubContractDraft(300, "ריק", MasterPlanSnapshotFeeTypeIds.WorkingHours)
            ],
            [
                new BillingHourReportFact(11, 5905, 100, 1, new DateTime(2026, 8, 1), 1, "A", 2.5m, "x"),
                new BillingHourReportFact(12, 5905, 100, 1, new DateTime(2026, 8, 15), 1, "A", 3m, "y"),
                new BillingHourReportFact(14, 5905, 200, 1, new DateTime(2026, 8, 10), 1, "B", 4m, "other")
            ]);

    private static BillingPreparationHoursLineSnapshot HoursLine(
        int subId,
        int reportId,
        decimal hours,
        string name = "תנועה") =>
        new(
            subId, name,
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 31),
            1, hours,
            [new BillingPreparationHourReportSnapshot(reportId, new DateTime(2026, 8, 1), 1, "A", hours, subId, 1, "x")],
            [],
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            BillingConfirmationMode.None, null, null, null);

    private static BillingPreparationRequestRecord EmptyRequest() =>
        new(
            1, 5905, 12, "2608", "פרויקט", "לקוח",
            BillingPreparationStatus.ReadyForApproval,
            new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc),
            7, "manager",
            null, null, null, null, null, null,
            false, null, null, null,
            [], []);

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
