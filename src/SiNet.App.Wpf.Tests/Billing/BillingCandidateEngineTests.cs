using SiNet.Application.Billing;
using Xunit;

namespace SiNet.App.Wpf.Tests.Billing;

public sealed class BillingCandidateEngineTests
{
    private static readonly DateTime AsOf = new(2026, 9, 6);

    [Fact]
    public void When_bill_in_creation_then_never_ReviewNow()
    {
        // Shape similar to research project 5893 — hours exist but a bill is already in creation.
        var rows = BillingCandidateEngine.BuildRows(
            [Project(5893)],
            [Bill(1, 5893, MasterPlanBillStatusIds.InCreation, AsOf.AddDays(-2))],
            [Hour(5893, AsOf.AddDays(-1), 12m), Hour(5893, AsOf.AddDays(-40), 80m)],
            AsOf);

        var row = Assert.Single(rows);
        Assert.Equal(BillingCandidateState.BillInPreparation, row.CandidateState);
        Assert.Contains("חשבון ביצירה", row.CandidateReason, StringComparison.Ordinal);
        Assert.Equal(12m, row.Hours30);
        Assert.True(row.HoursSinceLastBill > 0m);
    }

    [Fact]
    public void When_hours_since_real_bill_are_zero_then_CoveredByLatestBill()
    {
        // Shape similar to research project 6982 — recent activity but HoursSinceLastBill = 0.
        var lastBill = new DateTime(2026, 9, 1);
        var rows = BillingCandidateEngine.BuildRows(
            [Project(6982)],
            [Bill(10, 6982, MasterPlanBillStatusIds.Submitted, lastBill, sum: 5000m)],
            [
                Hour(6982, lastBill, 8m),
                Hour(6982, lastBill.AddDays(-1), 96.75m),
            ],
            AsOf);

        var row = Assert.Single(rows);
        Assert.Equal(BillingCandidateState.CoveredByLatestBill, row.CandidateState);
        Assert.Equal(0m, row.HoursSinceLastBill);
        Assert.Equal(104.75m, row.Hours30);
        Assert.Equal(lastBill.Date, row.LastBillDate);
        Assert.Equal(10, row.LatestBillId);
        Assert.Null(row.SnapshotBalance);
        Assert.Null(row.SnapshotDate);
    }

    [Fact]
    public void When_recent_hours_after_last_bill_then_ReviewNow()
    {
        // Shape similar to research project 5905 — strong ReviewNow.
        var lastBill = AsOf.AddDays(-115);
        var rows = BillingCandidateEngine.BuildRows(
            [Project(5905, feeSum: 120000m)],
            [Bill(20, 5905, MasterPlanBillStatusIds.Approved, lastBill)],
            [
                Hour(5905, AsOf.AddDays(-2), 38.67m),
                Hour(5905, AsOf.AddDays(-40), 162.58m),
            ],
            AsOf);

        var row = Assert.Single(rows);
        Assert.Equal(BillingCandidateState.ReviewNow, row.CandidateState);
        Assert.Equal(201.25m, row.HoursSinceLastBill);
        Assert.Equal(38.67m, row.Hours30);
        Assert.Equal(1, row.WorkDays30);
        Assert.Equal(115, row.DaysSinceLastBill);
        Assert.Contains("אין חשבון ביצירה", row.CandidateReason, StringComparison.Ordinal);
        Assert.Equal(120000m, row.CurrentFeeSum);
    }

    [Fact]
    public void When_no_hours30_but_older_hours_since_bill_then_AccumulatedWork()
    {
        // Shape similar to research project 3611 — accumulated work, no recent 30-day activity.
        var lastBill = AsOf.AddDays(-200);
        var rows = BillingCandidateEngine.BuildRows(
            [Project(3611)],
            [Bill(30, 3611, MasterPlanBillStatusIds.Closed, lastBill)],
            [Hour(3611, AsOf.AddDays(-45), 40m)],
            AsOf);

        var row = Assert.Single(rows);
        Assert.Equal(BillingCandidateState.AccumulatedWork, row.CandidateState);
        Assert.Equal(40m, row.HoursSinceLastBill);
        Assert.Equal(0m, row.Hours30);
        Assert.Equal(0, row.WorkDays30);
    }

    [Fact]
    public void When_no_work_and_no_bills_then_NotUrgent()
    {
        var rows = BillingCandidateEngine.BuildRows(
            [Project(1)],
            [],
            [],
            AsOf);

        var row = Assert.Single(rows);
        Assert.Equal(BillingCandidateState.NotUrgent, row.CandidateState);
        Assert.Equal(0m, row.HoursSinceLastBill);
        Assert.Null(row.LastBillDate);
        Assert.Null(row.LatestBillId);
        Assert.Null(row.DaysSinceLastBill);
    }

    [Fact]
    public void When_project_has_no_previous_real_bill_then_all_hours_count_since_last_bill()
    {
        var rows = BillingCandidateEngine.BuildRows(
            [Project(2)],
            [Bill(99, 2, MasterPlanBillStatusIds.InCreation, AsOf)],
            [Hour(2, AsOf.AddDays(-10), 5m)],
            AsOf);

        var row = Assert.Single(rows);
        Assert.Equal(5m, row.HoursSinceLastBill);
        Assert.Null(row.LastBillDate);
        Assert.Equal(99, row.LatestBillId);
        Assert.Equal(MasterPlanBillStatusIds.InCreation, row.LatestBillStatusId);
        Assert.Equal(BillingCandidateState.BillInPreparation, row.CandidateState);
    }

    [Fact]
    public void When_latest_is_in_creation_then_last_real_bill_still_resets_hours()
    {
        var realBillDate = AsOf.AddDays(-10);
        var rows = BillingCandidateEngine.BuildRows(
            [Project(3)],
            [
                Bill(1, 3, MasterPlanBillStatusIds.Submitted, realBillDate),
                Bill(2, 3, MasterPlanBillStatusIds.InCreation, AsOf),
            ],
            [
                Hour(3, realBillDate, 8m),
                Hour(3, AsOf.AddDays(-1), 3m),
            ],
            AsOf);

        var row = Assert.Single(rows);
        Assert.Equal(2, row.LatestBillId);
        Assert.Equal(MasterPlanBillStatusIds.InCreation, row.LatestBillStatusId);
        Assert.Equal(realBillDate.Date, row.LastBillDate);
        Assert.Equal(3m, row.HoursSinceLastBill);
        Assert.Equal(1, row.BillsInCreation);
        Assert.Equal(1, row.BillsSubmitted);
        Assert.Equal(2, row.NonClosedBills);
        Assert.Equal(BillingCandidateState.BillInPreparation, row.CandidateState);
    }

    [Fact]
    public void When_hours_on_last_bill_date_then_they_are_excluded_from_HoursSinceLastBill()
    {
        var lastBill = new DateTime(2026, 8, 1);
        var rows = BillingCandidateEngine.BuildRows(
            [Project(4)],
            [Bill(4, 4, MasterPlanBillStatusIds.Submitted, lastBill)],
            [
                Hour(4, lastBill, 10m),
                Hour(4, lastBill.AddDays(1), 2m),
            ],
            AsOf);

        var row = Assert.Single(rows);
        Assert.Equal(2m, row.HoursSinceLastBill);
    }

    [Fact]
    public void When_hours_windows_then_30_60_90_use_inclusive_as_of_rolling_days()
    {
        var rows = BillingCandidateEngine.BuildRows(
            [Project(5)],
            [],
            [
                Hour(5, AsOf, 1m),
                Hour(5, AsOf.AddDays(-30), 2m),
                Hour(5, AsOf.AddDays(-31), 4m),
                Hour(5, AsOf.AddDays(-60), 8m),
                Hour(5, AsOf.AddDays(-61), 16m),
                Hour(5, AsOf.AddDays(-90), 32m),
                Hour(5, AsOf.AddDays(-91), 64m),
            ],
            AsOf);

        var row = Assert.Single(rows);
        Assert.Equal(3m, row.Hours30);
        Assert.Equal(15m, row.Hours60);
        Assert.Equal(63m, row.Hours90);
        Assert.Equal(2, row.WorkDays30);
        Assert.Equal(AsOf.Date, row.LastWorkDate);
    }

    [Fact]
    public void When_same_calendar_day_has_two_hour_rows_then_WorkDays30_counts_once()
    {
        var rows = BillingCandidateEngine.BuildRows(
            [Project(6)],
            [],
            [
                Hour(6, AsOf, 1m),
                Hour(6, AsOf.AddHours(3), 2m),
            ],
            AsOf);

        var row = Assert.Single(rows);
        Assert.Equal(1, row.WorkDays30);
        Assert.Equal(3m, row.Hours30);
    }

    [Fact]
    public void When_multiple_projects_then_ReviewNow_sorts_by_hours_since_bill()
    {
        var lastBill = AsOf.AddDays(-10);
        var rows = BillingCandidateEngine.BuildRows(
            [Project(10, "B"), Project(11, "A")],
            [
                Bill(1, 10, MasterPlanBillStatusIds.Submitted, lastBill),
                Bill(2, 11, MasterPlanBillStatusIds.Submitted, lastBill),
            ],
            [
                Hour(10, AsOf, 5m),
                Hour(11, AsOf, 20m),
            ],
            AsOf);

        Assert.Equal(11, rows[0].ProjectId);
        Assert.Equal(10, rows[1].ProjectId);
        Assert.All(rows, r => Assert.Equal(BillingCandidateState.ReviewNow, r.CandidateState));
    }

    [Fact]
    public void When_customer_name_is_null_then_row_keeps_null_without_inventing_a_fallback()
    {
        var project = new BillingProjectFact(
            7, "7", "P", CustomerName: null, CustomerId: 9, "פעיל", true, 0m);
        var rows = BillingCandidateEngine.BuildRows([project], [], [], AsOf);
        Assert.Null(Assert.Single(rows).CustomerName);
    }

    [Fact]
    public void When_in_creation_bill_is_later_then_it_does_not_reset_HoursSinceLastBill()
    {
        var realBill = new DateTime(2026, 6, 1);
        var rows = BillingCandidateEngine.BuildRows(
            [Project(8)],
            [
                Bill(1, 8, MasterPlanBillStatusIds.Submitted, realBill),
                Bill(2, 8, MasterPlanBillStatusIds.InCreation, AsOf, lastUpdated: AsOf),
            ],
            [
                Hour(8, realBill, 10m),
                Hour(8, realBill.AddDays(1), 152m),
            ],
            AsOf);

        var row = Assert.Single(rows);
        Assert.Equal(152m, row.HoursSinceLastBill);
        Assert.Equal(realBill.Date, row.LastBillDate);
        Assert.Equal(1, row.BillsInCreation);
        Assert.Equal(MasterPlanBillStatusIds.InCreation, row.LatestBillStatusId);
    }

    [Fact]
    public void When_hours_exist_and_no_bills_at_all_then_all_hours_count_as_since_last_bill()
    {
        var rows = BillingCandidateEngine.BuildRows(
            [Project(9)],
            [],
            [Hour(9, AsOf.AddDays(-10), 7m), Hour(9, AsOf.AddDays(-40), 3m)],
            AsOf);

        var row = Assert.Single(rows);
        Assert.Equal(10m, row.HoursSinceLastBill);
        Assert.Null(row.LastBillDate);
        Assert.Null(row.LatestBillId);
        Assert.Equal(0, row.BillsInCreation);
        Assert.Equal(BillingCandidateState.ReviewNow, row.CandidateState);
    }

    [Fact]
    public void When_submit_date_exists_then_later_LastUpdated_is_ignored_for_effective_date()
    {
        var submit = new DateTime(2026, 7, 1);
        var lastUpdated = new DateTime(2026, 9, 5);
        var bill = new BillingBillFact(
            50, 12, "B-50", 1m, MasterPlanBillStatusIds.Submitted, "הוגש", submit, lastUpdated);

        Assert.Equal(submit, BillingBillTimeline.EffectiveDate(bill));
        Assert.False(BillingBillTimeline.UsesLastUpdatedFallback(bill));

        var rows = BillingCandidateEngine.BuildRows(
            [Project(12)],
            [bill],
            [Hour(12, submit, 4m), Hour(12, submit.AddDays(1), 6m)],
            AsOf);

        var row = Assert.Single(rows);
        Assert.Equal(submit.Date, row.LastBillDate);
        Assert.Equal(6m, row.HoursSinceLastBill);
    }

    [Fact]
    public void When_real_bill_has_no_SubmitDate_then_LastUpdated_is_the_effective_date()
    {
        var lastUpdated = new DateTime(2026, 8, 1);
        var bill = new BillingBillFact(
            51, 13, "B-51", 1m, MasterPlanBillStatusIds.Approved, "אושר", SubmitDate: null, lastUpdated);

        Assert.Equal(lastUpdated, BillingBillTimeline.EffectiveDate(bill));
        Assert.True(BillingBillTimeline.UsesLastUpdatedFallback(bill));
        Assert.Equal(1, BillingBillTimeline.CountRealBillsUsingLastUpdatedFallback([bill]));

        var rows = BillingCandidateEngine.BuildRows(
            [Project(13)],
            [bill],
            [Hour(13, lastUpdated, 4m), Hour(13, lastUpdated.AddDays(1), 6m)],
            AsOf);

        var row = Assert.Single(rows);
        Assert.Equal(lastUpdated.Date, row.LastBillDate);
        Assert.Equal(6m, row.HoursSinceLastBill);
    }

    private static BillingProjectFact Project(int id, string? number = null, decimal? feeSum = null) =>
        new(id, number ?? id.ToString(), "Project " + id, "Customer", 1, "פעיל", true, feeSum);

    private static BillingBillFact Bill(
        int id,
        int projectId,
        int statusId,
        DateTime? submitDate,
        decimal? sum = null,
        DateTime? lastUpdated = null) =>
        new(
            id,
            projectId,
            "B-" + id,
            sum,
            statusId,
            statusId switch
            {
                MasterPlanBillStatusIds.InCreation => "ביצירה",
                MasterPlanBillStatusIds.Submitted => "הוגש",
                MasterPlanBillStatusIds.Approved => "אושר",
                MasterPlanBillStatusIds.Closed => "סגור",
                _ => "?",
            },
            submitDate,
            LastUpdated: lastUpdated);

    private static BillingHourFact Hour(int projectId, DateTime date, decimal hours) =>
        new(projectId, date, hours);
}
