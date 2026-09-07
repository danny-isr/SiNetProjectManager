namespace SiNet.Application.Billing;

/// <summary>Full billing dashboard payload: summary, rows, freshness, warnings, Replica diagnostics.</summary>
public sealed record BillingDashboardResult(
    BillingDashboardSummary Summary,
    IReadOnlyList<BillingCandidateRow> Candidates,
    BillingSourceFreshness Freshness,
    IReadOnlyList<BillingDataQualityWarning> Warnings,
    ReplicaConnectionDiagnostics Diagnostics,
    BillingReplicaFreshnessStatus FreshnessStatus,
    bool CandidatesBlocked);
