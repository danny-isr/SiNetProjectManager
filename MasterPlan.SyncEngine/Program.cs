using Dapper;
using MasterPlan.SyncEngine;
using MasterPlan.SyncEngine.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using SiNetSQL.Services.Logging;

// ═══════════════════════════════════════════════════════════════════════════════════════════
// Register Dapper type handlers for SQL Server compatibility
// - DateTime handlers: Convert out-of-range dates (like DateTime.MinValue) to NULL
// - TimeSpan handlers: Properly map to SQL Server TIME type (fixes SqlDateTime overflow)
// ═══════════════════════════════════════════════════════════════════════════════════════════
SqlMapper.AddTypeHandler(new SqlDateTimeHandler());
SqlMapper.AddTypeHandler(new SqlNullableDateTimeHandler());
SqlMapper.AddTypeHandler(new SqlTimeSpanHandler());
SqlMapper.AddTypeHandler(new SqlNullableTimeSpanHandler());

// Build configuration
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();

// ═══════════════════════════════════════════════════════════════════════════════════════════
// Centralized logging — Serilog wired via Microsoft.Extensions.Logging.
// Configuration (central path, levels, retention) is read from the SystemSettings
// table in SQL — single source of truth shared with the WPF client and AccService.
// Falls back to compile-time defaults when the DB is unreachable.
// ═══════════════════════════════════════════════════════════════════════════════════════════
var loggingConnectionString =
    configuration.GetConnectionString("SiData")
    ?? configuration.GetConnectionString("ReplicaDatabase")
    ?? configuration.GetConnectionString("SourceDatabase");

var loggingConfig = CentralLoggingSettings.LoadFromDatabase(
    loggingConnectionString,
    SiNetApp.SyncEngine,
    enableConsole: true);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
    .AddSiNetCentralLogging(loggingConfig)
    .CreateLogger();

using var loggerFactory = new SerilogLoggerFactory(Log.Logger, dispose: false);

var dbSyncLogger = loggerFactory.CreateLogger<DatabaseSyncManager>();
var apiClientLogger = loggerFactory.CreateLogger<MasterPlanApiClient>();
var apiSyncLogger = loggerFactory.CreateLogger<ApiDailySyncService>();
var monthlyServiceLogger = loggerFactory.CreateLogger<MonthlyBackupRestoreService>();
var captureServiceLogger = loggerFactory.CreateLogger<RawCaptureService>();
var dumpServiceLogger = loggerFactory.CreateLogger<ApiDumpService>();

// Flush any buffered log entries on every exit path (including Environment.Exit).
AppDomain.CurrentDomain.ProcessExit += (_, _) => Log.CloseAndFlush();

// Connection strings - configure these in appsettings.json or environment variables
var sourceConnectionString = configuration.GetConnectionString("SourceDatabase")
    ?? throw new InvalidOperationException("SourceDatabase connection string is required");

var replicaConnectionString = configuration.GetConnectionString("ReplicaDatabase")
    ?? throw new InvalidOperationException("ReplicaDatabase connection string is required");

var masterConnectionString = configuration.GetConnectionString("MasterConnection")
    ?? throw new InvalidOperationException("MasterConnection connection string is required");

var siDataConnectionString = configuration.GetConnectionString("SiData");

// Create the sync manager for database-based sync (legacy)
var syncManager = new DatabaseSyncManager(
    sourceConnectionString,
    replicaConnectionString,
    masterConnectionString,
    dbSyncLogger);

// Parse command line arguments (args is provided by top-level statements)
if (args.Contains("--monthly") || args.Contains("-m"))
{
    // Monthly Full Setup from backup using SMO-based ETL service
    var backupPath = args.SkipWhile(a => a != "--backup" && a != "-b").Skip(1).FirstOrDefault()
        ?? @"C:\Backups\MasterPlan.bak";

    // Validate backup file exists
    if (!File.Exists(backupPath))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[ERROR] Backup file not found: {backupPath}");
        Console.ResetColor();
        Environment.Exit(1);
    }

    Console.WriteLine();
    Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║       MASTERPLAN MONTHLY BACKUP/RESTORE - PHASE 1 ETL            ║");
    Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
    Console.WriteLine();
    Console.WriteLine($"[CONFIG] Backup File:    {backupPath}");
    Console.WriteLine($"[CONFIG] Source DB:      Db_Mp_SiEng (restored from backup)");
    Console.WriteLine($"[CONFIG] Target DB:      Replica_DB (ETL destination)");
    Console.WriteLine();

    // Create the new MonthlyBackupRestoreService with SMO support
    var monthlyService = new MonthlyBackupRestoreService(
        sourceConnectionString,
        replicaConnectionString,
        masterConnectionString,
        monthlyServiceLogger);

    try
    {
        var result = await monthlyService.RunMonthlyBackupRestoreAsync(backupPath);

        // Compute step completed and duration
        var stepCompleted = result.Step3Completed ? "All Steps (ETL Complete)" :
                           result.Step2Completed ? "Step 2 (Initialize)" :
                           result.Step1Completed ? "Step 1 (Restore)" : "None";
        var duration = result.EndTime - result.StartTime;

        // Display final summary based on result
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                    PHASE 1 ETL - FINAL STATUS                     ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Final Status:    {(result.Success ? "SUCCESS ✓" : "FAILURE ✗"),-47} ║");
        Console.WriteLine($"║  Step Completed:  {stepCompleted,-47} ║");
        Console.WriteLine($"║  Duration:        {duration.TotalSeconds:F1} seconds{new string(' ', 38 - $"{duration.TotalSeconds:F1}".Length)} ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  ENTITY RECORD COUNTS:                                           ║");

        var totalRecords = 0;
        foreach (var entity in result.EntityRecordCounts)
        {
            Console.WriteLine($"║    {entity.Key,-20} {entity.Value,8} records                      ║");
            totalRecords += entity.Value;
        }
        Console.WriteLine($"║    {"TOTAL",-20} {totalRecords,8} records                      ║");

        Console.WriteLine("╠══════════════════════════════════════════════════════════════════╣");

        if (result.Success)
        {
            Console.WriteLine("║  Step 1 – Restore:    ✓ COMPLETE                                ║");
            Console.WriteLine("║  Step 2 – Initialize: ✓ COMPLETE                                ║");
            Console.WriteLine($"║  Step 3 – Full ETL:   {(result.Step3Completed ? "✓ COMPLETE" : "○ SKIPPED"),-44} ║");
            Console.WriteLine("╠══════════════════════════════════════════════════════════════════╣");
            Console.WriteLine("║  [COMPLETE] Replica_DB ready. Run --daily for incremental sync.  ║");
        }
        else
        {
            var errorMsg = result.ErrorMessage ?? "Unknown error";
            if (errorMsg.Length > 55) errorMsg = errorMsg[..52] + "...";
            Console.WriteLine($"║  [ERROR] {errorMsg,-55} ║");
        }

        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");

        if (!result.Success)
        {
            Environment.Exit(1);
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[FATAL] Monthly ETL failed: {ex.Message}");
        Console.ResetColor();
        monthlyServiceLogger.LogError(ex, "Monthly backup/restore failed");
        Environment.Exit(1);
    }
}
else if (args.Contains("--daily-db") || args.Contains("-dd"))
{
    // Daily Delta Sync via direct database connection (legacy mode)
    Console.WriteLine("Running Daily Delta Sync (Database Mode)...");
    await syncManager.RunDailySyncAsync();
}
else if (args.Contains("--daily") || args.Contains("-d") || args.Contains("--daily-api") || args.Contains("-da"))
{
    // Daily Delta Sync via MasterPlan Web API (recommended)
    Console.WriteLine("Running Daily Delta Sync (API Mode)...");

    // Check if raw capture mode is enabled (default: enabled for debugging)
    var enableCapture = !args.Contains("--no-capture");
    RawCaptureService? captureService = null;

    if (enableCapture)
    {
        captureService = new RawCaptureService(
            replicaConnectionString,
            captureServiceLogger);
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  📁 RAW CAPTURE MODE ENABLED                                     ║");
        Console.WriteLine("║                                                                  ║");
        Console.WriteLine($"║  Output: {captureService.SessionPath,-53} ║");
        Console.WriteLine("║  Files: *.ndjson, *.meta.json, SchemaMismatch.*.json             ║");
        Console.WriteLine("║                                                                  ║");
        Console.WriteLine("║  To disable: --daily --no-capture                                ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
    }

    // Create API client using configuration from appsettings.json
    // Configuration section: MasterPlanApi { BaseUrl, ApiKey, TimeoutSeconds }
    using var apiClient = new MasterPlanApiClient(configuration, apiClientLogger, captureService);
    var apiSyncService = new ApiDailySyncService(apiClient, replicaConnectionString, apiSyncLogger, captureService, siDataConnectionString);

    try
    {
        // Check if --validate-only flag is present (just validate endpoints, don't sync)
        var validateOnly = args.Contains("--validate-only") || args.Contains("-v");
        // --skip-validation: skip the 12-endpoint validation GET calls to preserve rate limit quota
        var skipValidation = args.Contains("--skip-validation") || args.Contains("-sv");

        if (!skipValidation)
        {
            // Validate endpoints first to catch 404 errors early (costs 12 API calls)
            Console.WriteLine();
            Console.WriteLine("Validating API endpoints...");
            var validationResults = await apiClient.ValidateAllEndpointsAsync();

            var failedEndpoints = validationResults.Where(r => !r.Value.Success).ToList();
            if (failedEndpoints.Any())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine();
                Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
                Console.WriteLine("║  ⚠ WARNING: Some endpoints failed validation!                   ║");
                Console.WriteLine("╠══════════════════════════════════════════════════════════════════╣");
                foreach (var (entity, (_, statusCode, message)) in failedEndpoints)
                {
                    Console.WriteLine($"║  {entity,-15}: {statusCode} - {message,-40} ║");
                }
                Console.WriteLine("╠══════════════════════════════════════════════════════════════════╣");
                Console.WriteLine("║  Check master-plan-api-guide.html for correct endpoint paths.   ║");
                Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
                Console.ResetColor();

                if (!args.Contains("--force"))
                {
                    Console.WriteLine();
                    Console.WriteLine("Sync aborted due to endpoint validation failures.");
                    Console.WriteLine("Use --force to continue despite validation failures.");
                    Environment.Exit(1);
                }
            }

            if (validateOnly)
            {
                Console.WriteLine();
                Console.WriteLine("Endpoint validation completed. Skipping sync (--validate-only mode).");
                Environment.Exit(0);
            }
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("[CONFIG] Endpoint validation SKIPPED (--skip-validation). Saving API rate limit quota.");
        }

        await apiSyncService.RunDailySyncAsync();
        Console.WriteLine("Daily API Sync completed successfully!");
    }
    catch (MasterPlanApiException ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"API Error ({ex.StatusCode}): {ex.Message}");
        Console.ResetColor();

        switch (ex.StatusCode)
        {
            case 401:
                Console.WriteLine("Hint: Check that your API key is correct in appsettings.json (MasterPlanApi:ApiKey)");
                break;
            case 404:
                Console.WriteLine("Hint: Verify the API endpoint URL is correct in appsettings.json (MasterPlanApi:BaseUrl)");
                break;
            case 500:
                Console.WriteLine("Hint: Contact MasterPlan technical support");
                break;
        }

        Environment.Exit(1);
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[FATAL] Daily sync failed: {ex.Message}");
        Console.ResetColor();
        apiSyncLogger.LogError(ex, "Daily sync failed");
        Environment.Exit(1);
    }
}
else if (args.Contains("--offline") || args.Contains("-o"))
{
    // Offline Mode - Use dump files instead of live API
    Console.WriteLine();
    Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║  📁 OFFLINE SIMULATION MODE                                       ║");
    Console.WriteLine("║  Using local dump files instead of live API                       ║");
    Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
    Console.WriteLine();

    // Get dump folder path (default to embedded 20260213_010939 folder)
    var dumpFolder = args.SkipWhile(a => a != "--dump-folder" && a != "-df").Skip(1).FirstOrDefault();
    if (string.IsNullOrEmpty(dumpFolder))
    {
        // Default: look for dump folder in project directory
        var possiblePaths = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "20260213_010939"),
            Path.Combine(AppContext.BaseDirectory, "20260213_010939"),
            @"D:\repos2026\SiNetProjectManager\MasterPlan.SyncEngine\20260213_010939"
        };

        dumpFolder = possiblePaths.FirstOrDefault(Directory.Exists);
        if (dumpFolder == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[ERROR] No dump folder found. Specify with --dump-folder <path>");
            Console.WriteLine("Searched:");
            foreach (var p in possiblePaths)
                Console.WriteLine($"  - {p}");
            Console.ResetColor();
            Environment.Exit(1);
        }
    }

    if (!Directory.Exists(dumpFolder))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[ERROR] Dump folder not found: {dumpFolder}");
        Console.ResetColor();
        Environment.Exit(1);
    }

    Console.WriteLine($"[CONFIG] Dump Folder:    {dumpFolder}");
    Console.WriteLine($"[CONFIG] Target DB:      Replica_DB");
    Console.WriteLine();

    // Check for --reset flag to clear watermarks
    var resetWatermarks = args.Contains("--reset") || args.Contains("-r");
    if (resetWatermarks)
    {
        Console.WriteLine("[CONFIG] Reset Mode:     Watermarks will be cleared for full reload");
    }

    // Check for --clear-lock flag to force-clear stale locks
    var clearLock = args.Contains("--clear-lock") || args.Contains("-cl");
    if (clearLock)
    {
        Console.WriteLine("[CONFIG] Clear Lock:     Will force-clear any stale sync locks");
    }

    // Create offline simulator and sync service
    var offlineSimulatorLogger = loggerFactory.CreateLogger<OfflineApiSimulator>();
    var offlineSyncLogger = loggerFactory.CreateLogger<OfflineDailySyncService>();

    using var simulator = new OfflineApiSimulator(dumpFolder, offlineSimulatorLogger);
    var offlineSyncService = new OfflineDailySyncService(simulator, replicaConnectionString, offlineSyncLogger);

    try
    {
        // Validate dump files first
        Console.WriteLine("Validating dump files...");
        var validationResults = await simulator.ValidateAllEndpointsAsync();
        var stats = await simulator.GetDumpStatsAsync();

        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  DUMP FILE VALIDATION                                            ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════╣");
        foreach (var (entity, (success, _, message)) in validationResults)
        {
            var status = success ? "✓" : "✗";
            var count = stats.EntityCounts.GetValueOrDefault(entity, 0);
            Console.WriteLine($"║  {status} {entity,-15}: {count,6} records  {message,-25} ║");
        }
        Console.WriteLine($"╠══════════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  TOTAL: {stats.TotalRecords,8} records                                     ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        var failedFiles = validationResults.Where(r => !r.Value.Success).ToList();
        if (failedFiles.Any() && !args.Contains("--force"))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Some dump files are missing. Use --force to continue anyway.");
            Console.ResetColor();
            Environment.Exit(1);
        }

        // Clear stale lock if requested
        if (clearLock)
        {
            Console.WriteLine("Clearing stale sync lock...");
            await offlineSyncService.ForceClearLockAsync();
        }

        // Run the offline sync
        Console.WriteLine("Starting offline sync...");
        Console.WriteLine();
        var result = await offlineSyncService.RunOfflineSyncAsync(resetWatermarks);

        // Display results
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                  OFFLINE SYNC - FINAL STATUS                     ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Status:    {(result.Success ? "SUCCESS ✓" : "FAILURE ✗"),-53} ║");
        Console.WriteLine($"║  Duration:  {(result.EndTime - result.StartTime).TotalSeconds:F1} seconds{new string(' ', 45 - $"{(result.EndTime - result.StartTime).TotalSeconds:F1}".Length)} ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  ENTITY RESULTS:                                                 ║");

        var totalInserted = 0;
        var totalUpdated = 0;
        foreach (var (entity, entityResult) in result.EntityResults)
        {
            var status = entityResult.ErrorMessage == null ? "✓" : "✗";
            Console.WriteLine($"║  {status} {entity,-15}: {entityResult.RecordsInserted,5} ins, {entityResult.RecordsUpdated,5} upd, {entityResult.RecordsFetched,5} fetch ║");
            totalInserted += entityResult.RecordsInserted;
            totalUpdated += entityResult.RecordsUpdated;
        }
        Console.WriteLine($"╠══════════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  TOTALS: {totalInserted,6} inserted, {totalUpdated,6} updated                        ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");

        if (!result.Success)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {result.ErrorMessage}");
            Console.ResetColor();
            Environment.Exit(1);
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[FATAL] Offline sync failed: {ex.Message}");
        Console.ResetColor();
        offlineSyncLogger.LogError(ex, "Offline sync failed");
        Environment.Exit(1);
    }
}
else if (args.Contains("--dump-api"))
{
    // DUMP MODE: Call all API endpoints once, save raw JSON to disk
    var dumpFolder = args.SkipWhile(a => a != "--dump-folder" && a != "-df").Skip(1).FirstOrDefault()
        ?? "ApiDump";

    Console.WriteLine();
    Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║  API DUMP MODE                                                    ║");
    Console.WriteLine("║  Calling all endpoints and saving raw JSON to disk                ║");
    Console.WriteLine("║  This uses ONE API connection -- save it for later replay         ║");
    Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
    Console.WriteLine();
    Console.WriteLine($"[CONFIG] Dump Folder: {Path.GetFullPath(dumpFolder)}");
    Console.WriteLine();

    using var apiClient = new MasterPlanApiClient(configuration, apiClientLogger);
    var dumpService = new ApiDumpService(apiClient, dumpServiceLogger, dumpFolder);

    try
    {
        await dumpService.DumpAllEndpointsAsync();

        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  API DUMP COMPLETE                                                ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Files saved to: {Path.GetFullPath(dumpFolder),-48} ║");
        Console.WriteLine("║                                                                  ║");
        Console.WriteLine("║  Next step:                                                      ║");
        Console.WriteLine("║    MasterPlan.SyncEngine.exe --load-from-dump                    ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[FATAL] API dump failed: {ex.Message}");
        Console.ResetColor();
        dumpServiceLogger.LogError(ex, "API dump failed");
        Environment.Exit(1);
    }
}
else if (args.Contains("--load-from-dump"))
{
    // LOAD FROM DUMP: Read local JSON files, run daily sync pipeline without API calls
    var dumpFolder = args.SkipWhile(a => a != "--dump-folder" && a != "-df").Skip(1).FirstOrDefault()
        ?? "ApiDump";

    Console.WriteLine();
    Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║  LOAD FROM DUMP MODE                                             ║");
    Console.WriteLine("║  Reading API data from local JSON files                           ║");
    Console.WriteLine("║  NO Web API calls will be made                                    ║");
    Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
    Console.WriteLine();

    // Validate all dump files exist before proceeding
    var missing = ApiDumpService.EntityNames
        .Where(e => !File.Exists(Path.Combine(dumpFolder, $"{e}.json")))
        .ToList();

    if (missing.Count > 0)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("[ERROR] Missing dump files:");
        foreach (var m in missing)
            Console.WriteLine($"  - {Path.Combine(dumpFolder, m + ".json")}");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("Run --dump-api first to create the dump files.");
        Environment.Exit(1);
    }

    Console.WriteLine($"[CONFIG] Dump Folder: {Path.GetFullPath(dumpFolder)}");
    Console.WriteLine($"[CONFIG] Target DB:   Replica_DB");
    Console.WriteLine("[CONFIG] API Calls:   NONE (reading from local dump files)");
    Console.WriteLine();

    // Create API client in dump-load mode -- reads files instead of HTTP
    using var apiClient = new MasterPlanApiClient(configuration, apiClientLogger);
    apiClient.SetDumpLoadMode(dumpFolder);

    var apiSyncService = new ApiDailySyncService(apiClient, replicaConnectionString, apiSyncLogger, siDataConnectionString: siDataConnectionString);

    try
    {
        var result = await apiSyncService.RunDailySyncAsync();

        // Display results
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║              LOAD FROM DUMP -- FINAL STATUS                       ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Status:    {(result.Success ? "SUCCESS" : "FAILURE"),-53} ║");
        Console.WriteLine($"║  Duration:  {(result.EndTime - result.StartTime).TotalSeconds:F1} seconds{new string(' ', 45 - $"{(result.EndTime - result.StartTime).TotalSeconds:F1}".Length)} ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  ENTITY RESULTS:                                                 ║");

        var totalInserted = 0;
        var totalUpdated = 0;
        foreach (var (entity, entityResult) in result.EntityResults)
        {
            var status = entityResult.ErrorMessage == null ? "ok" : "ERR";
            Console.WriteLine($"║  {status,-3} {entity,-15}: {entityResult.RecordsInserted,5} ins, {entityResult.RecordsUpdated,5} upd, {entityResult.RecordsFetched,5} fetch ║");
            totalInserted += entityResult.RecordsInserted;
            totalUpdated += entityResult.RecordsUpdated;
        }
        Console.WriteLine($"╠══════════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  TOTALS: {totalInserted,6} inserted, {totalUpdated,6} updated                        ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");

        if (!result.Success)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {result.ErrorMessage}");
            Console.ResetColor();
            Environment.Exit(1);
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[FATAL] Load from dump failed: {ex.Message}");
        Console.ResetColor();
        dumpServiceLogger.LogError(ex, "Load from dump failed");
        Environment.Exit(1);
    }
}
else
{
    // Show usage
    Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║          MasterPlan.SyncEngine - Data Pipeline Tool              ║");
    Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
    Console.WriteLine();
    Console.WriteLine("USAGE:");
    Console.WriteLine();
    Console.WriteLine("  PHASE 1 - Monthly ETL (Backup → Replica_DB)");
    Console.WriteLine("  ─────────────────────────────────────────────────────────────────");
    Console.WriteLine("  --monthly, -m          Run Monthly Backup/Restore ETL Pipeline");
    Console.WriteLine("    --backup, -b <path>  Path to the MasterPlan .bak file");
    Console.WriteLine();
    Console.WriteLine("    Steps performed:");
    Console.WriteLine("      1. [RESTORE] SMO restore of .bak → Db_Mp_SiEng");
    Console.WriteLine("      2. [INIT]    Create Replica_DB and Sync_* tables if needed");
    Console.WriteLine("      3. [ETL]     INSERT INTO...SELECT with JOINs and transforms");
    Console.WriteLine("      4. [STOP]    Wait for verification before daily sync");
    Console.WriteLine();
    Console.WriteLine("  PHASE 2 - Daily Delta Sync (API → Replica_DB)");
    Console.WriteLine("  ─────────────────────────────────────────────────────────────────");
    Console.WriteLine("  --daily, -d            Run Daily Delta Sync via MasterPlan Web API");
    Console.WriteLine("  --daily-api, -da       Same as --daily (explicit API mode)");
    Console.WriteLine("  --daily-db, -dd        Run Daily Delta Sync via direct database (legacy)");
    Console.WriteLine("  --validate-only, -v    Only validate endpoints, don't run sync");
    Console.WriteLine("  --force                Continue sync even if endpoint validation fails");
    Console.WriteLine("  --no-capture           Disable raw capture mode (skips saving API responses)");
    Console.WriteLine();
    Console.WriteLine("  OFFLINE SIMULATION MODE (no API calls)");
    Console.WriteLine("  ─────────────────────────────────────────────────────────────────");
    Console.WriteLine("  --offline, -o          Run sync using local dump files instead of API");
    Console.WriteLine("    --dump-folder, -df   Path to dump folder (default: ./20260213_010939)");
    Console.WriteLine("    --reset, -r          Clear watermarks for full reload");
    Console.WriteLine("    --force              Continue even if some dump files are missing");
    Console.WriteLine();
    Console.WriteLine("  API DUMP / REPLAY MODE (temporary -- for validation with limited API access)");
    Console.WriteLine("  ─────────────────────────────────────────────────────────────────");
    Console.WriteLine("  --dump-api             Call all API endpoints ONCE, save raw JSON to ./ApiDump/");
    Console.WriteLine("  --load-from-dump       Load from ./ApiDump/ files, run daily sync (NO API calls)");
    Console.WriteLine("    --dump-folder, -df   Custom dump folder path (default: ./ApiDump)");
    Console.WriteLine();
    Console.WriteLine("  RAW CAPTURE MODE (default: enabled for debugging):");
    Console.WriteLine("    Saves API responses to D:\\file\\MasterPlanApiDump\\<timestamp>\\");
    Console.WriteLine("    Files: *.ndjson (data), *.meta.json (metadata), SchemaMismatch.*.json");
    Console.WriteLine();
    Console.WriteLine("EXAMPLES:");
    Console.WriteLine();
    Console.WriteLine(@"  # Monthly ETL from backup (run first of month)");
    Console.WriteLine(@"  MasterPlan.SyncEngine.exe --monthly --backup ""C:\Backups\MasterPlan.bak""");
    Console.WriteLine();
    Console.WriteLine(@"  # Daily API sync (run daily after Phase 1 verification)");
    Console.WriteLine(@"  MasterPlan.SyncEngine.exe --daily");
    Console.WriteLine();
    Console.WriteLine(@"  # Offline sync using dump files (for testing without API)");
    Console.WriteLine(@"  MasterPlan.SyncEngine.exe --offline --reset");
    Console.WriteLine();
    Console.WriteLine(@"  # Offline sync with custom dump folder");
    Console.WriteLine(@"  MasterPlan.SyncEngine.exe --offline --dump-folder ""C:\dumps\20260213""");
    Console.WriteLine();
    Console.WriteLine(@"  # Dump all API responses to disk (uses 1 API connection)");
    Console.WriteLine(@"  MasterPlan.SyncEngine.exe --dump-api");
    Console.WriteLine();
    Console.WriteLine(@"  # Load from dump and sync to DB (unlimited runs, no API calls)");
    Console.WriteLine(@"  MasterPlan.SyncEngine.exe --load-from-dump");
    Console.WriteLine();
    Console.WriteLine("CONFIGURATION (appsettings.json):");
    Console.WriteLine();
    Console.WriteLine("  ConnectionStrings:");
    Console.WriteLine("    SourceDatabase      → Db_Mp_SiEng (restored from backup)");
    Console.WriteLine("    ReplicaDatabase     → Replica_DB (ETL target, API sync target)");
    Console.WriteLine("    MasterConnection    → master database for SMO operations");
    Console.WriteLine();
    Console.WriteLine("  MasterPlanApi:");
    Console.WriteLine("    BaseUrl             → API endpoint URL");
    Console.WriteLine("    ApiKey              → Authentication key");
    Console.WriteLine("    TimeoutSeconds      → Request timeout (default: 300)");
}

