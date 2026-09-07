namespace SiNet.Application.Billing;

/// <summary>Persisted SiNet billing review decision, including observed Replica evidence.</summary>
public sealed record BillingReviewDecisionRecord(
    int ProjectId,
    BillingLocalDecisionType DecisionType,
    string? Reason,
    DateTime? ReviewAgainDate,
    DateTime CreatedAtUtc,
    int CreatedByUserId,
    string? CreatedByLogin,
    DateTime? UpdatedAtUtc,
    int? UpdatedByUserId,
    string? UpdatedByLogin,
    DateTime? ClearedAtUtc,
    int? ClearedByUserId,
    string? ClearedByLogin,
    int? ObservedLatestBillId,
    int? ObservedLatestBillStatusId,
    DateTime? ObservedLastBillDate,
    decimal? ObservedHoursSinceLastBill,
    DateTime? ObservedLastWorkDate);
