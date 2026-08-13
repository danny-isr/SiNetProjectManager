using Microsoft.Extensions.Configuration;

namespace MasterPlan.SyncEngine;

/// <summary>
/// Tuning for the hour-reporting entities (ProjectHours, ProjectHoursExtended, TimeHourReports).
/// Hour reports can be created and edited for past dates, so a plain forward-only watermark drops
/// rows written behind it. See docs/MASTERPLAN_SYNC_WATERMARKS.md.
/// </summary>
public sealed record HoursSyncOptions
{
    private const int DefaultLookbackDays = 14;
    private const int DefaultReconcileIntervalDays = 7;

    /// <summary>Days subtracted from the stored watermark when building the API request.</summary>
    public int LookbackDays { get; init; } = DefaultLookbackDays;

    /// <summary>Minimum days between two unfiltered reconciliation passes.</summary>
    public int ReconcileIntervalDays { get; init; } = DefaultReconcileIntervalDays;

    /// <summary>Run an unfiltered reconciliation pass regardless of when the last one ran.</summary>
    public bool ForceReconcile { get; init; }

    /// <summary>Suppress reconciliation for this execution.</summary>
    public bool SkipReconcile { get; init; }

    /// <summary>DEV-025 orphan purge knobs for this run (default: DELETE after successful full reconcile).</summary>
    public OrphanPurgeOptions OrphanPurge { get; init; } = new();

    public static HoursSyncOptions FromConfiguration(
        IConfiguration configuration,
        bool forceReconcile = false,
        bool skipReconcile = false,
        bool skipOrphanPurge = false,
        bool purgeOrphansDryRun = false,
        bool purgeOrphansIncludeLegacy = false)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection("MasterPlanApi");
        return new HoursSyncOptions
        {
            LookbackDays = ReadPositiveInt(section["HoursLookbackDays"], DefaultLookbackDays),
            ReconcileIntervalDays = ReadPositiveInt(section["ReconcileIntervalDays"], DefaultReconcileIntervalDays),
            ForceReconcile = forceReconcile,
            SkipReconcile = skipReconcile,
            OrphanPurge = OrphanPurgeOptions.FromConfiguration(
                configuration,
                purgeRequested: !skipOrphanPurge,
                dryRun: purgeOrphansDryRun,
                includeLegacy: purgeOrphansIncludeLegacy)
        };
    }

    private static int ReadPositiveInt(string? rawValue, int fallback) =>
        int.TryParse(rawValue, out var parsed) && parsed > 0 ? parsed : fallback;
}
