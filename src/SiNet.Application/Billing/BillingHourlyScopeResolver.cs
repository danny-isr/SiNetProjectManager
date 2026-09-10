namespace SiNet.Application.Billing;

/// <summary>
/// Resolves a manager date-range scope to concrete hour-report rows.
/// Does not claim the hours were billed in MasterPlan.
/// </summary>
public static class BillingHourlyScopeResolver
{
    public static IReadOnlyList<BillingPreparationHourReportSnapshot> Resolve(
        IEnumerable<BillingHourReportFact> reports,
        int masterPlanSubContractId,
        DateTime fromDateInclusive,
        DateTime toDateInclusive)
    {
        ArgumentNullException.ThrowIfNull(reports);

        var from = fromDateInclusive.Date;
        var to = toDateInclusive.Date;
        if (to < from)
            throw new ArgumentException("To date must be on or after from date.");

        return reports
            .Where(r => r.SubContractId == masterPlanSubContractId
                        && r.Date.Date >= from
                        && r.Date.Date <= to)
            .Select(r => new BillingPreparationHourReportSnapshot(
                r.HoursReportId,
                r.Date.Date,
                r.EmployeeId,
                r.EmployeeName,
                r.Hours,
                r.SubContractId,
                r.SubContractStepId,
                r.Description))
            .OrderBy(r => r.Date)
            .ThenBy(r => r.HoursReportId)
            .ToList();
    }
}
