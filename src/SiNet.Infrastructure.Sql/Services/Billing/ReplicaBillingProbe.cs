using SiNet.Application.Billing;

namespace SiNet.Infrastructure.Sql.Services.Billing;

/// <summary>Lightweight Replica probe used by the freshness gate before loading billing facts.</summary>
public sealed record ReplicaBillingProbe(
    ReplicaConnectionDiagnostics Diagnostics,
    IReadOnlyList<string> MissingRequiredTables,
    bool SyncStateTablePresent,
    IReadOnlyDictionary<string, DateTime?> SyncTimesByEntity);
