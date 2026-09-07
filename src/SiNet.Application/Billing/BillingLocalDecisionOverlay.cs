namespace SiNet.Application.Billing;

/// <summary>
/// Active local decision attached to a candidate row. Never rewrites
/// <see cref="BillingCandidateRow.CandidateState"/> or Replica numbers.
/// </summary>
public sealed record BillingLocalDecisionOverlay(
    BillingLocalDecisionType DecisionType,
    string? Reason,
    DateTime? ReviewAgainDate,
    DateTime CreatedAtUtc,
    int CreatedByUserId,
    string? CreatedByLogin,
    DateTime? UpdatedAtUtc,
    int? UpdatedByUserId,
    string? UpdatedByLogin,
    BillingLocalDecisionEffect Effect);
