using SiNet.Application.Billing;
using Xunit;

namespace SiNet.App.Wpf.Tests.Billing;

public sealed class BillingLocalDecisionApplierTests
{
    private static readonly DateTime AsOf = new(2026, 9, 7);

    [Fact]
    public void PrepareBill_attaches_overlay_without_changing_computed_state_or_hours()
    {
        var row = Row(5905, BillingCandidateState.ReviewNow, latestBillId: 10, hours30: 18m, hoursSinceLastBill: 22m);
        var stored = Decision(5905, BillingLocalDecisionType.PrepareBill, observedBillId: 10);

        var applied = Assert.Single(BillingLocalDecisionApplier.Apply([row], [stored], AsOf));

        Assert.Equal(BillingCandidateState.ReviewNow, applied.CandidateState);
        Assert.Equal(18m, applied.Hours30);
        Assert.Equal(22m, applied.HoursSinceLastBill);
        Assert.Equal(10, applied.LatestBillId);
        Assert.Equal("הוגש", applied.LatestBillStatus);
        Assert.Equal(row.CandidateReason, applied.CandidateReason);
        Assert.NotNull(applied.LocalDecision);
        Assert.Equal(BillingLocalDecisionType.PrepareBill, applied.LocalDecision.DecisionType);
        Assert.Equal(BillingLocalDecisionEffect.Active, applied.LocalDecision.Effect);
        Assert.True(BillingLocalDecisionApplier.IncludeInDefaultActionableView(
            applied, BillingDashboardViewModelActionable.States, AsOf));
    }

    [Fact]
    public void Newer_bill_supersedes_PrepareBill_and_does_not_attach_overlay()
    {
        var row = Row(5905, BillingCandidateState.CoveredByLatestBill, latestBillId: 20);
        var stored = Decision(5905, BillingLocalDecisionType.PrepareBill, observedBillId: 10);

        var applied = Assert.Single(BillingLocalDecisionApplier.Apply([row], [stored], AsOf));

        Assert.Null(applied.LocalDecision);
        Assert.Equal(BillingCandidateState.CoveredByLatestBill, applied.CandidateState);
        Assert.Equal(20, applied.LatestBillId);
        Assert.Equal(BillingLocalDecisionEffect.Superseded, BillingLocalDecisionApplier.Evaluate(row, stored, AsOf));
    }

    [Fact]
    public void Same_bill_id_with_status_change_does_not_supersede()
    {
        var row = Row(5905, BillingCandidateState.BillInPreparation, latestBillId: 10) with
        {
            LatestBillStatusId = 3,
            LatestBillStatus = "מאושר"
        };
        var stored = Decision(5905, BillingLocalDecisionType.PrepareBill, observedBillId: 10);

        Assert.Equal(BillingLocalDecisionEffect.Active, BillingLocalDecisionApplier.Evaluate(row, stored, AsOf));
        Assert.NotNull(Assert.Single(BillingLocalDecisionApplier.Apply([row], [stored], AsOf)).LocalDecision);
    }

    [Fact]
    public void First_bill_after_observed_null_supersedes_PrepareBill()
    {
        var row = Row(5905, latestBillId: 44);
        var stored = Decision(5905, BillingLocalDecisionType.PrepareBill, observedBillId: null);

        Assert.Equal(BillingLocalDecisionEffect.Superseded, BillingLocalDecisionApplier.Evaluate(row, stored, AsOf));
        Assert.Null(Assert.Single(BillingLocalDecisionApplier.Apply([row], [stored], AsOf)).LocalDecision);
    }

    [Fact]
    public void Null_current_latest_bill_does_not_count_as_advancement()
    {
        var row = Row(5905, latestBillId: null);
        var stored = Decision(5905, BillingLocalDecisionType.PrepareBill, observedBillId: 10);

        Assert.Equal(BillingLocalDecisionEffect.Active, BillingLocalDecisionApplier.Evaluate(row, stored, AsOf));
    }

    [Fact]
    public void Future_NotNow_is_suppressed_from_default_actionable_view()
    {
        var row = Row(5893, BillingCandidateState.ReviewNow);
        var stored = Decision(
            5893,
            BillingLocalDecisionType.NotNow,
            reason: "ממתינים לאישור לקוח",
            reviewAgain: new DateTime(2026, 9, 15));

        var applied = Assert.Single(BillingLocalDecisionApplier.Apply([row], [stored], AsOf));

        Assert.Equal(BillingCandidateState.ReviewNow, applied.CandidateState);
        Assert.True(BillingLocalDecisionApplier.SuppressFromDefaultActionableView(applied, AsOf));
        Assert.False(BillingLocalDecisionApplier.IncludeInDefaultActionableView(
            applied, BillingDashboardViewModelActionable.States, AsOf));
    }

    [Fact]
    public void Expired_NotNow_becomes_actionable_again()
    {
        var row = Row(5893, BillingCandidateState.ReviewNow);
        var stored = Decision(
            5893,
            BillingLocalDecisionType.NotNow,
            reason: "חזרה אחרי החג",
            reviewAgain: new DateTime(2026, 9, 1));

        Assert.Equal(BillingLocalDecisionEffect.Expired, BillingLocalDecisionApplier.Evaluate(row, stored, AsOf));
        var applied = Assert.Single(BillingLocalDecisionApplier.Apply([row], [stored], AsOf));
        Assert.Null(applied.LocalDecision);
        Assert.True(BillingLocalDecisionApplier.IncludeInDefaultActionableView(
            applied, BillingDashboardViewModelActionable.States, AsOf));
    }

    [Fact]
    public void NotNow_without_date_stays_visible_in_default_actionable_view()
    {
        var row = Row(3611, BillingCandidateState.AccumulatedWork);
        var stored = Decision(3611, BillingLocalDecisionType.NotNow, reason: "בדיקה ידנית", reviewAgain: null);

        var applied = Assert.Single(BillingLocalDecisionApplier.Apply([row], [stored], AsOf));

        Assert.NotNull(applied.LocalDecision);
        Assert.False(BillingLocalDecisionApplier.SuppressFromDefaultActionableView(applied, AsOf));
        Assert.True(BillingLocalDecisionApplier.IncludeInDefaultActionableView(
            applied, BillingDashboardViewModelActionable.States, AsOf));
    }

    [Fact]
    public void Newer_bill_supersedes_NotNow_hold()
    {
        var row = Row(6982, BillingCandidateState.BillInPreparation, latestBillId: 90);
        var stored = Decision(
            6982,
            BillingLocalDecisionType.NotNow,
            reason: "לחכות",
            reviewAgain: new DateTime(2026, 10, 1),
            observedBillId: 70);

        Assert.Equal(BillingLocalDecisionEffect.Superseded, BillingLocalDecisionApplier.Evaluate(row, stored, AsOf));
        Assert.Null(Assert.Single(BillingLocalDecisionApplier.Apply([row], [stored], AsOf)).LocalDecision);
    }

    [Fact]
    public void Cleared_decision_does_not_attach_overlay()
    {
        var row = Row(100);
        var stored = Decision(100, BillingLocalDecisionType.PrepareBill, observedBillId: null) with
        {
            ClearedAtUtc = AsOf.ToUniversalTime()
        };

        var applied = Assert.Single(BillingLocalDecisionApplier.Apply([row], [stored], AsOf));
        Assert.Null(applied.LocalDecision);
        Assert.Equal(row.CandidateState, applied.CandidateState);
    }

    [Fact]
    public void PrepareBill_keeps_non_actionable_computed_state_visible_in_default_view()
    {
        var row = Row(200, BillingCandidateState.CoveredByLatestBill, latestBillId: 3);
        var stored = Decision(200, BillingLocalDecisionType.PrepareBill, observedBillId: 3);

        var applied = Assert.Single(BillingLocalDecisionApplier.Apply([row], [stored], AsOf));
        Assert.Equal(BillingCandidateState.CoveredByLatestBill, applied.CandidateState);
        Assert.True(BillingLocalDecisionApplier.IncludeInDefaultActionableView(
            applied, BillingDashboardViewModelActionable.States, AsOf));
    }

    private static BillingCandidateRow Row(
        int projectId,
        BillingCandidateState state = BillingCandidateState.ReviewNow,
        int? latestBillId = null,
        decimal hours30 = 10m,
        decimal hoursSinceLastBill = 10m) =>
        new(
            projectId,
            projectId.ToString(),
            "פרויקט",
            "לקוח",
            "פעיל",
            1000m,
            new DateTime(2026, 9, 1),
            hours30,
            hours30,
            hours30,
            hoursSinceLastBill,
            3,
            latestBillId is null ? null : new DateTime(2026, 6, 1),
            latestBillId is null ? null : 90,
            latestBillId,
            latestBillId?.ToString(),
            latestBillId is null ? null : 2,
            latestBillId is null ? null : "הוגש",
            1000m,
            0,
            1,
            0,
            1,
            1m,
            null,
            null,
            null,
            null,
            null,
            state,
            "סיבה מחושבת");

    private static BillingReviewDecisionRecord Decision(
        int projectId,
        BillingLocalDecisionType type,
        string? reason = "הערה",
        DateTime? reviewAgain = null,
        int? observedBillId = null) =>
        new(
            projectId,
            type,
            reason,
            reviewAgain,
            new DateTime(2026, 9, 6, 10, 0, 0, DateTimeKind.Utc),
            7,
            "manager",
            null,
            null,
            null,
            null,
            null,
            null,
            observedBillId,
            observedBillId is null ? null : 2,
            observedBillId is null ? null : new DateTime(2026, 6, 1),
            10m,
            new DateTime(2026, 9, 1));
}

internal static class BillingDashboardViewModelActionable
{
    public static IReadOnlyCollection<BillingCandidateState> States =>
        SiNet.App.Wpf.Billing.BillingDashboardViewModel.DefaultActionableStates;
}
