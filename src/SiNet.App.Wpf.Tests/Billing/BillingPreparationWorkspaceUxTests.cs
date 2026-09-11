using System.IO;
using SiNet.App.Wpf.Billing;
using SiNet.Application.Billing;
using Xunit;

namespace SiNet.App.Wpf.Tests.Billing;

public sealed class BillingPreparationWorkspaceUxTests
{
    private readonly MemoryBillingPreparationStore _store = new();
    private readonly MemoryBillingPreparationComponentSource _components = new();
    private readonly BillingPreparationService _service;
    private readonly RecordingUnsavedPrompts _prompts = new();

    public BillingPreparationWorkspaceUxTests()
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
    public void Preparation_editor_is_not_fixed_to_460px()
    {
        var xaml = File.ReadAllText(ViewPath());
        Assert.DoesNotContain("<ColumnDefinition Width=\"460\" MinWidth=\"320\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"320\" MinWidth=\"260\" MaxWidth=\"400\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"*\" MinWidth=\"420\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("GridSplitter", xaml, StringComparison.Ordinal);
        Assert.Contains("VisibleEditableStages", xaml, StringComparison.Ordinal);
        Assert.Contains("CompactHeaderTitle", xaml, StringComparison.Ordinal);
        Assert.Contains("BillingDashboard.StageSearch", xaml, StringComparison.Ordinal);
        Assert.Contains("BillingDashboard.DirtyBanner", xaml, StringComparison.Ordinal);
        Assert.Contains("DirtyBannerText", xaml, StringComparison.Ordinal);
        Assert.Contains("BillingDashboard.PreparationAmountSummary", xaml, StringComparison.Ordinal);
        Assert.Contains("PreparationAmountMainText", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Search_matches_contract_subcontract_and_stage_without_changing_additions()
    {
        Assert.True(BillingPreparationStageSearch.MatchesSubContract("תכנון פיזי", "1", "תכנון"));
        Assert.True(BillingPreparationStageSearch.MatchesSubContract("כבישים", "3853", "3853"));
        Assert.True(BillingPreparationStageSearch.MatchesStageName("פיקוח עליון על הביצוע", "פיקוח עליון"));
        Assert.True(BillingPreparationStageSearch.MatchesContract("תל יבנה", "2", "תל יבנה"));
        Assert.True(BillingPreparationStageSearch.MatchesContract("חוזה", "2", "2"));
        Assert.False(BillingPreparationStageSearch.MatchesStageName("פיקוח עליון", "ניקוז"));
    }

    [Fact]
    public async Task Stage_search_filters_visible_groups_and_does_not_change_persistence()
    {
        _components.Load = TwoContractCatalog();
        await _service.EnsureFromPrepareBillAsync(5905, "1844", "פרויקט", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        var stage = vm.StageEdits.Single(s => s.StageName.Contains("פיקוח עליון", StringComparison.Ordinal));
        stage.AdditionPercent = 20m;
        Assert.Equal(3, vm.StageGroups.Count);

        vm.StageSearchText = "פיקוח עליון";
        Assert.True(vm.StageGroups.Single(g => g.SubContractId == 10).IsSearchVisible);
        Assert.False(vm.StageGroups.Single(g => g.SubContractId == 20).IsSearchVisible);
        Assert.False(vm.StageGroups.Single(g => g.SubContractId == 30).IsSearchVisible);
        Assert.Single(vm.StageGroups.Single(g => g.SubContractId == 10).VisibleEditableStages);
        Assert.Equal("פיקוח עליון על הביצוע", vm.StageGroups.Single(g => g.SubContractId == 10).VisibleEditableStages[0].StageName);
        Assert.Equal(20m, stage.AdditionPercent);
        Assert.True(vm.StageContractGroups.Single(c => c.ContractId == 1).IsSearchVisible);
        Assert.False(vm.StageContractGroups.Single(c => c.ContractId == 2).IsSearchVisible);

        vm.StageSearchText = "3853";
        Assert.True(vm.StageGroups.Single(g => g.SubContractId == 10).IsSearchVisible);
        Assert.Equal(2, vm.StageGroups.Single(g => g.SubContractId == 10).VisibleEditableStages.Count);
        Assert.Equal(20m, stage.AdditionPercent);

        vm.StageSearchText = "תכנון פיזי";
        Assert.True(vm.StageGroups.Single(g => g.SubContractId == 10).IsSearchVisible);
        Assert.False(vm.StageGroups.Single(g => g.SubContractId == 20).IsSearchVisible);
        Assert.False(vm.StageGroups.Single(g => g.SubContractId == 30).IsSearchVisible);

        vm.StageSearchText = "תל יבנה";
        Assert.True(vm.StageGroups.Single(g => g.SubContractId == 30).IsSearchVisible);
        Assert.False(vm.StageGroups.Single(g => g.SubContractId == 10).IsSearchVisible);

        vm.StageSearchText = string.Empty;
        Assert.All(vm.StageGroups, g => Assert.True(g.IsSearchVisible));
        Assert.Equal(2, vm.StageGroups.Single(g => g.SubContractId == 10).VisibleEditableStages.Count);
        Assert.Equal(20m, stage.AdditionPercent);
        Assert.Empty((await _store.GetByIdAsync(1))!.Stages);
    }

    [Fact]
    public async Task Valid_100_percent_group_keeps_short_summary_and_invalid_weights_are_explicit()
    {
        _components.Load = WeightMixCatalog();
        await _service.EnsureFromPrepareBillAsync(5905, "1844", "פרויקט", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();

        var valid = vm.StageGroups.Single(g => g.SubContractId == 10);
        Assert.True(valid.WeightsSumApproximatelyToOne);
        Assert.False(valid.ShowWeightSumWarning);
        Assert.DoesNotContain("לפי שלבים", valid.ObservedSummaryText, StringComparison.Ordinal);
        Assert.DoesNotContain("לפי שלבים", valid.RemainingSummaryText, StringComparison.Ordinal);

        var ninetyFive = vm.StageGroups.Single(g => g.SubContractId == 20);
        ninetyFive.IsExpanded = false;
        Assert.Equal("⚠ משקל שלבים: 95%", ninetyFive.HeaderWeightWarning);
        Assert.Contains("לפי שלבים", ninetyFive.RemainingSummaryText, StringComparison.Ordinal);
        ninetyFive.IsExpanded = true;
        Assert.Contains("ולא ב-100%", ninetyFive.HeaderWeightWarning, StringComparison.Ordinal);

        var over = vm.StageGroups.Single(g => g.SubContractId == 30);
        over.IsExpanded = false;
        Assert.Equal("⚠ משקל שלבים: 114%", over.HeaderWeightWarning);
        Assert.Contains("114%", over.DefinedWeightSummaryText, StringComparison.Ordinal);
        Assert.DoesNotContain("משקל שלבים מוגדר: 100%", over.DefinedWeightSummaryText, StringComparison.Ordinal);
        Assert.Contains("לפי שלבים", over.ObservedSummaryText, StringComparison.Ordinal);

        var dq = vm.StageGroups.Single(g => g.SubContractId == 20);
        Assert.True(dq.ShowPartialSummaryWarning);
        Assert.NotEqual(dq.HeaderPartialWarning, ninetyFive.CompactWeightSumWarning);
        Assert.Contains("חלקי", dq.PartialSummaryWarning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changing_stage_addition_marks_dirty_and_load_does_not()
    {
        _components.Load = TwoContractCatalog();
        await _service.EnsureFromPrepareBillAsync(5905, "1844", "פרויקט", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        Assert.False(vm.IsDirty);
        vm.StageEdits[0].AdditionPercent = 20m;
        Assert.True(vm.IsDirty);
        Assert.Empty((await _store.GetByIdAsync(1))!.Stages);

        await vm.SavePreparationAsync();
        Assert.False(vm.IsDirty);
        Assert.Single((await _store.GetByIdAsync(1))!.Stages);

        var reloaded = CreateVm();
        await reloaded.RefreshPreparationAsync();
        Assert.False(reloaded.IsDirty);
        Assert.Equal(20m, reloaded.StageEdits.Single(s => s.MasterPlanStageId == vm.StageEdits[0].MasterPlanStageId).AdditionPercent);
    }

    [Fact]
    public async Task Changing_hourly_selection_marks_dirty()
    {
        _components.Load = HourlyCatalog();
        await _service.EnsureFromPrepareBillAsync(5905, "1844", "פרויקט", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        Assert.False(vm.IsDirty);
        vm.AddHourlyScopeCommand.Execute(null);
        Assert.True(vm.IsDirty);
        var edit = Assert.Single(vm.HourlyScopeEdits);
        edit.FromDate = new DateTime(2026, 8, 1);
        edit.ToDate = new DateTime(2026, 8, 31);
        await vm.SavePreparationAsync();
        Assert.False(vm.IsDirty);
        var loaded = Assert.Single(vm.HourlyScopeEdits);
        loaded.ClearAll();
        Assert.True(vm.IsDirty);
        Assert.Single((await _store.GetByIdAsync(1))!.Hours);
    }

    [Fact]
    public async Task Dirty_request_switch_stay_keeps_edits_and_discard_reloads_persisted()
    {
        _components.Load = TwoContractCatalog();
        await _service.EnsureFromPrepareBillAsync(5905, "1844", "פרויקט", "לקוח");
        await _service.EnsureFromPrepareBillAsync(5906, "2754", "אחר", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        var first = vm.SelectedPreparation!;
        var second = vm.PreparationRows.Single(r => r.Id != first.Id);
        var stage = vm.StageEdits.Single(s => s.StageName.Contains("פיקוח עליון", StringComparison.Ordinal));
        stage.AdditionPercent = 20m;
        Assert.True(vm.IsDirty);

        _prompts.Next = BillingUnsavedEditsDecision.Stay;
        vm.SelectedPreparation = second;
        Assert.Equal(1, _prompts.ConfirmCount);
        Assert.Equal(first.Id, vm.SelectedPreparation!.Id);
        Assert.Equal(20m, stage.AdditionPercent);
        Assert.Empty((await _store.GetByIdAsync(first.Id))!.Stages);

        _prompts.Next = BillingUnsavedEditsDecision.Discard;
        vm.SelectedPreparation = second;
        Assert.Equal(2, _prompts.ConfirmCount);
        Assert.Equal(second.Id, vm.SelectedPreparation!.Id);
        Assert.False(vm.IsDirty);
        Assert.All(vm.StageEdits, s => Assert.Equal(0m, s.AdditionPercent));
        Assert.Empty((await _store.GetByIdAsync(first.Id))!.Stages);
        Assert.Empty((await _store.GetByIdAsync(second.Id))!.Stages);
    }

    [Fact]
    public async Task Refresh_while_dirty_requires_confirmation_and_does_not_write()
    {
        _components.Load = TwoContractCatalog();
        await _service.EnsureFromPrepareBillAsync(5905, "1844", "פרויקט", "לקוח");
        var vm = CreateVm();
        await vm.RefreshPreparationAsync();
        var stage = vm.StageEdits.Single(s => s.StageName.Contains("פיקוח עליון", StringComparison.Ordinal));
        stage.AdditionPercent = 20m;

        _prompts.Next = BillingUnsavedEditsDecision.Stay;
        await vm.RefreshPreparationAsync();
        Assert.Equal(1, _prompts.ConfirmCount);
        Assert.Equal(20m, stage.AdditionPercent);
        Assert.True(vm.IsDirty);
        Assert.Empty((await _store.GetByIdAsync(1))!.Stages);

        _prompts.Next = BillingUnsavedEditsDecision.Discard;
        await vm.RefreshPreparationAsync();
        Assert.Equal(2, _prompts.ConfirmCount);
        Assert.False(vm.IsDirty);
        Assert.Equal(0m, vm.StageEdits.Single(s => s.StageName.Contains("פיקוח עליון", StringComparison.Ordinal)).AdditionPercent);
        Assert.Empty((await _store.GetByIdAsync(1))!.Stages);
    }

    private BillingDashboardViewModel CreateVm() =>
        new(new UnusedDashboardRead(), preparation: _service, prompts: _prompts);

    private static string ViewPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "SiNet.App.Wpf", "Billing", "BillingDashboardView.xaml");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("BillingDashboardView.xaml not found.");
    }

    private static BillingPreparationSnapshotLoad TwoContractCatalog() =>
        new(
            true,
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            "לקוח",
            "1844",
            "פרויקט",
            [
                Stage(1, 10, "פיקוח עליון על הביצוע", "תכנון פיזי", 0.50m, 0.10m, 1, "חוזה ראשי", "1", "3853"),
                Stage(2, 10, "תכנון מפורט", "תכנון פיזי", 0.50m, 0.20m, 1, "חוזה ראשי", "1", "3853"),
                Stage(3, 20, "ניקוז", "ניקוז", 1m, 0.10m, 1, "חוזה ראשי", "1", "2"),
                Stage(4, 30, "פיקוח", "תל יבנה עבודות", 1m, 0.10m, 2, "תל יבנה", "2", "6")
            ],
            [],
            []);

    private static BillingPreparationSnapshotLoad WeightMixCatalog() =>
        new(
            true,
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            "לקוח",
            "1844",
            "פרויקט",
            [
                Stage(1, 10, "א", "מאה", 0.50m, 0.10m),
                Stage(2, 10, "ב", "מאה", 0.50m, 0.10m),
                Stage(3, 20, "ג", "תשעים", 0.50m, 0.10m),
                Stage(4, 20, "ד", "תשעים", 0.45m, 0.10m),
                Stage(5, 20, "חריג", "תשעים", 0.50m, 1.06m, observeRaw: true),
                Stage(6, 30, "ה", "חריגה", 0.50m, 0.82m),
                Stage(7, 30, "ו", "חריגה", 0.64m, 0.82m)
            ],
            [],
            []);

    private static BillingPreparationSnapshotLoad HourlyCatalog() =>
        new(
            true,
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            "לקוח",
            "1844",
            "פרויקט",
            [],
            [new BillingHourlySubContractDraft(100, "תנועה", MasterPlanSnapshotFeeTypeIds.WorkingHours)],
            [new BillingHourReportFact(11, 5905, 100, 1, new DateTime(2026, 8, 1), 1, "A", 2.5m, "x")]);

    private static BillingPreparationStageDraft Stage(
        int stageId,
        int subId,
        string name,
        string subName,
        decimal weight,
        decimal observedOrOutlier,
        int contractId = 1,
        string? contractName = "חוזה",
        string? contractNumber = "1",
        string? subNumber = null,
        bool observeRaw = false)
    {
        var observed = BillingStageProgressCalculator.Observe([observedOrOutlier]);
        return new BillingPreparationStageDraft(
            stageId, subId, name, subName, weight, observed, observed.Value, false,
            MasterPlanSnapshotFeeTypeIds.FixedPrice, contractId, contractName, contractNumber, subNumber);
    }

    private sealed class RecordingUnsavedPrompts : IBillingReviewPrompts
    {
        public BillingUnsavedEditsDecision Next { get; set; } = BillingUnsavedEditsDecision.Stay;
        public int ConfirmCount { get; private set; }

        public bool ConfirmPrepareBill(string projectLabel) => true;

        public BillingNotNowPromptResult? PromptNotNow(string projectLabel) => null;

        public bool ConfirmClearDecision(string projectLabel) => true;

        public BillingUnsavedEditsDecision ConfirmDiscardUnsavedPreparationEdits()
        {
            ConfirmCount++;
            return Next;
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
