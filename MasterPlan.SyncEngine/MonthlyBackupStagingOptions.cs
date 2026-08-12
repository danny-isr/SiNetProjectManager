using Microsoft.Extensions.Configuration;

namespace MasterPlan.SyncEngine;

/// <summary>
/// Client↔SQL Server path mapping for monthly <c>.bak</c> staging (DEV-020).
/// SQL <c>RESTORE</c> runs on the server host and cannot see workstation drive letters
/// such as <c>N:\</c>; the engine <b>moves</b> the chosen file into the client staging
/// folder and passes the mapped server path to SQL.
/// </summary>
public sealed record MonthlyBackupStagingOptions
{
    public const string ConfigurationSectionName = "MasterPlanMonthlyBackup";
    public const int DefaultMaxRetainedBackups = 10;

    /// <summary>Path as seen by the SyncEngine process (e.g. <c>N:\MasterPlanBakup</c>).</summary>
    public string ClientStagingPath { get; init; } = @"N:\MasterPlanBakup";

    /// <summary>Same folder as seen by SQL Server (e.g. <c>D:\SharedFolder\ProjectsData\MasterPlanBakup</c>).</summary>
    public string ServerStagingPath { get; init; } = @"D:\SharedFolder\ProjectsData\MasterPlanBakup";

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
                ? @"N:\MasterPlanBakup"
                : section["ClientStagingPath"]!.Trim(),
            ServerStagingPath = string.IsNullOrWhiteSpace(section["ServerStagingPath"])
                ? @"D:\SharedFolder\ProjectsData\MasterPlanBakup"
                : section["ServerStagingPath"]!.Trim(),
            MaxRetainedBackups = maxRetained
        };
    }
}
