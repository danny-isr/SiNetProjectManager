namespace SiNet.Application.Billing;

/// <summary>Write a current local decision. Observed* is the Replica evidence at click time.</summary>
public sealed record BillingReviewDecisionWriteRequest(
    int ProjectId,
    BillingLocalDecisionType DecisionType,
    string? Reason,
    DateTime? ReviewAgainDate,
    int? ObservedLatestBillId,
    int? ObservedLatestBillStatusId,
    DateTime? ObservedLastBillDate,
    decimal? ObservedHoursSinceLastBill,
    DateTime? ObservedLastWorkDate);
