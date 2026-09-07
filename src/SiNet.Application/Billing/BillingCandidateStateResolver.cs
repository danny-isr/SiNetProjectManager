namespace SiNet.Application.Billing;

/// <summary>
/// Deterministic candidate classification. Rule order is the product contract
/// (<c>docs/BILLING_CONTROL_CENTER_V1_IMPLEMENTATION_PLAN.md</c> §4.3).
/// </summary>
public static class BillingCandidateStateResolver
{
    public static BillingCandidateState Resolve(
        int billsInCreation,
        decimal hoursSinceLastBill,
        decimal hours30,
        bool hasRealLastBill)
    {
        if (billsInCreation > 0)
            return BillingCandidateState.BillInPreparation;

        if (hoursSinceLastBill <= 0m && hasRealLastBill)
            return BillingCandidateState.CoveredByLatestBill;

        if (hours30 > 0m)
            return BillingCandidateState.ReviewNow;

        if (hoursSinceLastBill > 0m)
            return BillingCandidateState.AccumulatedWork;

        return BillingCandidateState.NotUrgent;
    }
}
