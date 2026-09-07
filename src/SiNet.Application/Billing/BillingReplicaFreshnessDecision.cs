namespace SiNet.Application.Billing;

/// <summary>Outcome of the Replica freshness gate.</summary>
public sealed record BillingReplicaFreshnessDecision(
    BillingReplicaFreshnessStatus Status,
    bool CandidatesBlocked,
    bool IsCurrentDashboardRequest,
    TimeSpan? OldestSyncAge,
    string Code,
    string Message,
    IReadOnlyList<string> MissingRequiredTables,
    IReadOnlyList<string> MissingSyncStateEntities);
