namespace SiNet.Application.Billing;

/// <summary>
/// Explainable billing-review classification. Not a score.
/// See <c>docs/BILLING_CONTROL_CENTER_V1_IMPLEMENTATION_PLAN.md</c> §4.
/// </summary>
public enum BillingCandidateState
{
    ReviewNow = 0,
    AccumulatedWork = 1,
    CoveredByLatestBill = 2,
    BillInPreparation = 3,
    NotUrgent = 4
}

/// <summary>Hebrew labels for <see cref="BillingCandidateState"/>.</summary>
public static class BillingCandidateStateDisplay
{
    public static string ToHebrew(BillingCandidateState state) => state switch
    {
        BillingCandidateState.ReviewNow => "לבדוק עכשיו",
        BillingCandidateState.AccumulatedWork => "עבודה שהצטברה",
        BillingCandidateState.CoveredByLatestBill => "אין עבודה חדשה מאז החשבון",
        BillingCandidateState.BillInPreparation => "כבר יש חשבון ביצירה",
        BillingCandidateState.NotUrgent => "לא דחוף",
        _ => state.ToString()
    };
}
