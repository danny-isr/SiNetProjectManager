using SiNet.App.Wpf.Billing;
using SiNet.Application.Billing;
using Xunit;

namespace SiNet.App.Wpf.Tests.Billing;

public sealed class BillingDashboardViewModelTests
{
    private static readonly DateTime SnapshotDate = new(2026, 8, 2);
    private static readonly DateTime LastSync = new(2026, 9, 6, 8, 15, 0);

    [Fact]
    public async Task Healthy_result_populates_candidates_and_kpis()
    {
        var fake = new FakeBillingDashboardReadService(HealthyResult());
        var vm = new BillingDashboardViewModel(fake);

        await vm.LoadAsync().ConfigureAwait(true);

        Assert.Equal(BillingDashboardUiState.Loaded, vm.UiState);
        Assert.False(vm.ShowBlockedPanel);
        Assert.False(vm.ShowWarningBanner);
        Assert.True(vm.ShowCandidatesArea);
        Assert.True(vm.ShowOperationalChrome);
        Assert.Equal("3", vm.ReviewNowCountText);
        Assert.Equal("1", vm.AccumulatedWorkCountText);
        Assert.Equal("1", vm.BillInPreparationCountText);
        Assert.Equal("1", vm.CoveredCountText);
        Assert.Equal(BillingDashboardFormatters.Money(12345.50m), vm.ReceivedThisMonthText);
        Assert.Equal(BillingDashboardFormatters.ReceivedThisMonthLabel, vm.ReceivedThisMonthLabel);
        Assert.Contains("5905", vm.Rows.Select(r => r.ProjectNumber));
        Assert.Equal(1, fake.CallCount);
        Assert.True(fake.LastRequest!.ActiveOnly);
        Assert.Null(fake.LastRequest.CandidateStates);
    }

    [Fact]
    public async Task Stale_blocked_result_is_not_an_empty_candidate_list()
    {
        var fake = new FakeBillingDashboardReadService(StaleBlockedResult());
        var vm = new BillingDashboardViewModel(fake);

        await vm.LoadAsync().ConfigureAwait(true);

        Assert.Equal(BillingDashboardUiState.FreshnessBlocked, vm.UiState);
        Assert.True(vm.ShowBlockedPanel);
        Assert.False(vm.ShowCandidatesArea);
        Assert.False(vm.ShowOperationalChrome);
        Assert.False(vm.ShowHeaderStatusMessage);
        Assert.False(vm.ShowEmptyFilterState);
        Assert.False(vm.ShowEmptyServiceState);
        Assert.Empty(vm.Rows);
        Assert.Equal(string.Empty, vm.StatusMessage);
        Assert.Equal(BillingDashboardFormatters.BlockedHeadline, vm.BlockedHeadline);
        Assert.DoesNotContain("אין פרויקטים לחיוב", vm.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("אין פרויקטים לחיוב", vm.EmptyListMessage, StringComparison.Ordinal);
        Assert.Equal(BillingDashboardFormatters.EmDash, vm.ReviewNowCountText);
        Assert.Contains("Replica-DEV", vm.DiagnosticsSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Warning_stays_visible_while_candidates_are_available()
    {
        var fake = new FakeBillingDashboardReadService(WarningResult());
        var vm = new BillingDashboardViewModel(fake);

        await vm.LoadAsync().ConfigureAwait(true);

        Assert.Equal(BillingDashboardUiState.Loaded, vm.UiState);
        Assert.True(vm.ShowWarningBanner);
        Assert.True(vm.ShowCandidatesArea);
        Assert.False(vm.ShowBlockedPanel);
        Assert.NotEmpty(vm.Rows);
        Assert.Contains("חריגה", vm.WarningBannerText, StringComparison.Ordinal);
        Assert.Equal("חריגה", vm.FreshnessStatusText);
    }

    [Fact]
    public async Task Text_search_filters_project_and_customer()
    {
        var fake = new FakeBillingDashboardReadService(HealthyResult());
        var vm = new BillingDashboardViewModel(fake);
        await vm.LoadAsync().ConfigureAwait(true);
        vm.ActionableOnly = false;

        vm.FilterText = "גשר";
        Assert.Single(vm.Rows);
        Assert.Equal("5905", vm.Rows[0].ProjectNumber);

        vm.FilterText = "לקוח רפליקה";
        Assert.Equal(2, vm.Rows.Count);

        vm.FilterText = "3611";
        Assert.Single(vm.Rows);
        Assert.Equal("3611", vm.Rows[0].ProjectNumber);
    }

    [Fact]
    public async Task State_filter_narrows_to_service_state()
    {
        var fake = new FakeBillingDashboardReadService(HealthyResult());
        var vm = new BillingDashboardViewModel(fake);
        await vm.LoadAsync().ConfigureAwait(true);

        vm.StateFilter = vm.StateFilterOptions.Single(o => o.State == BillingCandidateState.CoveredByLatestBill);

        var row = Assert.Single(vm.Rows);
        Assert.Equal(BillingCandidateState.CoveredByLatestBill, row.CandidateState);
        Assert.Equal("6982", row.ProjectNumber);
    }

    [Fact]
    public async Task Clearing_filters_restores_all_returned_candidates()
    {
        var fake = new FakeBillingDashboardReadService(HealthyResult());
        var vm = new BillingDashboardViewModel(fake);
        await vm.LoadAsync().ConfigureAwait(true);

        Assert.True(vm.ActionableOnly);
        Assert.Equal(3, vm.Rows.Count);

        vm.FilterText = "אין-התאמה";
        Assert.True(vm.ShowEmptyFilterState);
        Assert.Empty(vm.Rows);

        vm.ClearFilters();

        Assert.False(vm.ActionableOnly);
        Assert.Equal(string.Empty, vm.FilterText);
        Assert.Null(vm.StateFilter.State);
        Assert.Equal(5, vm.Rows.Count);
        Assert.False(vm.ShowEmptyFilterState);
    }

    [Fact]
    public async Task Replica_customer_and_snapshot_enrichment_display_separately()
    {
        var fake = new FakeBillingDashboardReadService(HealthyResult());
        var vm = new BillingDashboardViewModel(fake);
        await vm.LoadAsync().ConfigureAwait(true);
        vm.ActionableOnly = false;

        var row = vm.Rows.Single(r => r.ProjectNumber == "5905");
        Assert.Equal("לקוח רפליקה", row.CustomerName);
        Assert.Equal(BillingDashboardFormatters.Money(12000m), row.SnapshotBalanceText);
        Assert.Equal("יתרה לפי snapshot 02/08/2026", row.SnapshotBalanceCaption);
        Assert.DoesNotContain(BillingDashboardFormatters.CurrentBalanceForbiddenLabel, row.SnapshotBalanceCaption);
        Assert.Equal("מחיר קבוע", row.FeeClassification);
        Assert.Contains("מחיר קבוע ×2", row.FeeTypesTooltip, StringComparison.Ordinal);
        Assert.Equal(BillingCandidateState.ReviewNow, row.CandidateState);
        Assert.Equal(row.Source.CandidateState, row.CandidateState);
    }

    [Fact]
    public async Task Null_snapshot_date_and_amounts_render_unknown_and_dash()
    {
        var fake = new FakeBillingDashboardReadService(HealthyResult());
        var vm = new BillingDashboardViewModel(fake);
        await vm.LoadAsync().ConfigureAwait(true);
        vm.ActionableOnly = false;

        var row = vm.Rows.Single(r => r.ProjectNumber == "3611");
        Assert.Equal(BillingDashboardFormatters.UnknownSnapshotDate, row.SnapshotDateText);
        Assert.Equal("יתרה לפי snapshot תאריך snapshot לא ידוע", row.SnapshotBalanceCaption);
        Assert.Equal(BillingDashboardFormatters.EmDash, row.SnapshotBalanceText);
        Assert.Equal(BillingDashboardFormatters.EmDash, row.SnapshotOpenBillSumText);
        Assert.Equal(BillingDashboardFormatters.EmDash, row.SnapshotBilledPercentText);
        Assert.DoesNotContain("0.00", row.SnapshotBalanceText, StringComparison.Ordinal);
        Assert.NotEqual("0", row.SnapshotBalanceText);
    }

    [Fact]
    public async Task Candidate_state_from_service_is_not_recomputed()
    {
        var mismatched = Candidate(
            99,
            "99",
            "כפי שהשירות אמר",
            hours30: 0m,
            hoursSinceLastBill: 0m,
            state: BillingCandidateState.ReviewNow,
            reason: "השירות קבע לבדוק עכשיו");
        var fake = new FakeBillingDashboardReadService(
            Result(
                BillingReplicaFreshnessStatus.Healthy,
                blocked: false,
                summary: new BillingDashboardSummary(1, 0, null, 0m, LastSync, SnapshotDate, 0, 0, 0),
                mismatched));
        var vm = new BillingDashboardViewModel(fake);
        await vm.LoadAsync().ConfigureAwait(true);

        var row = Assert.Single(vm.Rows);
        Assert.Equal(BillingCandidateState.ReviewNow, row.CandidateState);
        Assert.Equal("השירות קבע לבדוק עכשיו", row.CandidateReason);
        Assert.Equal(0m, row.Source.Hours30);
        Assert.Equal(0m, row.Source.HoursSinceLastBill);
    }

    [Fact]
    public async Task Refresh_calls_the_read_service_again()
    {
        var fake = new FakeBillingDashboardReadService(HealthyResult());
        var vm = new BillingDashboardViewModel(fake);
        await vm.LoadAsync().ConfigureAwait(true);
        Assert.Equal(1, vm.ServiceCallCount);

        await vm.RefreshAsync().ConfigureAwait(true);

        Assert.Equal(2, fake.CallCount);
        Assert.Equal(2, vm.ServiceCallCount);
        Assert.Equal(BillingDashboardUiState.Loaded, vm.UiState);
    }

    [Fact]
    public async Task Service_exception_produces_recoverable_error()
    {
        var fake = new FakeBillingDashboardReadService(HealthyResult())
        {
            Exception = new TimeoutException("Replica timeout")
        };
        var vm = new BillingDashboardViewModel(fake);

        await vm.LoadAsync().ConfigureAwait(true);

        Assert.Equal(BillingDashboardUiState.RecoverableError, vm.UiState);
        Assert.True(vm.ShowErrorBanner);
        Assert.False(vm.ShowCandidatesArea);
        Assert.False(vm.ShowBlockedPanel);
        Assert.False(vm.ShowOperationalChrome);
        Assert.False(vm.ShowHeaderStatusMessage);
        Assert.Equal("Replica timeout", vm.ErrorMessage);
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public async Task InvalidOperationException_produces_fatal_error()
    {
        var fake = new FakeBillingDashboardReadService(HealthyResult())
        {
            Exception = new InvalidOperationException("Replica is required")
        };
        var vm = new BillingDashboardViewModel(fake);

        await vm.LoadAsync().ConfigureAwait(true);

        Assert.Equal(BillingDashboardUiState.FatalError, vm.UiState);
        Assert.True(vm.ShowErrorBanner);
        Assert.False(vm.ShowOperationalChrome);
        Assert.Equal("Replica is required", vm.ErrorMessage);
    }

    [Fact]
    public async Task Healthy_visual_fixture_covers_states_and_snapshot_edges()
    {
        var vm = new BillingDashboardViewModel(BillingDashboardHealthyVisualFixture.CreateService());
        await vm.LoadAsync().ConfigureAwait(true);
        vm.ActionableOnly = false;

        Assert.Equal(BillingDashboardUiState.Loaded, vm.UiState);
        Assert.True(vm.ShowOperationalChrome);
        Assert.InRange(vm.Rows.Count, 10, 15);
        Assert.Contains(vm.Rows, r => r.CandidateState == BillingCandidateState.ReviewNow);
        Assert.Contains(vm.Rows, r => r.CandidateState == BillingCandidateState.AccumulatedWork);
        Assert.Contains(vm.Rows, r => r.CandidateState == BillingCandidateState.BillInPreparation);
        Assert.Contains(vm.Rows, r => r.CandidateState == BillingCandidateState.CoveredByLatestBill);
        Assert.Contains(vm.Rows, r => string.IsNullOrWhiteSpace(r.CustomerName));
        Assert.Contains(vm.Rows, r => r.ProjectName is { Length: > 40 });
        Assert.Contains(vm.Rows, r => r.SnapshotDateText == BillingDashboardFormatters.UnknownSnapshotDate);
        Assert.Contains(vm.Rows, r => r.SnapshotBalanceText == BillingDashboardFormatters.EmDash);
        Assert.Contains(vm.Rows, r => r.FeeClassification == "מחיר קבוע");
        Assert.Contains(vm.Rows, r => r.FeeClassification == "שעות");
        Assert.Contains(vm.Rows, r => r.FeeClassification == "מעורב");
        Assert.Equal("תקין", vm.FreshnessStatusText);
    }

    [Fact]
    public void Snapshot_formatters_never_label_current_balance()
    {
        Assert.Equal("יתרה לפי snapshot 02/08/2026", BillingDashboardFormatters.SnapshotBalanceCaption(SnapshotDate));
        Assert.Equal(BillingDashboardFormatters.UnknownSnapshotDate, BillingDashboardFormatters.SnapshotDateText(null));
        Assert.Equal(BillingDashboardFormatters.EmDash, BillingDashboardFormatters.Money(null));
        Assert.Equal("0.00", BillingDashboardFormatters.Money(0m));
        Assert.DoesNotContain(
            BillingDashboardFormatters.CurrentBalanceForbiddenLabel,
            BillingDashboardFormatters.SnapshotBalanceCaption(null),
            StringComparison.Ordinal);
        Assert.Equal("מעורב", BillingDashboardFormatters.FeeMix(BillingSnapshotFeeMix.Mixed));
        Assert.Equal("שעות", BillingDashboardFormatters.FeeMix(BillingSnapshotFeeMix.Hourly));
        Assert.Equal("אחר", BillingDashboardFormatters.FeeMix(BillingSnapshotFeeMix.Other));
    }

    private static BillingDashboardResult HealthyResult()
    {
        var summary = new BillingDashboardSummary(
            ReviewNowCount: 3,
            BillInPreparationCount: 1,
            SubmittedOpenAmount: null,
            ReceivedThisMonth: 12345.50m,
            ReplicaLastSyncTime: LastSync,
            MonthlySnapshotDate: SnapshotDate,
            AccumulatedWorkCount: 1,
            CoveredByLatestBillCount: 1,
            NotUrgentCount: 1);

        var fee = new BillingFeeTypeSummary(
            [MasterPlanSnapshotFeeTypeIds.FixedPrice],
            [new BillingFeeTypeCount(MasterPlanSnapshotFeeTypeIds.FixedPrice, "מחיר קבוע", 2)],
            BillingSnapshotFeeMix.FixedPrice);

        return Result(
            BillingReplicaFreshnessStatus.Healthy,
            blocked: false,
            summary,
            Candidate(5905, "5905", "גשר הצפון", "לקוח רפליקה", BillingCandidateState.ReviewNow, "שעות אחרי חשבון",
                snapshotBalance: 12000m, snapshotOpen: 2000m, snapshotApproved: 8000m, billedPercent: 40m,
                snapshotDate: SnapshotDate, fees: fee),
            Candidate(5893, "5893", "מגדל", "לקוח רפליקה", BillingCandidateState.BillInPreparation, "חשבון ביצירה",
                snapshotBalance: 100m, snapshotDate: SnapshotDate),
            Candidate(3611, "3611", "כביש", "לקוח אחר", BillingCandidateState.AccumulatedWork, "עבודה ישנה",
                snapshotBalance: null, snapshotDate: null),
            Candidate(6982, "6982", "כיסוי", "לקוח ג", BillingCandidateState.CoveredByLatestBill, "אין שעות חדשות"),
            Candidate(100, "100", "שקט", "לקוח ד", BillingCandidateState.NotUrgent, "אין עבודה"));
    }

    private static BillingDashboardResult WarningResult()
    {
        var healthy = HealthyResult();
        return healthy with
        {
            FreshnessStatus = BillingReplicaFreshnessStatus.Warning,
            Warnings =
            [
                new BillingDataQualityWarning(
                    BillingDataQualityWarningCodes.ReplicaFreshnessWarning,
                    "סנכרון Replica בחריגה — המועמדים מוצגים.")
            ]
        };
    }

    private static BillingDashboardResult StaleBlockedResult() =>
        new(
            new BillingDashboardSummary(0, 0, null, null, LastSync.AddDays(-5), null),
            [],
            Freshness(LastSync.AddDays(-5), null),
            [
                new BillingDataQualityWarning(
                    BillingDataQualityWarningCodes.ReplicaFreshnessFatal,
                    "נתוני MasterPlan אינם עדכניים.")
            ],
            new ReplicaConnectionDiagnostics(
                "Replica-DEV", "Replica_DB", "sql-dev", "DEV-PC", null, "Replica_DB",
                LastSync.AddDays(-5), LastSync.AddDays(-5), LastSync.AddDays(-5),
                LastSync.AddDays(-5), LastSync.AddDays(-5),
                null, null, null, null),
            BillingReplicaFreshnessStatus.Stale,
            CandidatesBlocked: true);

    private static BillingDashboardResult Result(
        BillingReplicaFreshnessStatus status,
        bool blocked,
        BillingDashboardSummary summary,
        params BillingCandidateRow[] rows) =>
        new(
            summary,
            rows,
            Freshness(summary.ReplicaLastSyncTime, summary.MonthlySnapshotDate),
            [],
            ReplicaConnectionDiagnostics.Empty,
            status,
            blocked);

    private static BillingSourceFreshness Freshness(DateTime? lastSync, DateTime? snapshot) =>
        new(
            lastSync,
            lastSync,
            lastSync,
            lastSync,
            lastSync,
            lastSync,
            lastSync,
            snapshot);

    private static BillingCandidateRow Candidate(
        int id,
        string number,
        string name,
        string customer,
        BillingCandidateState state,
        string reason,
        decimal hours30 = 10m,
        decimal hoursSinceLastBill = 10m,
        decimal? snapshotBalance = 1m,
        decimal? snapshotOpen = null,
        decimal? snapshotApproved = null,
        decimal? billedPercent = null,
        DateTime? snapshotDate = null,
        BillingFeeTypeSummary? fees = null) =>
        new(
            id,
            number,
            name,
            customer,
            "פעיל",
            5000m,
            new DateTime(2026, 9, 1),
            hours30,
            hours30,
            hours30,
            hoursSinceLastBill,
            3,
            new DateTime(2026, 6, 1),
            90,
            11,
            "B-11",
            2,
            "הוגש",
            1000m,
            0,
            1,
            0,
            1,
            snapshotBalance,
            snapshotOpen,
            snapshotApproved,
            billedPercent,
            snapshotDate,
            fees,
            state,
            reason);

    private static BillingCandidateRow Candidate(
        int id,
        string number,
        string name,
        decimal hours30,
        decimal hoursSinceLastBill,
        BillingCandidateState state,
        string reason) =>
        Candidate(id, number, name, "לקוח", state, reason, hours30, hoursSinceLastBill, null, null, null, null, null, null);

    private sealed class FakeBillingDashboardReadService(BillingDashboardResult result) : IBillingDashboardReadService
    {
        public int CallCount { get; private set; }
        public BillingDashboardRequest? LastRequest { get; private set; }
        public Exception? Exception { get; set; }

        public Task<BillingDashboardResult> GetAsync(
            BillingDashboardRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            if (Exception is not null)
                throw Exception;
            return Task.FromResult(result);
        }
    }
}
