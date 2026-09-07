namespace SiNet.Application.Billing;

/// <summary>
/// Builds <see cref="BillingCandidateRow"/> values from Replica facts.
/// Snapshot enrichment is applied afterwards by <see cref="BillingSnapshotEnrichmentApplier"/>.
/// </summary>
public static class BillingCandidateEngine
{
    public static IReadOnlyList<BillingCandidateRow> BuildRows(
        IReadOnlyList<BillingProjectFact> projects,
        IReadOnlyList<BillingBillFact> bills,
        IReadOnlyList<BillingHourFact> hours,
        DateTime asOfDate)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(bills);
        ArgumentNullException.ThrowIfNull(hours);

        var asOf = asOfDate.Date;
        var billsByProject = bills
            .GroupBy(b => b.ProjectId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<BillingBillFact>)g.ToList());
        var hoursByProject = hours
            .GroupBy(h => h.ProjectId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<BillingHourFact>)g.ToList());

        var rows = new List<BillingCandidateRow>(projects.Count);
        foreach (var project in projects)
        {
            billsByProject.TryGetValue(project.ProjectId, out var projectBills);
            hoursByProject.TryGetValue(project.ProjectId, out var projectHours);
            projectBills ??= Array.Empty<BillingBillFact>();
            projectHours ??= Array.Empty<BillingHourFact>();
            rows.Add(BuildRow(project, projectBills, projectHours, asOf));
        }

        return Sort(rows);
    }

    private static BillingCandidateRow BuildRow(
        BillingProjectFact project,
        IReadOnlyList<BillingBillFact> bills,
        IReadOnlyList<BillingHourFact> hours,
        DateTime asOf)
    {
        var lastReal = PickLast(bills, realOnly: true);
        var latest = PickLast(bills, realOnly: false);
        var lastRealDate = lastReal is null ? null : BillingBillTimeline.EffectiveDate(lastReal);

        var hours30 = SumHours(hours, asOf, days: 30);
        var hours60 = SumHours(hours, asOf, days: 60);
        var hours90 = SumHours(hours, asOf, days: 90);
        var workDays30 = CountWorkDays(hours, asOf, days: 30);
        var lastWorkDate = hours
            .Where(h => h.Hours > 0m)
            .Select(h => h.ReportDate.Date)
            .DefaultIfEmpty()
            .Max();
        DateTime? lastWork = hours.Any(h => h.Hours > 0m) ? lastWorkDate : null;

        var hoursSinceLastBill = hours
            .Where(h => lastRealDate is null || h.ReportDate.Date > lastRealDate.Value.Date)
            .Sum(h => h.Hours);

        var billsInCreation = bills.Count(b => b.StatusId == MasterPlanBillStatusIds.InCreation);
        var billsSubmitted = bills.Count(b => b.StatusId == MasterPlanBillStatusIds.Submitted);
        var billsApproved = bills.Count(b => b.StatusId == MasterPlanBillStatusIds.Approved);
        var nonClosed = bills.Count(b => b.StatusId != MasterPlanBillStatusIds.Closed);

        var hasRealLastBill = lastReal is not null;
        var daysSinceLastBill = lastRealDate is DateTime lastDate
            ? (int?)(asOf - lastDate.Date).Days
            : null;

        var state = BillingCandidateStateResolver.Resolve(
            billsInCreation,
            hoursSinceLastBill,
            hours30,
            hasRealLastBill);

        var reason = BillingCandidateReasonFormatter.Format(
            state,
            new BillingCandidateMetrics(hours30, hoursSinceLastBill, workDays30, daysSinceLastBill));

        return new BillingCandidateRow(
            ProjectId: project.ProjectId,
            ProjectNumber: project.ProjectNumber,
            ProjectName: project.ProjectName,
            CustomerName: project.CustomerName,
            ProjectStatus: project.ProjectStatus,
            CurrentFeeSum: project.CurrentFeeSum,
            LastWorkDate: lastWork,
            Hours30: hours30,
            Hours60: hours60,
            Hours90: hours90,
            HoursSinceLastBill: hoursSinceLastBill,
            WorkDays30: workDays30,
            LastBillDate: lastRealDate?.Date,
            DaysSinceLastBill: daysSinceLastBill,
            LatestBillId: latest?.BillId,
            LatestBillNumber: latest?.BillNumber,
            LatestBillStatusId: latest?.StatusId,
            LatestBillStatus: latest?.Status,
            LatestBillSum: latest?.Sum,
            BillsInCreation: billsInCreation,
            BillsSubmitted: billsSubmitted,
            BillsApproved: billsApproved,
            NonClosedBills: nonClosed,
            SnapshotBalance: null,
            SnapshotOpenBillSum: null,
            SnapshotApprovedBillSum: null,
            SnapshotBilledPercent: null,
            SnapshotDate: null,
            SnapshotFeeTypes: null,
            CandidateState: state,
            CandidateReason: reason);
    }

    private static BillingBillFact? PickLast(IReadOnlyList<BillingBillFact> bills, bool realOnly)
    {
        IEnumerable<BillingBillFact> source = bills;
        if (realOnly)
            source = source.Where(b => MasterPlanBillStatusIds.ResetsWorkPeriod(b.StatusId));

        return source
            .OrderByDescending(b => BillingBillTimeline.EffectiveDate(b) ?? DateTime.MinValue)
            .ThenByDescending(b => b.BillId)
            .FirstOrDefault();
    }

    private static decimal SumHours(IReadOnlyList<BillingHourFact> hours, DateTime asOf, int days) =>
        hours.Where(h => InRollingWindow(h.ReportDate, asOf, days)).Sum(h => h.Hours);

    private static int CountWorkDays(IReadOnlyList<BillingHourFact> hours, DateTime asOf, int days) =>
        hours
            .Where(h => h.Hours > 0m && InRollingWindow(h.ReportDate, asOf, days))
            .Select(h => h.ReportDate.Date)
            .Distinct()
            .Count();

    private static bool InRollingWindow(DateTime reportDate, DateTime asOf, int days)
    {
        var start = asOf.Date.AddDays(-days);
        var endExclusive = asOf.Date.AddDays(1);
        var date = reportDate.Date;
        return date >= start && date < endExclusive;
    }

    private static IReadOnlyList<BillingCandidateRow> Sort(List<BillingCandidateRow> rows) =>
        rows
            .OrderBy(r => StateSortOrder(r.CandidateState))
            .ThenByDescending(r => r.HoursSinceLastBill)
            .ThenByDescending(r => r.WorkDays30)
            .ThenByDescending(r => r.Hours30)
            .ThenByDescending(r => r.DaysSinceLastBill ?? int.MinValue)
            .ThenBy(r => r.ProjectNumber)
            .ToList();

    private static int StateSortOrder(BillingCandidateState state) => state switch
    {
        BillingCandidateState.ReviewNow => 0,
        BillingCandidateState.AccumulatedWork => 1,
        BillingCandidateState.BillInPreparation => 2,
        BillingCandidateState.CoveredByLatestBill => 3,
        BillingCandidateState.NotUrgent => 4,
        _ => 5
    };
}
