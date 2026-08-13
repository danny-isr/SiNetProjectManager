using Microsoft.Extensions.Configuration;

namespace MasterPlan.SyncEngine;

/// <summary>
/// Orphan purge after a successful full hours reconcile (DEV-025).
/// Real DELETE is part of reconcile by default; opt out with <c>--skip-orphan-purge</c>.
/// </summary>
public sealed record OrphanPurgeOptions
{
    public const int DefaultMinAbsoluteFetch = 1000;
    public const double DefaultMinFetchFractionOfReplica = 0.5;
    public const double DefaultMaxPurgeFraction = 0.10;
    public const int DefaultMaxAbsolutePurge = 500;
    public const int DefaultAgeWindowMonths = 24;
    public const int DefaultDeleteBatchSize = 200;
    public const int DefaultArchiveRetentionDays = OrphanArchiveWriter.DefaultRetentionDays;
    public const string ArchiveSubfolderName = "OrphanArchive";

    /// <summary>Master switch. Default true under DEV-025.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>This run: compute gates + artifacts; never DELETE.</summary>
    public bool DryRun { get; init; }

    /// <summary>This run: allow DELETE when <see cref="Enabled"/> and not skipped/dry-run.</summary>
    public bool PurgeRequested { get; init; } = true;

    /// <summary>Unused under DEV-025 (G5 dropped). Kept so old CLI still parses.</summary>
    public bool IncludeLegacy { get; init; }

    public int MinAbsoluteFetch { get; init; } = DefaultMinAbsoluteFetch;
    public double MinFetchFractionOfReplica { get; init; } = DefaultMinFetchFractionOfReplica;
    public double MaxPurgeFraction { get; init; } = DefaultMaxPurgeFraction;
    public int MaxAbsolutePurge { get; init; } = DefaultMaxAbsolutePurge;
    public int AgeWindowMonths { get; init; } = DefaultAgeWindowMonths;
    public int DeleteBatchSize { get; init; } = DefaultDeleteBatchSize;
    public int ArchiveRetentionDays { get; init; } = DefaultArchiveRetentionDays;

    /// <summary>JSON archive folder (DEV-020 staging root + <see cref="ArchiveSubfolderName"/>).</summary>
    public string ArchiveDirectory { get; init; } = "";

    /// <summary>Real DELETE path (not dry-run, not skipped).</summary>
    public bool ShouldDelete
        => Enabled && PurgeRequested && !DryRun;

    /// <summary>Write would-delete CSV without DELETE.</summary>
    public bool ShouldWriteDryRunArtifact
        => DryRun;

    public static OrphanPurgeOptions FromConfiguration(
        IConfiguration configuration,
        bool purgeRequested = true,
        bool dryRun = false,
        bool includeLegacy = false)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection("MasterPlanApi:OrphanPurge");
        var archiveOverride = section["ArchiveDirectory"];
        var archiveDirectory = string.IsNullOrWhiteSpace(archiveOverride)
            ? Path.Combine(
                MonthlyBackupStagingOptions.FromConfiguration(configuration).ClientStagingPath,
                ArchiveSubfolderName)
            : archiveOverride.Trim();

        return new OrphanPurgeOptions
        {
            Enabled = ReadBool(section["Enabled"], true),
            DryRun = dryRun,
            PurgeRequested = purgeRequested,
            IncludeLegacy = includeLegacy,
            MinAbsoluteFetch = ReadPositiveInt(section["MinAbsoluteFetch"], DefaultMinAbsoluteFetch),
            MinFetchFractionOfReplica = ReadPositiveDouble(section["MinFetchFractionOfReplica"], DefaultMinFetchFractionOfReplica),
            MaxPurgeFraction = ReadPositiveDouble(section["MaxPurgeFraction"], DefaultMaxPurgeFraction),
            MaxAbsolutePurge = ReadPositiveInt(section["MaxAbsolutePurge"], DefaultMaxAbsolutePurge),
            AgeWindowMonths = ReadPositiveInt(section["AgeWindowMonths"], DefaultAgeWindowMonths),
            DeleteBatchSize = ReadPositiveInt(section["DeleteBatchSize"], DefaultDeleteBatchSize),
            ArchiveRetentionDays = ReadPositiveInt(section["ArchiveRetentionDays"], DefaultArchiveRetentionDays),
            ArchiveDirectory = archiveDirectory
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
