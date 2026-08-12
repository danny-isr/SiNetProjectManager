using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MasterPlan.SyncEngine;

/// <summary>
/// Runs the existing daily MasterPlan Web API sync. Used by <c>--daily</c> and by
/// monthly Step 4 (DEV-023 forced reconcile after a successful bak restore).
/// </summary>
public static class DailyApiSyncRunner
{
    /// <summary>
    /// Full unfiltered hours pass from the internet (same as <c>--daily --reconcile</c>).
    /// Does not purge orphans. Skips the 12-endpoint validation GETs.
    /// </summary>
    public static async Task RunForcedReconcileAsync(
        IConfiguration configuration,
        string replicaConnectionString,
        string? siDataConnectionString,
        ILogger<MasterPlanApiClient> apiClientLogger,
        ILogger<ApiDailySyncService> apiSyncLogger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(replicaConnectionString);
        ArgumentNullException.ThrowIfNull(apiClientLogger);
        ArgumentNullException.ThrowIfNull(apiSyncLogger);

        var hoursOptions = HoursSyncOptions.FromConfiguration(configuration, forceReconcile: true);
        using var apiClient = new MasterPlanApiClient(configuration, apiClientLogger, captureService: null);
        var apiSyncService = new ApiDailySyncService(
            apiClient,
            replicaConnectionString,
            apiSyncLogger,
            captureService: null,
            siDataConnectionString,
            hoursOptions);

        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  STEP 4 – API FORCE RECONCILE (MasterPlan Web)                   ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
        Console.WriteLine("    Pulling unfiltered hours from the API and merging into Replica_DB.");
        Console.WriteLine(
            $"[CONFIG] Hours sync: lookback {hoursOptions.LookbackDays}d, reconcile FORCED this run");

        var result = await apiSyncService.RunDailySyncAsync().ConfigureAwait(false);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                result.ErrorMessage ?? "סנכרון API אחרי שחזור חודשי נכשל.");
        }
    }
}
