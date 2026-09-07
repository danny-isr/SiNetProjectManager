namespace SiNet.Application.Billing;

/// <summary>
/// Effective bill date: <c>SubmitDate</c> is authoritative; <c>LastUpdated</c> is fallback only.
/// </summary>
public static class BillingBillTimeline
{
    public static DateTime? EffectiveDate(BillingBillFact bill)
    {
        ArgumentNullException.ThrowIfNull(bill);
        return bill.SubmitDate ?? bill.LastUpdated;
    }

    public static bool UsesLastUpdatedFallback(BillingBillFact bill)
    {
        ArgumentNullException.ThrowIfNull(bill);
        return MasterPlanBillStatusIds.ResetsWorkPeriod(bill.StatusId)
            && bill.SubmitDate is null
            && bill.LastUpdated is not null;
    }

    public static int CountRealBillsUsingLastUpdatedFallback(IEnumerable<BillingBillFact> bills)
    {
        ArgumentNullException.ThrowIfNull(bills);
        return bills.Count(UsesLastUpdatedFallback);
    }
}
