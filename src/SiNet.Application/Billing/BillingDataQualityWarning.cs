namespace SiNet.Application.Billing;

/// <summary>Non-fatal data-quality flag (for example hours parity drift).</summary>
public sealed record BillingDataQualityWarning(
    string Code,
    string Message,
    int? Count = null);

/// <summary>Stable warning codes for the billing dashboard.</summary>
public static class BillingDataQualityWarningCodes
{
    public const string HoursParityOnlyInBasic = "HoursParityOnlyInBasic";
    public const string SyncStateMissing = "SyncStateMissing";
    public const string RealBillSubmitDateFallback = "RealBillSubmitDateFallback";
    public const string ReplicaFreshnessWarning = "ReplicaFreshnessWarning";
    public const string ReplicaFreshnessFatal = "ReplicaFreshnessFatal";
    public const string ReplicaSyncStateMissing = "ReplicaSyncStateMissing";
    public const string ReplicaRequiredTableMissing = "ReplicaRequiredTableMissing";
    public const string SnapshotEnrichmentUnavailable = "SnapshotEnrichmentUnavailable";
    public const string SnapshotRequiredTableMissing = "SnapshotRequiredTableMissing";
    public const string SnapshotDateUnknown = "SnapshotDateUnknown";
}
