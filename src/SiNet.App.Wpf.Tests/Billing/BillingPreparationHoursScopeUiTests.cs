using System.IO;
using SiNet.App.Wpf.Billing;
using SiNet.Application.Billing;
using Xunit;

namespace SiNet.App.Wpf.Tests.Billing;

public sealed class BillingPreparationHoursScopeComposerTests
{
    [Fact]
    public void Caption_is_manager_selected_and_never_unbilled_claim()
    {
        var caption = BillingPreparationHoursScopeComposer.ManagerSelectedScopeCaption;
        Assert.Contains("נבחר במפורש", caption, StringComparison.Ordinal);
        Assert.False(BillingPreparationHoursScopeComposer.ContainsForbiddenUnbilledClaim(caption));
    }

    [Fact]
    public void Invalid_date_range_is_rejected()
    {
        var preview = BillingPreparationHoursScopeComposer.Preview(
            100,
            new DateTime(2026, 8, 31),
            new DateTime(2026, 8, 1),
            HourlySubs(),
            Reports());
        Assert.NotNull(preview.ValidationMessage);
        Assert.Equal(0, preview.ReportCount);
    }

    [Fact]
    public void Zero_matching_reports_cannot_be_composed()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            BillingPreparationHoursScopeComposer.Compose(
                100,
                new DateTime(2026, 1, 1),
                new DateTime(2026, 1, 31),
                HourlySubs(),
                Reports(),
                [],
                new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.Contains("ללא דיווחים תואמים", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_hourly_subcontract_cannot_be_composed()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            BillingPreparationHoursScopeComposer.RequireHourlySubContract(
                200,
                [new BillingHourlySubContractDraft(200, "קבוע", MasterPlanSnapshotFeeTypeIds.FixedPrice)]));
        Assert.Contains("FeeType=4", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_hours_report_ids_are_collapsed_in_one_line()
    {
        var reports = new BillingHourReportFact[]
        {
            new(11, 5905, 100, 1, new DateTime(2026, 8, 1), 1, "A", 2.5m, "x"),
            new(11, 5905, 100, 1, new DateTime(2026, 8, 1), 1, "A", 2.5m, "x")
        };
        var composed = BillingPreparationHoursScopeComposer.Compose(
            100,
            new DateTime(2026, 8, 1),
            new DateTime(2026, 8, 31),
            HourlySubs(),
            reports,
            [],
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Single(composed.Reports);
        Assert.Equal(11, composed.Reports[0].HoursReportId);
    }

    private static IReadOnlyList<BillingHourlySubContractDraft> HourlySubs() =>
        [new BillingHourlySubContractDraft(100, "תנועה", MasterPlanSnapshotFeeTypeIds.WorkingHours)];

    private static IReadOnlyList<BillingHourReportFact> Reports() =>
    [
        new(11, 5905, 100, 1, new DateTime(2026, 8, 1), 1, "A", 2.5m, "x"),
        new(12, 5905, 100, 1, new DateTime(2026, 8, 15), 1, "A", 3m, "y")
    ];
}

public sealed class BillingPreparationHoursScopeUiTests
{
    private readonly MemoryBillingPreparationStore _store = new();
    private readonly MemoryBillingPreparationComponentSource _components = new();
    private readonly BillingPreparationService _service;

    public BillingPreparationHoursScopeUiTests()
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
    public async Task Hourly_scope_ui_resolves_and_saves_through_SaveSelectionAsync()
    {
        _components.Load = HoursOnlySnapshot();
        await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        vm.AddHourlyScopeCommand.Execute(null);
        var edit = Assert.Single(vm.HourlyScopeEdits);
        edit.MasterPlanSubContractId = 100;
        edit.FromDate = new DateTime(2026, 8, 1);
        edit.ToDate = new DateTime(2026, 8, 31);

        Assert.Equal(2, edit.ReportCount);
        Assert.Equal(5.5m, edit.TotalHours);
        Assert.Equal("תנועה", edit.SubContractName);
        Assert.False(BillingPreparationHoursScopeComposer.ContainsForbiddenUnbilledClaim(vm.HourlyScopeCaption));

        await vm.SavePreparationAsync();
        Assert.Equal(string.Empty, vm.ErrorMessage);
        Assert.Equal(string.Empty, vm.OperationErrorMessage);
        var saved = await _store.GetByIdAsync(1);
        var hours = Assert.Single(saved!.Hours);
        Assert.Equal(100, hours.MasterPlanSubContractId);
        Assert.Equal(2, hours.ReportCount);
        Assert.Equal(5.5m, hours.TotalHours);
        Assert.Equal([11, 12], hours.Reports.Select(r => r.HoursReportId).ToArray());
    }

    [Fact]
    public async Task Stage_only_save_does_not_reuse_source_hours()
    {
        _components.Load = MixedSnapshot();
        await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        var stage = Assert.Single(vm.StageEdits);
        stage.AdditionPercent = 30m;
        Assert.Equal(70m, stage.AfterBillPercent);
        Assert.Empty(vm.HourlyScopeEdits);

        await vm.SavePreparationAsync();
        var saved = await _store.GetByIdAsync(1);
        Assert.Single(saved!.Stages);
        Assert.Equal(0.30m, saved.Stages[0].RequestedDelta);
        Assert.Empty(saved.Hours);
    }

    [Fact]
    public async Task Hours_only_save_persists_hourly_snapshot_without_stages()
    {
        _components.Load = HoursOnlySnapshot();
        await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        vm.AddHourlyScopeCommand.Execute(null);
        var edit = Assert.Single(vm.HourlyScopeEdits);
        edit.MasterPlanSubContractId = 100;
        edit.FromDate = new DateTime(2026, 8, 1);
        edit.ToDate = new DateTime(2026, 8, 31);
        await vm.SavePreparationAsync();

        var saved = await _store.GetByIdAsync(1);
        Assert.Empty(saved!.Stages);
        Assert.Single(saved.Hours);
    }

    [Fact]
    public async Task Mixed_stages_and_hours_are_saved_together()
    {
        _components.Load = MixedSnapshot();
        await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        vm.StageEdits[0].AdditionPercent = 30m;
        vm.AddHourlyScopeCommand.Execute(null);
        var edit = Assert.Single(vm.HourlyScopeEdits);
        edit.MasterPlanSubContractId = 100;
        edit.FromDate = new DateTime(2026, 8, 1);
        edit.ToDate = new DateTime(2026, 8, 31);
        await vm.SavePreparationAsync();

        var saved = await _store.GetByIdAsync(1);
        Assert.Single(saved!.Stages);
        Assert.Single(saved.Hours);
        Assert.Equal(BillingPreparationStatus.ReadyForApproval, saved.Status);
    }

    [Fact]
    public async Task Invalid_date_range_is_not_saved()
    {
        _components.Load = HoursOnlySnapshot();
        await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        vm.AddHourlyScopeCommand.Execute(null);
        var edit = Assert.Single(vm.HourlyScopeEdits);
        edit.MasterPlanSubContractId = 100;
        edit.FromDate = new DateTime(2026, 8, 31);
        edit.ToDate = new DateTime(2026, 8, 1);
        await vm.SavePreparationAsync();

        Assert.False(string.IsNullOrEmpty(vm.OperationErrorMessage));
        var saved = await _store.GetByIdAsync(1);
        Assert.Empty(saved!.Hours);
    }

    [Fact]
    public async Task Zero_matching_reports_cannot_be_silently_saved()
    {
        _components.Load = HoursOnlySnapshot();
        await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        vm.AddHourlyScopeCommand.Execute(null);
        var edit = Assert.Single(vm.HourlyScopeEdits);
        edit.MasterPlanSubContractId = 100;
        edit.FromDate = new DateTime(2026, 1, 1);
        edit.ToDate = new DateTime(2026, 1, 31);
        await vm.SavePreparationAsync();

        Assert.Contains("לא נמצאו דיווחי שעות", vm.OperationErrorMessage, StringComparison.Ordinal);
        var saved = await _store.GetByIdAsync(1);
        Assert.Empty(saved!.Hours);
    }

    [Fact]
    public async Task Overlap_warning_is_shown_before_save()
    {
        _components.Load = HoursOnlySnapshot();
        await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        await _service.SaveSelectionAsync(1, [], [HoursLine()]);
        await _service.EnsureFromPrepareBillAsync(5906, "2609", "אחר", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        vm.SelectedPreparation = vm.PreparationRows.Single(r => r.MasterPlanProjectId == 5906);
        await vm.LoadPreparationComponentsAsync();
        vm.AddHourlyScopeCommand.Execute(null);
        var edit = Assert.Single(vm.HourlyScopeEdits);
        edit.MasterPlanSubContractId = 100;
        edit.FromDate = new DateTime(2026, 8, 1);
        edit.ToDate = new DateTime(2026, 8, 31);
        await vm.LoadPreparationComponentsAsync();

        Assert.True(edit.HasOverlapWarning);
        Assert.Contains("בקשת הכנה אחרת", edit.OverlapWarningText, StringComparison.Ordinal);
        Assert.False(BillingPreparationHoursScopeComposer.ContainsForbiddenUnbilledClaim(edit.OverlapWarningText));
    }

    [Fact]
    public async Task Saved_hourly_scope_reloads_into_the_ui()
    {
        _components.Load = HoursOnlySnapshot();
        await _service.EnsureFromPrepareBillAsync(5905, "2608", "פרויקט", "לקוח");
        var first = CreateVm();
        await first.RefreshPreparationAsync();
        first.AddHourlyScopeCommand.Execute(null);
        var edit = Assert.Single(first.HourlyScopeEdits);
        edit.MasterPlanSubContractId = 100;
        edit.FromDate = new DateTime(2026, 8, 1);
        edit.ToDate = new DateTime(2026, 8, 31);
        await first.SavePreparationAsync();

        var reloaded = CreateVm();
        await reloaded.RefreshPreparationAsync();
        var loaded = Assert.Single(reloaded.HourlyScopeEdits);
        Assert.True(loaded.Included);
        Assert.Equal(100, loaded.MasterPlanSubContractId);
        Assert.Equal(new DateTime(2026, 8, 1), loaded.FromDate);
        Assert.Equal(new DateTime(2026, 8, 31), loaded.ToDate);
        Assert.Equal(2, loaded.ReportCount);
        Assert.Equal(5.5m, loaded.TotalHours);
    }

    [Fact]
    public void Dashboard_caption_never_claims_unbilled_truth()
    {
        var vm = CreateVm();
        Assert.False(BillingPreparationHoursScopeComposer.ContainsForbiddenUnbilledClaim(vm.HourlyScopeCaption));
        Assert.Contains("נבחר במפורש", vm.HourlyScopeCaption, StringComparison.Ordinal);
        var xaml = File.ReadAllText(
            Path.Combine(FindRepoRoot(), "src", "SiNet.App.Wpf", "Billing", "BillingDashboardView.xaml"));
        Assert.Contains("רכיבי שעות", xaml, StringComparison.Ordinal);
        Assert.False(BillingPreparationHoursScopeComposer.ContainsForbiddenUnbilledClaim(xaml));
    }

    [Fact]
    public void Hourly_date_text_parses_israeli_and_iso_formats()
    {
        var edit = new BillingPreparationHoursScopeEditVm();
        edit.FromDateText = "02/06/2026";
        Assert.Equal(new DateTime(2026, 6, 2), edit.FromDate);
        edit.ToDateText = "2/6/2026";
        Assert.Equal(new DateTime(2026, 6, 2), edit.ToDate);
        edit.FromDateText = "2026-06-03";
        Assert.Equal(new DateTime(2026, 6, 3), edit.FromDate);
        Assert.Equal("03/06/2026", edit.FromDateText);
    }

    [Fact]
    public void Incomplete_hourly_date_text_does_not_wipe_existing_date()
    {
        var edit = new BillingPreparationHoursScopeEditVm
        {
            FromDate = new DateTime(2026, 6, 2)
        };
        edit.FromDateText = "02/0";
        Assert.Equal(new DateTime(2026, 6, 2), edit.FromDate);
        Assert.Equal("02/06/2026", edit.FromDateText);
    }

    [Fact]
    public void New_hourly_scope_opens_selector_inline_by_default()
    {
        var edit = new BillingPreparationHoursScopeEditVm();
        Assert.True(edit.IsSelectorOpen);
        var xaml = File.ReadAllText(
            Path.Combine(FindRepoRoot(), "src", "SiNet.App.Wpf", "Billing", "BillingDashboardView.xaml"));
        Assert.DoesNotContain("<Popup", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<DatePicker", xaml, StringComparison.Ordinal);
        Assert.Contains("FromDateText", xaml, StringComparison.Ordinal);
        Assert.Contains("ToDateText", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"BillingDashboard.HourlyFromDate\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"BillingDashboard.SavePreparation\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"{DynamicResource SiPopupListMaxHeight}\"", xaml, StringComparison.Ordinal);
    }

    private BillingDashboardViewModel CreateVm() =>
        new(new UnusedDashboardRead(), preparation: _service);

    private static BillingPreparationSnapshotLoad HoursOnlySnapshot() =>
        new(
            true,
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            "לקוח",
            "2608",
            "פרויקט",
            [],
            [new BillingHourlySubContractDraft(100, "תנועה", MasterPlanSnapshotFeeTypeIds.WorkingHours)],
            [
                new BillingHourReportFact(11, 5905, 100, 1, new DateTime(2026, 8, 1), 1, "A", 2.5m, "x"),
                new BillingHourReportFact(12, 5905, 100, 1, new DateTime(2026, 8, 15), 1, "A", 3m, "y")
            ]);

    private static BillingPreparationSnapshotLoad MixedSnapshot() =>
        HoursOnlySnapshot() with
        {
            Stages =
            [
                new BillingPreparationStageDraft(
                    12791, 9794, "תכנון מפורט", "כבישים", 0.20m,
                    BillingStageProgressCalculator.Observe([0.40m]),
                    0.40m,
                    Included: false)
            ]
        };

    private static BillingPreparationHoursLineSnapshot HoursLine() =>
        new(
            100, "תנועה",
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 31),
            1, 2.5m,
            [new BillingPreparationHourReportSnapshot(11, new DateTime(2026, 8, 1), 1, "A", 2.5m, 100, 1, "x")],
            [],
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            BillingConfirmationMode.None, null, null, null);

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

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SiNet.sln"))
                || File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repo root not found.");
    }
}
