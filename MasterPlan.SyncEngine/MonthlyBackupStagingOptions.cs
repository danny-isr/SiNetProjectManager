using Microsoft.Extensions.Configuration;

namespace MasterPlan.SyncEngine;

/// <summary>
/// Client↔SQL Server path mapping for monthly <c>.bak</c> staging (DEV-020).
/// Production SyncEngine runs as a Scheduled Task on the SQL host and must use a
/// server-visible path (not a workstation mapped drive such as <c>N:\</c>).
/// When SyncEngine and SQL Server share the same machine, <see cref="ClientStagingPath"/>
/// and <see cref="ServerStagingPath"/> may be identical.
/// </summary>
public sealed record MonthlyBackupStagingOptions
{
    public const string ConfigurationSectionName = "MasterPlanMonthlyBackup";
    public const int DefaultMaxRetainedBackups = 10;

    /// <summary>
    /// Production staging root visible to the SyncEngine process
    /// (<c>D:\SharedFolder\ProjectsData\MasterPlanBakup</c>).
    /// </summary>
    public const string DefaultProductionStagingPath =
        @"D:\SharedFolder\ProjectsData\MasterPlanBakup";

    /// <summary>Path as seen by the SyncEngine process (server-local on PROD).</summary>
    public string ClientStagingPath { get; init; } = DefaultProductionStagingPath;

    /// <summary>Same folder as seen by SQL Server.</summary>
    public string ServerStagingPath { get; init; } = DefaultProductionStagingPath;

    /// <summary>Keep at most this many <c>.bak</c> files in staging; delete oldest beyond the limit.</summary>
    public int MaxRetainedBackups { get; init; } = DefaultMaxRetainedBackups;

    public static MonthlyBackupStagingOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(ConfigurationSectionName);
        var maxRetained = DefaultMaxRetainedBackups;
        if (int.TryParse(section["MaxRetainedBackups"], out var parsed) && parsed > 0)
        {
            maxRetained = parsed;
        }

        return new MonthlyBackupStagingOptions
        {
            ClientStagingPath = string.IsNullOrWhiteSpace(section["ClientStagingPath"])
                ? DefaultProductionStagingPath
                : section["ClientStagingPath"]!.Trim(),
            ServerStagingPath = string.IsNullOrWhiteSpace(section["ServerStagingPath"])
                ? DefaultProductionStagingPath
                : section["ServerStagingPath"]!.Trim(),
            MaxRetainedBackups = maxRetained
        };
    }
}
