using Microsoft.Extensions.Configuration;

namespace MasterPlan.SyncEngine;

/// <summary>
/// Safety-gated orphan purge after a full hours reconcile (DEV-019).
/// See docs/DEV_PLAN_MASTERPLAN_ORPHAN_PURGE.md.
/// </summary>
public sealed record OrphanPurgeOptions
{
    public const int DefaultMinAbsoluteFetch = 1000;
    public const double DefaultMinFetchFractionOfReplica = 0.5;
    public const double DefaultMaxPurgeFraction = 0.10;
    public const int DefaultMaxAbsolutePurge = 500;
    public const int DefaultAgeWindowMonths = 24;
    public const int DefaultDeleteBatchSize = 200;

    /// <summary>Master switch. Real DELETE also requires CLI <c>--purge-orphans</c>.</summary>
    public bool Enabled { get; init; }

    /// <summary>This run: compute gates + CSV; never DELETE.</summary>
    public bool DryRun { get; init; }

    /// <summary>This run: allow DELETE when <see cref="Enabled"/> and all gates pass.</summary>
    public bool PurgeRequested { get; init; }

    /// <summary>Bypass the ReportDate age window (G5) only.</summary>
    public bool IncludeLegacy { get; init; }

    public int MinAbsoluteFetch { get; init; } = DefaultMinAbsoluteFetch;
    public double MinFetchFractionOfReplica { get; init; } = DefaultMinFetchFractionOfReplica;
    public double MaxPurgeFraction { get; init; } = DefaultMaxPurgeFraction;
    public int MaxAbsolutePurge { get; init; } = DefaultMaxAbsolutePurge;
    public int AgeWindowMonths { get; init; } = DefaultAgeWindowMonths;
    public int DeleteBatchSize { get; init; } = DefaultDeleteBatchSize;

    /// <summary>Real DELETE path (not dry-run).</summary>
    public bool ShouldDelete
        => Enabled && PurgeRequested && !DryRun;

    /// <summary>Write would-delete CSV without DELETE.</summary>
    public bool ShouldWriteDryRunArtifact
        => DryRun;

    public static OrphanPurgeOptions FromConfiguration(
        IConfiguration configuration,
        bool purgeRequested = false,
        bool dryRun = false,
        bool includeLegacy = false)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection("MasterPlanApi:OrphanPurge");
        return new OrphanPurgeOptions
        {
            Enabled = ReadBool(section["Enabled"], false),
            DryRun = dryRun,
            PurgeRequested = purgeRequested,
            IncludeLegacy = includeLegacy,
            MinAbsoluteFetch = ReadPositiveInt(section["MinAbsoluteFetch"], DefaultMinAbsoluteFetch),
            MinFetchFractionOfReplica = ReadPositiveDouble(section["MinFetchFractionOfReplica"], DefaultMinFetchFractionOfReplica),
            MaxPurgeFraction = ReadPositiveDouble(section["MaxPurgeFraction"], DefaultMaxPurgeFraction),
            MaxAbsolutePurge = ReadPositiveInt(section["MaxAbsolutePurge"], DefaultMaxAbsolutePurge),
            AgeWindowMonths = ReadPositiveInt(section["AgeWindowMonths"], DefaultAgeWindowMonths),
            DeleteBatchSize = ReadPositiveInt(section["DeleteBatchSize"], DefaultDeleteBatchSize),
        };
    }

    private static bool ReadBool(string? raw, bool fallback)
        => bool.TryParse(raw, out var parsed) ? parsed : fallback;

    private static int ReadPositiveInt(string? raw, int fallback)
        => int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : fallback;

    private static double ReadPositiveDouble(string? raw, double fallback)
        => double.TryParse(raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : fallback;
}
