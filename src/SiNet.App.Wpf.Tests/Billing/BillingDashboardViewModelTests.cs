using SiNet.App.Wpf.Billing;
using SiNet.Application.Billing;
using SiNet.Application.Identity;
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
    public async Task Future_NotNow_is_hidden_from_default_actionable_filter()
    {
        var overlay = ActiveNotNow(new DateTime(2026, 9, 15), "ממתינים ללקוח");
        var held = Candidate(5905, "5905", "גשר הצפון", "לקוח רפליקה", BillingCandidateState.ReviewNow, "שעות")
            with { LocalDecision = overlay };
        var other = Candidate(5893, "5893", "מגדל", "לקוח", BillingCandidateState.AccumulatedWork, "עבודה");
        var fake = new FakeBillingDashboardReadService(
            Result(
                BillingReplicaFreshnessStatus.Healthy,
                blocked: false,
                new BillingDashboardSummary(1, 0, null, 0m, LastSync, SnapshotDate, 1, 0, 0),
                held,
                other));
        var vm = new BillingDashboardViewModel(fake, new FrozenLocalClock(new DateTime(2026, 9, 7)));
        await vm.LoadAsync().ConfigureAwait(true);

        Assert.True(vm.ActionableOnly);
        var visible = Assert.Single(vm.Rows);
        Assert.Equal("5893", visible.ProjectNumber);

        vm.ActionableOnly = false;
        Assert.Equal(2, vm.Rows.Count);

        vm.StateFilter = BillingDashboardStateFilterOption.Held;
        var heldRow = Assert.Single(vm.Rows);
        Assert.Equal("5905", heldRow.ProjectNumber);
        Assert.Equal("מושהה עד 15/09", heldRow.LocalDecisionLabel);
        Assert.Equal(BillingCandidateState.ReviewNow, heldRow.CandidateState);
    }

    [Fact]
    public async Task Active_PrepareBill_stays_in_default_actionable_view()
    {
        var overlay = ActivePrepare();
        var marked = Candidate(6982, "6982", "כיסוי", "לקוח", BillingCandidateState.CoveredByLatestBill, "אין שעות")
            with { LocalDecision = overlay };
        var fake = new FakeBillingDashboardReadService(
            Result(
                BillingReplicaFreshnessStatus.Healthy,
                blocked: false,
                new BillingDashboardSummary(0, 0, null, 0m, LastSync, SnapshotDate, 0, 1, 0),
                marked));
        var vm = new BillingDashboardViewModel(fake, new FrozenLocalClock(new DateTime(2026, 9, 7)));
        await vm.LoadAsync().ConfigureAwait(true);

        var row = Assert.Single(vm.Rows);
        Assert.Equal(BillingCandidateState.CoveredByLatestBill, row.CandidateState);
        Assert.Equal("להכין חשבון", row.LocalDecisionLabel);
        Assert.True(row.HasActivePrepareBill);
    }

    [Fact]
    public async Task PrepareBill_command_saves_observed_facts_without_changing_candidate_state()
    {
        var source = Candidate(5905, "5905", "גשר", "לקוח", BillingCandidateState.ReviewNow, "שעות",
            hours30: 18m, hoursSinceLastBill: 22m);
        var fake = new FakeBillingDashboardReadService(
            Result(
                BillingReplicaFreshnessStatus.Healthy,
                blocked: false,
                new BillingDashboardSummary(1, 0, null, 0m, LastSync, SnapshotDate, 0, 0, 0),
                source));
        var write = new RecordingWrite();
        var vm = new BillingDashboardViewModel(
            fake,
            new FrozenLocalClock(new DateTime(2026, 9, 7)),
            reviewDecisions: write,
            authorization: new StubAuth(true),
            prompts: new FakePrompts());
        await vm.LoadAsync().ConfigureAwait(true);

        Assert.True(vm.CanWriteBillingDecisions);
        Assert.True(vm.ShowDecisionWriteButtons);
        await vm.PrepareBillAsync().ConfigureAwait(true);

        var saved = Assert.Single(write.Saves);
        Assert.Equal(5905, saved.ProjectId);
        Assert.Equal(BillingLocalDecisionType.PrepareBill, saved.DecisionType);
        Assert.Equal(18m, source.Hours30);
        Assert.Equal(BillingCandidateState.ReviewNow, vm.Selected!.CandidateState);
        Assert.Equal(source.HoursSinceLastBill, saved.ObservedHoursSinceLastBill);
        Assert.Equal(source.LatestBillId, saved.ObservedLatestBillId);
    }

    [Fact]
    public async Task PrepareBill_creates_decision_and_one_preparation_request()
    {
        var source = Candidate(5905, "5905", "גשר", "לקוח", BillingCandidateState.ReviewNow, "שעות",
            hours30: 18m, hoursSinceLastBill: 22m);
        var write = new RecordingWrite();
        var prep = new InMemoryPreparation();
        var vm = CreateDecisionVm(source, write, prep);
        await vm.LoadAsync().ConfigureAwait(true);

        await vm.PrepareBillAsync().ConfigureAwait(true);

        var saved = Assert.Single(write.Saves);
        Assert.Equal(BillingLocalDecisionType.PrepareBill, saved.DecisionType);
        var request = Assert.Single(prep.Requests);
        Assert.Equal(5905, request.MasterPlanProjectId);
        Assert.Equal(1, prep.EnsureCalls);
        Assert.Equal(1, vm.SelectedWorkspaceTab);
        Assert.False(vm.ShowContinuePrepareBillButton);
        Assert.False(vm.ShowOperationErrorBanner);
        Assert.True(vm.ShowCandidatesArea);
    }

    [Fact]
    public async Task PrepareBill_preparation_failure_keeps_decision_and_shows_operation_error()
    {
        var source = Candidate(5905, "5905", "גשר", "לקוח", BillingCandidateState.ReviewNow, "שעות",
            hours30: 18m, hoursSinceLastBill: 22m);
        var write = new RecordingWrite();
        var prep = new InMemoryPreparation { ThrowOnEnsure = true };
        var vm = CreateDecisionVm(source, write, prep);
        await vm.LoadAsync().ConfigureAwait(true);

        await vm.PrepareBillAsync().ConfigureAwait(true);

        var saved = Assert.Single(write.Saves);
        Assert.Equal(BillingLocalDecisionType.PrepareBill, saved.DecisionType);
        Assert.Empty(prep.Requests);
        Assert.Equal(0, vm.SelectedWorkspaceTab);
        Assert.True(vm.HasActivePrepareBill);
        Assert.True(vm.ShowContinuePrepareBillButton);
        Assert.True(vm.ShowOperationErrorBanner);
        Assert.False(vm.ShowErrorBanner);
        Assert.True(vm.ShowCandidatesArea);
        Assert.Contains("Invalid column name 'Name'", vm.OperationErrorMessage, StringComparison.Ordinal);
        Assert.StartsWith("לא ניתן היה לפתוח בקשת הכנת חשבון:", vm.OperationErrorMessage, StringComparison.Ordinal);
        Assert.Equal(BillingDashboardUiState.Loaded, vm.UiState);
    }

    [Fact]
    public async Task Continue_prepare_bill_creates_exactly_one_request_without_another_decision()
    {
        var source = Candidate(5905, "5905", "גשר", "לקוח", BillingCandidateState.ReviewNow, "שעות")
            with { LocalDecision = ActivePrepare() };
        var write = new RecordingWrite();
        var prep = new InMemoryPreparation();
        var vm = CreateDecisionVm(source, write, prep);
        await vm.LoadAsync().ConfigureAwait(true);

        Assert.True(vm.ShowContinuePrepareBillButton);
        await vm.ContinuePrepareBillAsync().ConfigureAwait(true);

        Assert.Empty(write.Saves);
        Assert.Single(prep.Requests);
        Assert.Equal(1, prep.EnsureCalls);
        Assert.Equal(1, vm.SelectedWorkspaceTab);
        Assert.False(vm.ShowContinuePrepareBillButton);
        Assert.False(vm.ShowOperationErrorBanner);
    }

    [Fact]
    public async Task Continue_prepare_bill_twice_still_has_exactly_one_request()
    {
        var source = Candidate(5905, "5905", "גשר", "לקוח", BillingCandidateState.ReviewNow, "שעות")
            with { LocalDecision = ActivePrepare() };
        var write = new RecordingWrite();
        var prep = new InMemoryPreparation();
        var vm = CreateDecisionVm(source, write, prep);
        await vm.LoadAsync().ConfigureAwait(true);

        await vm.ContinuePrepareBillAsync().ConfigureAwait(true);
        await vm.ContinuePrepareBillAsync().ConfigureAwait(true);

        Assert.Single(prep.Requests);
        Assert.Equal(1, prep.EnsureCalls);
    }

    [Fact]
    public async Task Active_prepare_bill_with_existing_request_does_not_duplicate()
    {
        var source = Candidate(5905, "5905", "גשר", "לקוח", BillingCandidateState.ReviewNow, "שעות")
            with { LocalDecision = ActivePrepare() };
        var write = new RecordingWrite();
        var prep = new InMemoryPreparation();
        prep.Requests.Add(Request(5905, 9));
        var vm = CreateDecisionVm(source, write, prep);
        await vm.LoadAsync().ConfigureAwait(true);

        Assert.False(vm.ShowContinuePrepareBillButton);
        await vm.ContinuePrepareBillAsync().ConfigureAwait(true);

        Assert.Empty(write.Saves);
        Assert.Equal(0, prep.EnsureCalls);
        Assert.Single(prep.Requests);
    }

    [Fact]
    public async Task PrepareBill_preparation_failure_stays_on_candidates_and_shows_error_banner()
    {
        var source = Candidate(5905, "5905", "גשר", "לקוח", BillingCandidateState.ReviewNow, "שעות",
            hours30: 18m, hoursSinceLastBill: 22m);
        var write = new RecordingWrite();
        var prep = new InMemoryPreparation { ThrowOnEnsure = true };
        var vm = CreateDecisionVm(source, write, prep);
        await vm.LoadAsync().ConfigureAwait(true);

        await vm.PrepareBillAsync().ConfigureAwait(true);

        Assert.True(vm.ShowOperationErrorBanner);
        Assert.True(vm.ShowCandidatesArea);
        Assert.Empty(prep.Requests);
    }

    [Fact]
    public void Healthy_fixture_constructor_may_omit_preparation_service()
    {
        var vm = new BillingDashboardViewModel(BillingDashboardHealthyVisualFixture.CreateService());
        Assert.False(vm.HasPreparationService);
    }

    [Fact]
    public async Task Employee_cannot_write_from_the_view_model()
    {
        var fake = new FakeBillingDashboardReadService(HealthyResult());
        var write = new RecordingWrite();
        var vm = new BillingDashboardViewModel(
            fake,
            reviewDecisions: write,
            authorization: new StubAuth(false),
            prompts: new FakePrompts());
        await vm.LoadAsync().ConfigureAwait(true);

        Assert.False(vm.CanWriteBillingDecisions);
        Assert.False(vm.ShowDecisionWriteButtons);
        await vm.PrepareBillAsync().ConfigureAwait(true);
        Assert.Empty(write.Saves);
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

    private BillingDashboardViewModel CreateDecisionVm(
        BillingCandidateRow source,
        RecordingWrite write,
        IBillingPreparationService preparation)
    {
        var inner = new FakeBillingDashboardReadService(
            Result(
                BillingReplicaFreshnessStatus.Healthy,
                blocked: false,
                new BillingDashboardSummary(1, 0, null, 0m, LastSync, SnapshotDate, 0, 0, 0),
                source));
        return new BillingDashboardViewModel(
            new OverlayingReadService(inner, write),
            new FrozenLocalClock(new DateTime(2026, 9, 7)),
            reviewDecisions: write,
            authorization: new StubAuth(true),
            prompts: new FakePrompts(),
            preparation: preparation);
    }

    private static BillingPreparationRequestRecord Request(int masterPlanProjectId, int id) =>
        new(
            id,
            masterPlanProjectId,
            12,
            "5905",
            "גשר",
            "לקוח",
            BillingPreparationStatus.WaitingForSelection,
            new DateTime(2026, 9, 7, 10, 0, 0, DateTimeKind.Utc),
            7,
            "manager",
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            null,
            null,
            null,
            [],
            []);

    private static BillingLocalDecisionOverlay ActiveNotNow(DateTime reviewAgain, string reason) =>
        new(
            BillingLocalDecisionType.NotNow,
            reason,
            reviewAgain,
            new DateTime(2026, 9, 6, 10, 0, 0, DateTimeKind.Utc),
            7,
            "manager",
            null,
            null,
            null,
            BillingLocalDecisionEffect.Active);

    private static BillingLocalDecisionOverlay ActivePrepare() =>
        new(
            BillingLocalDecisionType.PrepareBill,
            null,
            null,
            new DateTime(2026, 9, 6, 10, 0, 0, DateTimeKind.Utc),
            7,
            "manager",
            null,
            null,
            null,
            BillingLocalDecisionEffect.Active);

    private sealed class FrozenLocalClock(DateTime localDate) : TimeProvider
    {
        private readonly DateTimeOffset _utcNow = new(DateTime.SpecifyKind(localDate, DateTimeKind.Utc));

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class StubAuth(bool allow) : IAuthorizationQueryService
    {
        public Task<bool> IsCurrentUserInRoleAsync(AppRole requiredRole, CancellationToken cancellationToken = default) =>
            Task.FromResult(allow);

        public Task<bool> CanCurrentUserAccessFeatureAsync(
            string featureCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(allow && featureCode == AppFeatureCodes.BillingRecordReviewDecision);
    }

    private sealed class FakePrompts : IBillingReviewPrompts
    {
        public bool ConfirmPrepareBill(string projectLabel) => true;

        public BillingNotNowPromptResult? PromptNotNow(string projectLabel) =>
            new("סיבת בדיקה", new DateTime(2026, 9, 20));

        public bool ConfirmClearDecision(string projectLabel) => true;

        public BillingUnsavedEditsDecision ConfirmDiscardUnsavedPreparationEdits() =>
            BillingUnsavedEditsDecision.Stay;
    }

    private sealed class RecordingWrite : IBillingReviewDecisionService
    {
        public List<BillingReviewDecisionWriteRequest> Saves { get; } = [];
        public List<int> Cleared { get; } = [];

        public Task SaveAsync(
            BillingReviewDecisionWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            Saves.Add(request);
            return Task.CompletedTask;
        }

        public Task ClearAsync(int projectId, CancellationToken cancellationToken = default)
        {
            Cleared.Add(projectId);
            return Task.CompletedTask;
        }
    }

    private sealed class OverlayingReadService(
        FakeBillingDashboardReadService inner,
        RecordingWrite write) : IBillingDashboardReadService
    {
        public async Task<BillingDashboardResult> GetAsync(
            BillingDashboardRequest request,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.GetAsync(request, cancellationToken).ConfigureAwait(false);
            var rows = result.Candidates.Select(ApplySavedDecision).ToArray();
            return result with { Candidates = rows };
        }

        private BillingCandidateRow ApplySavedDecision(BillingCandidateRow candidate)
        {
            if (candidate.LocalDecision is { Effect: BillingLocalDecisionEffect.Active })
                return candidate;
            var saved = write.Saves.LastOrDefault(s => s.ProjectId == candidate.ProjectId);
            return saved?.DecisionType == BillingLocalDecisionType.PrepareBill
                ? candidate with { LocalDecision = ActivePrepare() }
                : candidate;
        }
    }

    private sealed class InMemoryPreparation : IBillingPreparationService
    {
        public List<BillingPreparationRequestRecord> Requests { get; } = [];
        public int EnsureCalls { get; private set; }
        public bool ThrowOnEnsure { get; set; }

        public Task<BillingPreparationEnsureResult> EnsureFromPrepareBillAsync(
            int masterPlanProjectId,
            string? projectNumber,
            string? projectName,
            string? customerName,
            CancellationToken cancellationToken = default)
        {
            EnsureCalls++;
            if (ThrowOnEnsure)
                throw new InvalidOperationException("Invalid column name 'Name'.");

            var existing = Requests.FirstOrDefault(r => r.MasterPlanProjectId == masterPlanProjectId);
            if (existing is not null)
                return Task.FromResult(new BillingPreparationEnsureResult(existing, Created: false));

            var created = Request(masterPlanProjectId, Requests.Count + 1);
            Requests.Add(created);
            return Task.FromResult(new BillingPreparationEnsureResult(created, Created: true));
        }

        public Task<IReadOnlyList<BillingPreparationRequestRecord>> ListForPreparationTabAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<BillingPreparationRequestRecord>>(Requests);

        public Task<BillingPreparationRequestRecord> RefreshFromSnapshotAsync(
            int requestId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BillingPreparationRequestRecord> SaveSelectionAsync(
            int requestId,
            IReadOnlyList<BillingPreparationStageLineSnapshot> stages,
            IReadOnlyList<BillingPreparationHoursLineSnapshot> hours,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BillingPreparationRequestRecord> ApplyManualOverrideAsync(
            int requestId,
            string reason,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BillingPreparationApproveResult> ApproveAndCreateTaskAsync(
            int requestId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BillingPreparationRequestRecord> OnPrepareBillTaskCompletedAsync(
            int requestId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BillingPreparationRequestRecord> ConfirmHourlyManuallyAsync(
            int requestId,
            string? note,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BillingPreparationRequestRecord> ReevaluateStageConfirmationAsync(
            int requestId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task OnPrepareBillTaskCompletedByTaskIdAsync(
            int taskId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BillingPreparationSnapshotLoad> LoadComponentsAsync(
            int requestId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new BillingPreparationSnapshotLoad(false, null, null, null, null, [], [], []));

        public Task<IReadOnlyList<int>> FindHourReportIdsInOtherRequestsAsync(
            IReadOnlyList<int> hourReportIds,
            int? excludeRequestId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<int>>([]);
    }

    private sealed class ThrowingPreparation : IBillingPreparationService
    {
        public Task<BillingPreparationEnsureResult> EnsureFromPrepareBillAsync(
            int masterPlanProjectId,
            string? projectNumber,
            string? projectName,
            string? customerName,
            CancellationToken cancellationToken = default)
            => Task.FromException<BillingPreparationEnsureResult>(
                new InvalidOperationException("Invalid column name 'Name'."));

        public Task<IReadOnlyList<BillingPreparationRequestRecord>> ListForPreparationTabAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<BillingPreparationRequestRecord>>([]);

        public Task<BillingPreparationRequestRecord> RefreshFromSnapshotAsync(
            int requestId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BillingPreparationRequestRecord> SaveSelectionAsync(
            int requestId,
            IReadOnlyList<BillingPreparationStageLineSnapshot> stages,
            IReadOnlyList<BillingPreparationHoursLineSnapshot> hours,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BillingPreparationRequestRecord> ApplyManualOverrideAsync(
            int requestId,
            string reason,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BillingPreparationApproveResult> ApproveAndCreateTaskAsync(
            int requestId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BillingPreparationRequestRecord> OnPrepareBillTaskCompletedAsync(
            int requestId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BillingPreparationRequestRecord> ConfirmHourlyManuallyAsync(
            int requestId,
            string? note,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BillingPreparationRequestRecord> ReevaluateStageConfirmationAsync(
            int requestId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task OnPrepareBillTaskCompletedByTaskIdAsync(
            int taskId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BillingPreparationSnapshotLoad> LoadComponentsAsync(
            int requestId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<int>> FindHourReportIdsInOtherRequestsAsync(
            IReadOnlyList<int> hourReportIds,
            int? excludeRequestId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<int>>([]);
    }

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
