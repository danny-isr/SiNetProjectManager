namespace SiNet.Application.Billing;

/// <summary>
/// Replica sync age thresholds. Defaults: warn after 36 hours, block current dashboards after 72 hours.
/// </summary>
public sealed record BillingReplicaFreshnessOptions(TimeSpan WarningAfter, TimeSpan FatalAfter)
{
    public static BillingReplicaFreshnessOptions Default { get; } = new(
        TimeSpan.FromHours(36),
        TimeSpan.FromHours(72));
}

/// <summary>Required Replica objects for a current billing dashboard.</summary>
public static class BillingReplicaRequirements
{
    public static readonly string[] RequiredTables =
    [
        "MP_Projects",
        "MP_Bills",
        "MP_ProjectHoursExtended",
        "Sync_State"
    ];

    public static readonly string[] RequiredSyncStateEntities =
    [
        "Projects",
        "Bills",
        "Intakes",
        "ProjectHours",
        "ProjectHoursExtended"
    ];

    /// <summary>
    /// SyncEngine stamps this Replica <c>Sync_State</c> entity with the monthly .bak
    /// <c>BackupFinishDate</c>. Informational for B2 snapshot dating — not a freshness-gate entity.
    /// </summary>
    public const string MonthlyRestoreSyncStateEntity = "MonthlyRestore";
}
