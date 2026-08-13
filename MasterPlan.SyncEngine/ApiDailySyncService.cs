using System.Data;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Dapper;
using MasterPlan.SyncEngine.Models;

namespace MasterPlan.SyncEngine;

/// <summary>
/// Result of a daily sync operation
/// </summary>
public class DailySyncResult
{
    public bool Success { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, EntitySyncResult> EntityResults { get; set; } = new();
}

/// <summary>
/// Sync result for a single entity type
/// </summary>
public class EntitySyncResult
{
    public string EntityName { get; set; } = string.Empty;
    public int RecordsFetched { get; set; }
    public int RecordsInserted { get; set; }
    public int RecordsUpdated { get; set; }
    public int RecordsSkipped { get; set; }
    public DateTime? PreviousWatermark { get; set; }
    public DateTime? NewWatermark { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>True when this entity was fetched unfiltered as part of a reconciliation pass.</summary>
    public bool ReconciliationRun { get; set; }

    /// <summary>Replica rows the API did not return during reconciliation. Reported; may be purged under DEV-019 gates.</summary>
    public int OrphanCandidates { get; set; }

    /// <summary>Orphans actually deleted this run (DEV-019).</summary>
    public int OrphanPurged { get; set; }

    /// <summary>Orphans deferred by age / first-sighting gates.</summary>
    public int OrphanDeferred { get; set; }

    /// <summary>Non-null when purge was blocked by a safety gate.</summary>
    public string? OrphanPurgeBlockedReason { get; set; }
}

/// <summary>
/// Service for performing daily incremental sync from MasterPlan API to Replica database.
/// Uses watermarks (LastUpdated timestamps) to fetch only changed records.
/// </summary>
public class ApiDailySyncService
{
    private readonly MasterPlanApiClient _apiClient;
    private readonly string _replicaConnectionString;
    private readonly string? _siDataConnectionString;
    private readonly ILogger<ApiDailySyncService> _logger;
    private readonly RawCaptureService? _captureService;
    private readonly HoursSyncOptions _hoursOptions;
    private SqlConnection? _lockConnection;

    // Entity configuration: table name, watermark column, whether it supports lastUpdated filter
    // Schema source: 20260213_010939/*.ndjson dump files
    //
    // Watermark rules:
    //   ALL entities EXCEPT TimeHourReports/ProjectHours/Conversations → watermark on LastUpdated
    //   Conversations → watermark on CreatedDate
    //   ProjectHours → watermark on ReportDate
    //   TimeHourReports → watermark on ReportDateTime (no LastUpdated in API or backup)
    //
    // Sync strategy: UPSERT BY ID (NO BULK DELETE)
    //   Entities with LastUpdated → INSERT if new, UPDATE only if Api.LastUpdated > Db.LastUpdated, else SKIP
    //   Entities without LastUpdated (Conversations, ProjectHours, TimeHourReports) → INSERT if new, UPDATE if exists
    //   NO overlap-replace. NO DELETE by date range. Safe after monthly restore (LastUpdated=BackupFinishDate).
    private static readonly Dictionary<string, (string TableName, string WatermarkColumn, bool SupportsLastUpdated)> EntityConfig = new()
    {
        ["Projects"] = ("MP_Projects", "LastUpdated", true),
        ["Bids"] = ("MP_Bids", "LastUpdated", true),
        ["Bills"] = ("MP_Bills", "LastUpdated", true),
        ["Companies"] = ("MP_Companies", "LastUpdated", true),
        ["Contacts"] = ("MP_Contacts", "LastUpdated", true),
        ["Employees"] = ("MP_Employees", "LastUpdated", true),
        ["Intakes"] = ("MP_Intakes", "LastUpdated", true),
        ["Tasks"] = ("MP_Tasks", "LastUpdated", true), // API docs show dueDate param only, but we use lastUpdated for incremental sync
        ["Conversations"] = ("MP_Conversations", "CreatedDate", false), // Uses CreatedDate, not lastUpdated filter
        ["ProjectHours"] = ("MP_ProjectHours", "ReportDate", false), // Uses fromDate filter, watermark on ReportDate
        // Hours endpoints
        ["TimeHourReports"] = ("MP_TimeHourReports", "ReportDateTime", false), // NO LastUpdated in API — watermark on ReportDateTime (API field "DateTime")
        ["ProjectHoursExtended"] = ("MP_ProjectHoursExtended", "ReportDate", true) // Watermark on ReportDate (the field FromDate filters); LastUpdated still drives the upsert comparison
    };

    /// <summary>
    /// Entities whose rows are back-datable, so they use a lookback window plus periodic
    /// reconciliation instead of a bare watermark. See docs/MASTERPLAN_SYNC_WATERMARKS.md.
    /// </summary>
    private const string ReconcileStateSuffix = ":Reconcile";

    public ApiDailySyncService(
        MasterPlanApiClient apiClient, 
        string replicaConnectionString, 
        ILogger<ApiDailySyncService> logger,
        RawCaptureService? captureService = null,
        string? siDataConnectionString = null,
        HoursSyncOptions? hoursOptions = null)
    {
        _apiClient = apiClient;
        _replicaConnectionString = replicaConnectionString;
        _logger = logger;
        _captureService = captureService;
        _siDataConnectionString = siDataConnectionString;
        _hoursOptions = hoursOptions ?? new HoursSyncOptions();
    }

    /// <summary>
    /// Run the daily incremental sync for all entities
    /// 
    /// ANALYSIS MODE:
    /// - If Replica is empty (no watermarks), uses 2017-01-01 as initial date
    /// - This fetches the FULL dataset from Web Service for analysis
    /// </summary>
    public async Task<DailySyncResult> RunDailySyncAsync(CancellationToken cancellationToken = default)
    {
        var runGuid = Guid.NewGuid();
        var runId = runGuid.ToString("N")[..8];
        var startedAt = DateTime.UtcNow;
        var currentStage = "Initialization";
        var result = new DailySyncResult
        {
            StartTime = startedAt
        };

        _logger.LogInformation("[RUN {RunId}] Starting Daily API Sync at {StartTime}", runId, result.StartTime);
        _apiClient.StartRunTracking(runId);

        try
        {
            // ╔══════════════════════════════════════════════════════════════════╗
            // ║  TEMPORARY TEST: Uncomment to verify failure logging to          ║
            // ║  SiData.dbo.Sync_RunFailures. Remove after verification.         ║
            // ╚══════════════════════════════════════════════════════════════════╝
            // throw new Exception("TEST FAILURE - VERIFY DB LOGGING");

            // Ensure sync state table exists
            currentStage = "EnsureSyncState";
            await EnsureSyncStateTableAsync();

            // Check if this is initial load (analysis mode)
            var isInitialLoad = await IsInitialLoadAsync();
            if (isInitialLoad)
            {
                Console.WriteLine();
                Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
                Console.WriteLine("║  📊 ANALYSIS MODE: Initial Load from Web Service                 ║");
                Console.WriteLine("║                                                                  ║");
                Console.WriteLine("║  Replica is EMPTY - fetching FULL dataset from Web Service.      ║");
                Console.WriteLine("║  Using watermark: 2017-01-01 (to get all historical data)        ║");
                Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
                Console.WriteLine();
            }

            // Check for concurrent execution
            currentStage = "AcquireLock";
            if (!await TryAcquireLockAsync())
            {
                throw new InvalidOperationException("Another sync process is already running");
            }

            try
            {
                // Sync each entity type
                currentStage = "Projects";
                result.EntityResults["Projects"] = await SyncProjectsAsync(cancellationToken);
                currentStage = "Companies";
                result.EntityResults["Companies"] = await SyncCompaniesAsync(cancellationToken);
                currentStage = "Contacts";
                result.EntityResults["Contacts"] = await SyncContactsAsync(cancellationToken);
                currentStage = "Employees";
                result.EntityResults["Employees"] = await SyncEmployeesAsync(cancellationToken);
                currentStage = "Bids";
                result.EntityResults["Bids"] = await SyncBidsAsync(cancellationToken);
                currentStage = "Bills";
                result.EntityResults["Bills"] = await SyncBillsAsync(cancellationToken);
                currentStage = "Intakes";
                result.EntityResults["Intakes"] = await SyncIntakesAsync(cancellationToken);
                currentStage = "Tasks";
                result.EntityResults["Tasks"] = await SyncTasksAsync(cancellationToken);
                currentStage = "Conversations";
                result.EntityResults["Conversations"] = await SyncConversationsAsync(cancellationToken);
                currentStage = "ProjectHours";
                result.EntityResults["ProjectHours"] = await SyncProjectHoursAsync(cancellationToken);

                // New Hours endpoints
                currentStage = "TimeHourReports";
                result.EntityResults["TimeHourReports"] = await SyncTimeHourReportsAsync(cancellationToken);
                currentStage = "ProjectHoursExtended";
                result.EntityResults["ProjectHoursExtended"] = await SyncProjectHoursExtendedAsync(cancellationToken);

                // ═══════════════════════════════════════════════════════════════════
                // CROSS-SYNC: Push mapped MP data → SiNet Company/Contact tables
                // Runs AFTER all entity syncs so MP_Companies/MP_Contacts are fresh.
                // Only updates SiNet rows that have a MasterPlanCompanyId/MasterPlanContactId.
                // ═══════════════════════════════════════════════════════════════════
                currentStage = "CrossSync";
                result.EntityResults["CrossSync"] = await CrossSyncToSiNetAsync(cancellationToken);

                // Check if any entity sync failed — if so, throw so the outer catch
                // triggers LogRunFailureAsync (entity methods catch internally and don't re-throw)
                var failedEntities = result.EntityResults
                    .Where(kvp => kvp.Value.ErrorMessage != null)
                    .Select(kvp => kvp.Key)
                    .ToList();

                if (failedEntities.Count > 0)
                {
                    currentStage = string.Join(", ", failedEntities);
                    var errors = result.EntityResults
                        .Where(kvp => kvp.Value.ErrorMessage != null)
                        .Select(kvp => $"{kvp.Key}: {kvp.Value.ErrorMessage}");
                    throw new InvalidOperationException(
                        $"Entity sync failed for: {string.Join("; ", errors)}");
                }

                currentStage = "RecordRunHistory";
                result.Success = true;

                // Record run history - set EndTime BEFORE recording
                result.EndTime = DateTime.UtcNow;
                await RecordRunHistoryAsync(result);

                // Generate capture session summary if capture is enabled
                if (_captureService != null)
                {
                    var entityCounts = result.EntityResults.ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value.RecordsFetched);
                    await _captureService.GenerateSessionSummaryAsync(entityCounts, result.Success, result.ErrorMessage);
                }
            }
            finally
            {
                await ReleaseLockAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Daily sync failed at stage '{Stage}'", currentStage);
            result.Success = false;
            result.ErrorMessage = ex.Message;

            // Insert ONE failure row into SiData.dbo.Sync_RunFailures
            await LogRunFailureAsync(runGuid, startedAt, currentStage, ex);

            // Re-throw so callers (Program.cs) see the failure and can exit appropriately.
            // The failure row is already written above — do NOT swallow silently.
            throw;
        }

        result.EndTime = DateTime.UtcNow;

        // ═══════════════════════════════════════════════════════════════════════════════
        // API CALL SUMMARY — proves each endpoint is called exactly once per run
        // ═══════════════════════════════════════════════════════════════════════════════
        var callSummary = _apiClient.GetApiCallSummary();
        _logger.LogWarning("[API SUMMARY] RunId={RunId} TotalCalls={TotalCalls}", runId, callSummary.Values.Sum());
        Console.WriteLine();
        Console.WriteLine($"╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  API CALL SUMMARY — RunId={runId}                                ║");
        Console.WriteLine($"╠══════════════════════════════════════════════════════════════════╣");
        foreach (var (endpoint, count) in callSummary.OrderBy(kvp => kvp.Key))
        {
            var indicator = count > 1 ? "⚠ DUPLICATE" : "✓";
            Console.WriteLine($"║  {indicator} {endpoint,-45} → {count} call(s)  ║");
            _logger.LogWarning("[API SUMMARY] RunId={RunId} {Indicator} {Endpoint} → {Count} call(s)",
                runId, indicator, endpoint, count);
        }
        Console.WriteLine($"╠══════════════════════════════════════════════════════════════════╣");

        // Specifically verify the 3 ProjectHours-related endpoints
        var hourEndpoints = new[] { "ProjectHours/", "projecthours/GetTimeHourReports", "projecthours/GetProjectHoursExtended" };
        foreach (var ep in hourEndpoints)
        {
            var syncCalls = callSummary.Where(kvp => kvp.Key.Contains(ep)).ToList();
            var totalForEp = syncCalls.Sum(kvp => kvp.Value);
            var status = totalForEp == 1 ? "✓ OK (exactly 1)" : totalForEp == 0 ? "○ NOT CALLED" : $"⚠ CALLED {totalForEp}x";
            Console.WriteLine($"║  VERIFY: {ep,-40} {status,-15} ║");
            _logger.LogWarning("[API VERIFY] RunId={RunId} Endpoint={Endpoint} TotalCalls={Total} Status={Status}",
                runId, ep, totalForEp, status);
        }
        Console.WriteLine($"╚══════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        _logger.LogInformation("[RUN {RunId}] Daily API Sync completed. Success: {Success}, Duration: {Duration}s",
            runId, result.Success, (result.EndTime - result.StartTime).TotalSeconds);

        return result;
    }

    /// <summary>
    /// Logs a single failure row to SiData.dbo.Sync_RunFailures.
    /// Wrapped in its own try/catch so logging failures never mask the original error.
    /// </summary>
    private async Task LogRunFailureAsync(Guid runGuid, DateTime startedAt, string errorStage, Exception ex)
    {
        if (string.IsNullOrEmpty(_siDataConnectionString))
        {
            _logger.LogWarning("SiData connection string not configured — skipping Sync_RunFailures insert");
            return;
        }

        try
        {
            var appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

            await using var conn = new SqlConnection(_siDataConnectionString);
            await conn.OpenAsync();

            await conn.ExecuteAsync(@"
                INSERT INTO dbo.Sync_RunFailures
                    (RunId, StartedAt, FailedAt, MachineName, AppVersion, ErrorStage, ErrorType, ErrorMessage, StackTrace)
                VALUES
                    (@RunId, @StartedAt, @FailedAt, @MachineName, @AppVersion, @ErrorStage, @ErrorType, @ErrorMessage, @StackTrace)",
                new
                {
                    RunId = runGuid,
                    StartedAt = startedAt,
                    FailedAt = DateTime.UtcNow,
                    MachineName = Environment.MachineName,
                    AppVersion = appVersion,
                    ErrorStage = errorStage,
                    ErrorType = ex.GetType().FullName ?? ex.GetType().Name,
                    ErrorMessage = ex.ToString(),
                    StackTrace = ex.StackTrace
                });

            _logger.LogInformation("[FAILURE LOGGED] RunId={RunId} Stage={Stage} Type={Type}",
                runGuid, errorStage, ex.GetType().Name);
        }
        catch (Exception logEx)
        {
            _logger.LogError(logEx, "Failed to log run failure to SiData.dbo.Sync_RunFailures (original error preserved)");
        }
    }

    /// <summary>
    /// Check if this is an initial load (no watermarks exist = empty replica)
    /// </summary>
    private async Task<bool> IsInitialLoadAsync()
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        var count = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM Sync_State WHERE LastWatermark IS NOT NULL");
        return count == 0;
    }

    #region Entity Sync Methods

    private async Task<EntitySyncResult> SyncProjectsAsync(CancellationToken ct)
    {
        var result = new EntitySyncResult { EntityName = "Projects" };
        try
        {
            result.PreviousWatermark = await GetWatermarkAsync("Projects");
            LogWatermarkDiagnostics("Projects", "LastUpdated", result.PreviousWatermark, "?lastUpdated=");
            var projects = await _apiClient.GetProjectsAsync(result.PreviousWatermark, ct);
            result.RecordsFetched = projects.Count;
            var apiNullCount = projects.Count(p => !p.LastUpdated.HasValue);
            _logger.LogInformation("[DIAG] {Entity}: ApiLastUpdatedNull={NullCount} of {Total} records returned by API",
                "Projects", apiNullCount, projects.Count);

            var groups = projects.GroupBy(p => p.ID).ToList();
            var duplicateGroups = groups.Count(g => g.Count() > 1);
            projects = groups
                .Select(g => g.OrderByDescending(p => p.LastUpdated ?? DateTime.MinValue).First())
                .ToList();
            _logger.LogWarning("[DIAG] {Entity}: ApiDuplicateIds={DupGroups} (raw={RawCount}, deduped={DedupedCount})",
                "Projects", duplicateGroups, groups.Sum(g => g.Count()), projects.Count);

            if (projects.Count > 0)
            {
                var (inserted, updated, skipped) = await UpsertProjectsAsync(projects);
                result.RecordsInserted = inserted;
                result.RecordsUpdated = updated;
                result.RecordsSkipped = skipped;
                var batchMax = projects.Max(p => p.LastUpdated);
                result.NewWatermark = (batchMax.HasValue && batchMax > result.PreviousWatermark)
                    ? batchMax : result.PreviousWatermark;
                await UpdateWatermarkAsync("Projects", result.NewWatermark);
            }
            await CompleteEntitySyncAsync(result, "LastUpdated");
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Failed to sync Projects");
        }
        return result;
    }

    private async Task<EntitySyncResult> SyncCompaniesAsync(CancellationToken ct)
    {
        var result = new EntitySyncResult { EntityName = "Companies" };
        try
        {
            result.PreviousWatermark = await GetWatermarkAsync("Companies");
            LogWatermarkDiagnostics("Companies", "LastUpdated", result.PreviousWatermark, "?lastUpdated=");
            var companies = await _apiClient.GetCompaniesAsync(result.PreviousWatermark, ct);
            result.RecordsFetched = companies.Count;
            var apiNullCount = companies.Count(c => !c.LastUpdated.HasValue);
            _logger.LogInformation("[DIAG] {Entity}: ApiLastUpdatedNull={NullCount} of {Total} records returned by API",
                "Companies", apiNullCount, companies.Count);

            var groups = companies.GroupBy(c => c.ID).ToList();
            var duplicateGroups = groups.Count(g => g.Count() > 1);
            companies = groups
                .Select(g => g.OrderByDescending(c => c.LastUpdated ?? DateTime.MinValue).First())
                .ToList();
            _logger.LogWarning("[DIAG] {Entity}: ApiDuplicateIds={DupGroups} (raw={RawCount}, deduped={DedupedCount})",
                "Companies", duplicateGroups, groups.Sum(g => g.Count()), companies.Count);

            if (companies.Count > 0)
            {
                var (inserted, updated, skipped) = await UpsertCompaniesAsync(companies);
                result.RecordsInserted = inserted;
                result.RecordsUpdated = updated;
                result.RecordsSkipped = skipped;
                var batchMax = companies.Max(c => c.LastUpdated);
                result.NewWatermark = (batchMax.HasValue && batchMax > result.PreviousWatermark)
                    ? batchMax : result.PreviousWatermark;
                await UpdateWatermarkAsync("Companies", result.NewWatermark);
            }
            await CompleteEntitySyncAsync(result, "LastUpdated");
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Failed to sync Companies");
        }
        return result;
    }

    private async Task<EntitySyncResult> SyncContactsAsync(CancellationToken ct)
    {
        var result = new EntitySyncResult { EntityName = "Contacts" };
        try
        {
            result.PreviousWatermark = await GetWatermarkAsync("Contacts");
            LogWatermarkDiagnostics("Contacts", "LastUpdated", result.PreviousWatermark, "?lastUpdated=");
            var contacts = await _apiClient.GetContactsAsync(result.PreviousWatermark, ct);
            result.RecordsFetched = contacts.Count;
            var apiNullCount = contacts.Count(c => !c.LastUpdated.HasValue);
            _logger.LogInformation("[DIAG] {Entity}: ApiLastUpdatedNull={NullCount} of {Total} records returned by API",
                "Contacts", apiNullCount, contacts.Count);

            var groups = contacts.GroupBy(c => c.ID).ToList();
            var duplicateGroups = groups.Count(g => g.Count() > 1);
            contacts = groups
                .Select(g => g.OrderByDescending(c => c.LastUpdated ?? DateTime.MinValue).First())
                .ToList();
            _logger.LogWarning("[DIAG] {Entity}: ApiDuplicateIds={DupGroups} (raw={RawCount}, deduped={DedupedCount})",
                "Contacts", duplicateGroups, groups.Sum(g => g.Count()), contacts.Count);

            if (contacts.Count > 0)
            {
                var (inserted, updated, skipped) = await UpsertContactsAsync(contacts);
                result.RecordsInserted = inserted;
                result.RecordsUpdated = updated;
                result.RecordsSkipped = skipped;
                var batchMax = contacts.Max(c => c.LastUpdated);
                result.NewWatermark = (batchMax.HasValue && batchMax > result.PreviousWatermark)
                    ? batchMax : result.PreviousWatermark;
                await UpdateWatermarkAsync("Contacts", result.NewWatermark);
            }
            await CompleteEntitySyncAsync(result, "LastUpdated");
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Failed to sync Contacts");
        }
        return result;
    }

    private async Task<EntitySyncResult> SyncEmployeesAsync(CancellationToken ct)
    {
        var result = new EntitySyncResult { EntityName = "Employees" };
        try
        {
            result.PreviousWatermark = await GetWatermarkAsync("Employees");
            LogWatermarkDiagnostics("Employees", "LastUpdated", result.PreviousWatermark, "?lastUpdated=");
            var employees = await _apiClient.GetEmployeesAsync(result.PreviousWatermark, ct);
            result.RecordsFetched = employees.Count;
            var apiNullCount = employees.Count(e => !e.LastUpdated.HasValue);
            _logger.LogInformation("[DIAG] {Entity}: ApiLastUpdatedNull={NullCount} of {Total} records returned by API",
                "Employees", apiNullCount, employees.Count);

            var groups = employees.GroupBy(e => e.ID).ToList();
            var duplicateGroups = groups.Count(g => g.Count() > 1);
            employees = groups
                .Select(g => g.OrderByDescending(e => e.LastUpdated ?? DateTime.MinValue).First())
                .ToList();
            _logger.LogWarning("[DIAG] {Entity}: ApiDuplicateIds={DupGroups} (raw={RawCount}, deduped={DedupedCount})",
                "Employees", duplicateGroups, groups.Sum(g => g.Count()), employees.Count);

            if (employees.Count > 0)
            {
                var (inserted, updated, skipped) = await UpsertEmployeesAsync(employees);
                result.RecordsInserted = inserted;
                result.RecordsUpdated = updated;
                result.RecordsSkipped = skipped;
                var batchMax = employees.Max(e => e.LastUpdated);
                result.NewWatermark = (batchMax.HasValue && batchMax > result.PreviousWatermark)
                    ? batchMax : result.PreviousWatermark;
                await UpdateWatermarkAsync("Employees", result.NewWatermark);
            }
            await CompleteEntitySyncAsync(result, "LastUpdated");
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Failed to sync Employees");
        }
        return result;
    }

    private async Task<EntitySyncResult> SyncBidsAsync(CancellationToken ct)
    {
        var result = new EntitySyncResult { EntityName = "Bids" };
        try
        {
            result.PreviousWatermark = await GetWatermarkAsync("Bids");
            LogWatermarkDiagnostics("Bids", "LastUpdated", result.PreviousWatermark, "?lastUpdated=");
            var bids = await _apiClient.GetBidsAsync(result.PreviousWatermark, ct);
            result.RecordsFetched = bids.Count;
            var apiNullCount = bids.Count(b => !b.LastUpdated.HasValue);
            _logger.LogInformation("[DIAG] {Entity}: ApiLastUpdatedNull={NullCount} of {Total} records returned by API",
                "Bids", apiNullCount, bids.Count);

            var groups = bids.GroupBy(b => b.ID).ToList();
            var duplicateGroups = groups.Count(g => g.Count() > 1);
            bids = groups
                .Select(g => g.OrderByDescending(b => b.LastUpdated ?? DateTime.MinValue).First())
                .ToList();
            _logger.LogWarning("[DIAG] {Entity}: ApiDuplicateIds={DupGroups} (raw={RawCount}, deduped={DedupedCount})",
                "Bids", duplicateGroups, groups.Sum(g => g.Count()), bids.Count);

            if (bids.Count > 0)
            {
                var (inserted, updated, skipped) = await UpsertBidsAsync(bids);
                result.RecordsInserted = inserted;
                result.RecordsUpdated = updated;
                result.RecordsSkipped = skipped;
                var batchMax = bids.Max(b => b.LastUpdated);
                result.NewWatermark = (batchMax.HasValue && batchMax > result.PreviousWatermark)
                    ? batchMax : result.PreviousWatermark;
                await UpdateWatermarkAsync("Bids", result.NewWatermark);
            }
            await CompleteEntitySyncAsync(result, "LastUpdated");
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Failed to sync Bids");
        }
        return result;
    }

    private async Task<EntitySyncResult> SyncBillsAsync(CancellationToken ct)
    {
        var result = new EntitySyncResult { EntityName = "Bills" };
        try
        {
            result.PreviousWatermark = await GetWatermarkAsync("Bills");
            LogWatermarkDiagnostics("Bills", "LastUpdated", result.PreviousWatermark, "?lastUpdated=");
            var bills = await _apiClient.GetBillsAsync(result.PreviousWatermark, ct);
            result.RecordsFetched = bills.Count;
            var apiNullCount = bills.Count(b => !b.LastUpdated.HasValue);
            _logger.LogInformation("[DIAG] {Entity}: ApiLastUpdatedNull={NullCount} of {Total} records returned by API",
                "Bills", apiNullCount, bills.Count);

            var groups = bills.GroupBy(b => b.ID).ToList();
            var duplicateGroups = groups.Count(g => g.Count() > 1);
            bills = groups
                .Select(g => g.OrderByDescending(b => b.LastUpdated ?? DateTime.MinValue).First())
                .ToList();
            _logger.LogWarning("[DIAG] {Entity}: ApiDuplicateIds={DupGroups} (raw={RawCount}, deduped={DedupedCount})",
                "Bills", duplicateGroups, groups.Sum(g => g.Count()), bills.Count);

            if (bills.Count > 0)
            {
                var (inserted, updated, skipped) = await UpsertBillsAsync(bills);
                result.RecordsInserted = inserted;
                result.RecordsUpdated = updated;
                result.RecordsSkipped = skipped;
                var batchMax = bills.Max(b => b.LastUpdated);
                result.NewWatermark = (batchMax.HasValue && batchMax > result.PreviousWatermark)
                    ? batchMax : result.PreviousWatermark;
                await UpdateWatermarkAsync("Bills", result.NewWatermark);
            }
            await CompleteEntitySyncAsync(result, "LastUpdated");
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Failed to sync Bills");
        }
        return result;
    }

    private async Task<EntitySyncResult> SyncIntakesAsync(CancellationToken ct)
    {
        var result = new EntitySyncResult { EntityName = "Intakes" };
        try
        {
            result.PreviousWatermark = await GetWatermarkAsync("Intakes");
            LogWatermarkDiagnostics("Intakes", "LastUpdated", result.PreviousWatermark, "?lastUpdated=");
            var intakes = await _apiClient.GetIntakesAsync(result.PreviousWatermark, ct);
            result.RecordsFetched = intakes.Count;
            var apiNullCount = intakes.Count(i => !i.LastUpdated.HasValue);
            _logger.LogInformation("[DIAG] {Entity}: ApiLastUpdatedNull={NullCount} of {Total} records returned by API",
                "Intakes", apiNullCount, intakes.Count);

            var groups = intakes.GroupBy(i => i.ID).ToList();
            var duplicateGroups = groups.Count(g => g.Count() > 1);
            intakes = groups
                .Select(g => g.OrderByDescending(i => i.LastUpdated ?? DateTime.MinValue).First())
                .ToList();
            _logger.LogWarning("[DIAG] {Entity}: ApiDuplicateIds={DupGroups} (raw={RawCount}, deduped={DedupedCount})",
                "Intakes", duplicateGroups, groups.Sum(g => g.Count()), intakes.Count);

            if (intakes.Count > 0)
            {
                var (inserted, updated, skipped) = await UpsertIntakesAsync(intakes);
                result.RecordsInserted = inserted;
                result.RecordsUpdated = updated;
                result.RecordsSkipped = skipped;
                var batchMax = intakes.Max(i => i.LastUpdated);
                result.NewWatermark = (batchMax.HasValue && batchMax > result.PreviousWatermark)
                    ? batchMax : result.PreviousWatermark;
                await UpdateWatermarkAsync("Intakes", result.NewWatermark);
            }
            await CompleteEntitySyncAsync(result, "LastUpdated");
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Failed to sync Intakes");
        }
        return result;
    }

    private async Task<EntitySyncResult> SyncTasksAsync(CancellationToken ct)
    {
        var result = new EntitySyncResult { EntityName = "Tasks" };
        try
        {
            // Tasks support lastUpdated filter (verified from API dump)
            // NOTE: API docs show only dueDate param, but actual API also accepts lastUpdated
            result.PreviousWatermark = await GetWatermarkAsync("Tasks");
            LogWatermarkDiagnostics("Tasks", "LastUpdated", result.PreviousWatermark, "?lastUpdated=");
            var tasks = await _apiClient.GetTasksAsync(result.PreviousWatermark, ct);
            result.RecordsFetched = tasks.Count;
            var apiNullCount = tasks.Count(t => !t.LastUpdated.HasValue);
            _logger.LogInformation("[DIAG] {Entity}: ApiLastUpdatedNull={NullCount} of {Total} records returned by API",
                "Tasks", apiNullCount, tasks.Count);

            var groups = tasks.GroupBy(t => t.ID).ToList();
            var duplicateGroups = groups.Count(g => g.Count() > 1);
            tasks = groups
                .Select(g => g.OrderByDescending(t => t.LastUpdated ?? DateTime.MinValue).First())
                .ToList();
            _logger.LogWarning("[DIAG] {Entity}: ApiDuplicateIds={DupGroups} (raw={RawCount}, deduped={DedupedCount})",
                "Tasks", duplicateGroups, groups.Sum(g => g.Count()), tasks.Count);

            if (tasks.Count > 0)
            {
                var (inserted, updated, skipped) = await UpsertTasksAsync(tasks);
                result.RecordsInserted = inserted;
                result.RecordsUpdated = updated;
                result.RecordsSkipped = skipped;
                var batchMax = tasks.Max(t => t.LastUpdated);
                result.NewWatermark = (batchMax.HasValue && batchMax > result.PreviousWatermark)
                    ? batchMax : result.PreviousWatermark;
                await UpdateWatermarkAsync("Tasks", result.NewWatermark);
            }
            await CompleteEntitySyncAsync(result, "LastUpdated");
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Failed to sync Tasks");
        }
        return result;
    }

    private async Task<EntitySyncResult> SyncConversationsAsync(CancellationToken ct)
    {
        var result = new EntitySyncResult { EntityName = "Conversations" };
        try
        {
            // Get watermark based on CreatedDate
            result.PreviousWatermark = await GetWatermarkAsync("Conversations");
            LogWatermarkDiagnostics("Conversations", "CreatedDate", result.PreviousWatermark, "?createdDate=");
            var conversations = await _apiClient.GetConversationsAsync(result.PreviousWatermark, ct);
            result.RecordsFetched = conversations.Count;

            var groups = conversations.GroupBy(c => c.ID).ToList();
            var duplicateGroups = groups.Count(g => g.Count() > 1);
            conversations = groups.Select(g => g.First()).ToList();
            _logger.LogWarning("[DIAG] {Entity}: ApiDuplicateIds={DupGroups} (raw={RawCount}, deduped={DedupedCount})",
                "Conversations", duplicateGroups, groups.Sum(g => g.Count()), conversations.Count);

            if (conversations.Count > 0)
            {
                var (inserted, updated) = await UpsertConversationsAsync(conversations);
                result.RecordsInserted = inserted;
                result.RecordsUpdated = updated;
                var batchMax = conversations.Max(c => c.CreatedDate);
                result.NewWatermark = (batchMax.HasValue && batchMax > result.PreviousWatermark)
                    ? batchMax : result.PreviousWatermark;
                await UpdateWatermarkAsync("Conversations", result.NewWatermark);
            }
            await CompleteEntitySyncAsync(result, "CreatedDate");
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Failed to sync Conversations");
        }
        return result;
    }

    private async Task<EntitySyncResult> SyncProjectHoursAsync(CancellationToken ct)
    {
        var result = new EntitySyncResult { EntityName = "ProjectHours" };
        try
        {
            // Get watermark based on ReportDate (actual API field)
            result.PreviousWatermark = await GetWatermarkAsync("ProjectHours");
            var (fromDate, isReconciliation) = await ResolveHoursFromDateAsync("ProjectHours", result.PreviousWatermark);
            result.ReconciliationRun = isReconciliation;
            LogWatermarkDiagnostics("ProjectHours", "ReportDate", fromDate, "?fromDate=");
            var hours = await _apiClient.GetProjectHoursAsync(fromDate, ct);
            result.RecordsFetched = hours.Count;

            var groups = hours.GroupBy(h => h.ID).ToList();
            var duplicateGroups = groups.Count(g => g.Count() > 1);
            hours = groups.Select(g => g.First()).ToList();
            _logger.LogWarning("[DIAG] {Entity}: ApiDuplicateIds={DupGroups} (raw={RawCount}, deduped={DedupedCount})",
                "ProjectHours", duplicateGroups, groups.Sum(g => g.Count()), hours.Count);

            if (hours.Count > 0)
            {
                var (inserted, updated) = await UpsertProjectHoursAsync(hours);
                result.RecordsInserted = inserted;
                result.RecordsUpdated = updated;
                var batchMax = ClampToToday(hours.Max(h => h.ReportDate));
                result.NewWatermark = (batchMax.HasValue && batchMax > result.PreviousWatermark)
                    ? batchMax : result.PreviousWatermark;
                await UpdateWatermarkAsync("ProjectHours", result.NewWatermark);
            }

            if (isReconciliation)
            {
                result.OrphanCandidates = await CountOrphanCandidatesAsync("ProjectHours", hours.Select(h => h.ID));
                await TryPurgeOrphansAsync(
                    result,
                    entityName: "ProjectHours",
                    tableName: "MP_ProjectHours",
                    reportDateColumn: "ReportDate",
                    fromDate: fromDate,
                    fetchedCount: hours.Count,
                    apiIds: hours.Select(h => h.ID),
                    ct).ConfigureAwait(false);
                await MarkReconciliationCompleteAsync("ProjectHours");
            }
            await CompleteEntitySyncAsync(result, "ReportDate");
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Failed to sync ProjectHours");
        }
        return result;
    }

    /// <summary>
    /// Sync Time Hour Reports using UPSERT-by-ID strategy:
    /// No LastUpdated available — INSERT if new, UPDATE if exists.
    /// No bulk DELETE. Safe after monthly restore.
    /// </summary>
    private async Task<EntitySyncResult> SyncTimeHourReportsAsync(CancellationToken ct)
    {
        var result = new EntitySyncResult { EntityName = "TimeHourReports" };
        try
        {
            result.PreviousWatermark = await GetWatermarkAsync("TimeHourReports");
            var (fromDate, isReconciliation) = await ResolveHoursFromDateAsync("TimeHourReports", result.PreviousWatermark);
            result.ReconciliationRun = isReconciliation;
            LogWatermarkDiagnostics("TimeHourReports", "ReportDateTime", fromDate, "?FromDate=");
            var reports = await _apiClient.GetTimeHourReportsAsync(fromDate, ct);
            result.RecordsFetched = reports.Count;

            var groups = reports.GroupBy(r => r.ID).ToList();
            var duplicateGroups = groups.Count(g => g.Count() > 1);
            reports = groups.Select(g => g.First()).ToList();
            _logger.LogWarning("[DIAG] {Entity}: ApiDuplicateIds={DupGroups} (raw={RawCount}, deduped={DedupedCount})",
                "TimeHourReports", duplicateGroups, groups.Sum(g => g.Count()), reports.Count);

            if (reports.Count > 0)
            {
                var (inserted, updated) = await UpsertTimeHourReportsAsync(reports);
                result.RecordsInserted = inserted;
                result.RecordsUpdated = updated;
                var batchMax = ClampToToday(reports.Max(r => r.ReportDateTime));
                result.NewWatermark = (batchMax.HasValue && batchMax > result.PreviousWatermark)
                    ? batchMax : result.PreviousWatermark;
                await UpdateWatermarkAsync("TimeHourReports", result.NewWatermark);
            }

            if (isReconciliation)
            {
                result.OrphanCandidates = await CountOrphanCandidatesAsync("TimeHourReports", reports.Select(r => r.ID));
                await MarkReconciliationCompleteAsync("TimeHourReports");
            }
            await CompleteEntitySyncAsync(result, "ReportDateTime");
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Failed to sync TimeHourReports");
        }
        return result;
    }

    /// <summary>
    /// Sync Extended Project Hours using UPSERT-by-ID strategy:
    /// INSERT if new, UPDATE only if Api.LastUpdated > Db.LastUpdated, else SKIP.
    /// No bulk DELETE. Safe after monthly restore (LastUpdated=BackupFinishDate).
    /// </summary>
    private async Task<EntitySyncResult> SyncProjectHoursExtendedAsync(CancellationToken ct)
    {
        var result = new EntitySyncResult { EntityName = "ProjectHoursExtended" };
        try
        {
            result.PreviousWatermark = await GetWatermarkAsync("ProjectHoursExtended");
            var (fromDate, isReconciliation) = await ResolveHoursFromDateAsync("ProjectHoursExtended", result.PreviousWatermark);
            result.ReconciliationRun = isReconciliation;
            LogWatermarkDiagnostics("ProjectHoursExtended", "ReportDate", fromDate, "?FromDate=");
            var hours = await _apiClient.GetProjectHoursExtendedAsync(fromDate, ct);
            result.RecordsFetched = hours.Count;
            var apiNullCount = hours.Count(h => !h.LastUpdated.HasValue);
            _logger.LogInformation("[DIAG] {Entity}: ApiLastUpdatedNull={NullCount} of {Total} records returned by API",
                "ProjectHoursExtended", apiNullCount, hours.Count);

            var groups = hours.GroupBy(h => h.ID).ToList();
            var duplicateGroups = groups.Count(g => g.Count() > 1);
            hours = groups
                .Select(g => g.OrderByDescending(h => h.LastUpdated ?? DateTime.MinValue).First())
                .ToList();
            _logger.LogWarning("[DIAG] {Entity}: ApiDuplicateIds={DupGroups} (raw={RawCount}, deduped={DedupedCount})",
                "ProjectHoursExtended", duplicateGroups, groups.Sum(g => g.Count()), hours.Count);

            if (hours.Count > 0)
            {
                var (inserted, updated, skipped) = await UpsertProjectHoursExtendedAsync(hours);
                result.RecordsInserted = inserted;
                result.RecordsUpdated = updated;
                result.RecordsSkipped = skipped;
                // The API filters FromDate on ReportDate, so the watermark must be a ReportDate too.
                // Storing MAX(LastUpdated) here pushed the watermark past every report of the same day.
                var batchMax = ClampToToday(hours.Max(h => h.ReportDate));
                result.NewWatermark = (batchMax.HasValue && batchMax > result.PreviousWatermark)
                    ? batchMax : result.PreviousWatermark;
                await UpdateWatermarkAsync("ProjectHoursExtended", result.NewWatermark);
            }

            if (isReconciliation)
            {
                result.OrphanCandidates = await CountOrphanCandidatesAsync("ProjectHoursExtended", hours.Select(h => h.ID));
                await TryPurgeOrphansAsync(
                    result,
                    entityName: "ProjectHoursExtended",
                    tableName: "MP_ProjectHoursExtended",
                    reportDateColumn: "ReportDate",
                    fromDate: fromDate,
                    fetchedCount: hours.Count,
                    apiIds: hours.Select(h => h.ID),
                    ct).ConfigureAwait(false);
                await MarkReconciliationCompleteAsync("ProjectHoursExtended");
            }
            await CompleteEntitySyncAsync(result, "ReportDate");
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Failed to sync ProjectHoursExtended");
        }
        return result;
    }

    #endregion

    #region Upsert Methods

    private async Task<(int Inserted, int Updated, int Skipped)> UpsertProjectsAsync(List<ProjectEntity> projects)
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.OpenAsync();

        // Batch SELECT: one query to get all existing IDs + LastUpdated (chunked for IN clause limit)
        var existingMap = await BatchSelectLastUpdatedAsync(connection, "MP_Projects", projects.Select(p => p.ID));

        int inserted = 0, updated = 0, skipped = 0;

        foreach (var project in projects)
        {
            if (!existingMap.TryGetValue(project.ID, out var dbLastUpdated))
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO MP_Projects (ID, Name, ProjectNum, StartDate, EndDate, Description,
                        CustomerName, CustomerID, EmployeeID, EmployeeName, StatusID, StatusName,
                        ProjectTypeID, ProjectType, StudioDepartmentTypeID, StudioDepartmentType,
                        IsActive, FeeSum, LastUpdated)
                    VALUES (@ID, @Name, @ProjectNum, @StartDate, @EndDate, @Description,
                        @CustomerName, @CustomerID, @EmployeeID, @EmployeeName, @StatusID, @StatusName,
                        @ProjectTypeID, @ProjectType, @StudioDepartmentTypeID, @StudioDepartmentType,
                        @IsActive, @FeeSum, @LastUpdated)", project);
                inserted++;
            }
            else if (project.LastUpdated.HasValue && (!dbLastUpdated.HasValue || project.LastUpdated > dbLastUpdated))
            {
                await connection.ExecuteAsync(@"
                    UPDATE MP_Projects SET 
                        Name = @Name, ProjectNum = @ProjectNum, StartDate = @StartDate, EndDate = @EndDate,
                        Description = @Description, CustomerName = @CustomerName, CustomerID = @CustomerID,
                        EmployeeID = @EmployeeID, EmployeeName = @EmployeeName, StatusID = @StatusID,
                        StatusName = @StatusName, ProjectTypeID = @ProjectTypeID, ProjectType = @ProjectType,
                        StudioDepartmentTypeID = @StudioDepartmentTypeID, StudioDepartmentType = @StudioDepartmentType,
                        IsActive = @IsActive, FeeSum = @FeeSum, LastUpdated = @LastUpdated
                    WHERE ID = @ID", project);
                updated++;
            }
            else
            {
                skipped++;
            }
        }

        _logger.LogInformation("Projects: Inserted={Inserted} Updated={Updated} Skipped={Skipped}", inserted, updated, skipped);
        return (inserted, updated, skipped);
    }

    private async Task<(int Inserted, int Updated, int Skipped)> UpsertCompaniesAsync(List<CompanyEntity> companies)
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.OpenAsync();

        var existingMap = await BatchSelectLastUpdatedAsync(connection, "MP_Companies", companies.Select(c => c.ID));

        int inserted = 0, updated = 0, skipped = 0;

        foreach (var company in companies)
        {
            if (!existingMap.TryGetValue(company.ID, out var dbLastUpdated))
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO MP_Companies (ID, Name, Address, City, Email, RegistrationNumber, PhoneNum, LastUpdated)
                    VALUES (@ID, @Name, @Address, @city, @Email, @RegistrationNumber, @PhoneNum, @LastUpdated)", company);
                inserted++;
            }
            else if (company.LastUpdated.HasValue && (!dbLastUpdated.HasValue || company.LastUpdated > dbLastUpdated))
            {
                await connection.ExecuteAsync(@"
                    UPDATE MP_Companies SET 
                        Name = @Name, Address = @Address, City = @city, Email = @Email,
                        RegistrationNumber = @RegistrationNumber, PhoneNum = @PhoneNum, LastUpdated = @LastUpdated
                    WHERE ID = @ID", company);
                updated++;
            }
            else
            {
                skipped++;
            }
        }

        _logger.LogInformation("Companies: Inserted={Inserted} Updated={Updated} Skipped={Skipped}", inserted, updated, skipped);
        return (inserted, updated, skipped);
    }

    private async Task<(int Inserted, int Updated, int Skipped)> UpsertContactsAsync(List<ContactEntity> contacts)
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.OpenAsync();

        var existingMap = await BatchSelectLastUpdatedAsync(connection, "MP_Contacts", contacts.Select(c => c.ID));

        int inserted = 0, updated = 0, skipped = 0;

        foreach (var contact in contacts)
        {
            if (!existingMap.TryGetValue(contact.ID, out var dbLastUpdated))
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO MP_Contacts (ID, FirstName, LastName, CompanyName, CompanyID, Address,
                        Email, Phone, Mobile, LastUpdated)
                    VALUES (@ID, @FirstName, @LastName, @CompanyName, @CompanyID, @Address,
                        @Email, @Phone, @Mobile, @LastUpdated)", contact);
                inserted++;
            }
            else if (contact.LastUpdated.HasValue && (!dbLastUpdated.HasValue || contact.LastUpdated > dbLastUpdated))
            {
                await connection.ExecuteAsync(@"
                    UPDATE MP_Contacts SET 
                        FirstName = @FirstName, LastName = @LastName, CompanyName = @CompanyName,
                        CompanyID = @CompanyID, Address = @Address, Email = @Email,
                        Phone = @Phone, Mobile = @Mobile, LastUpdated = @LastUpdated
                    WHERE ID = @ID", contact);
                updated++;
            }
            else
            {
                skipped++;
            }
        }

        _logger.LogInformation("Contacts: Inserted={Inserted} Updated={Updated} Skipped={Skipped}", inserted, updated, skipped);
        return (inserted, updated, skipped);
    }

    private async Task<(int Inserted, int Updated, int Skipped)> UpsertEmployeesAsync(List<EmployeeEntity> employees)
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.OpenAsync();

        var existingMap = await BatchSelectLastUpdatedAsync(connection, "MP_Employees", employees.Select(e => e.ID));

        int inserted = 0, updated = 0, skipped = 0;

        foreach (var employee in employees)
        {
            if (!existingMap.TryGetValue(employee.ID, out var dbLastUpdated))
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO MP_Employees (ID, FirstName, LastName, LastUpdated)
                    VALUES (@ID, @FirstName, @LastName, @LastUpdated)", employee);
                inserted++;
            }
            else if (employee.LastUpdated.HasValue && (!dbLastUpdated.HasValue || employee.LastUpdated > dbLastUpdated))
            {
                await connection.ExecuteAsync(@"
                    UPDATE MP_Employees SET 
                        FirstName = @FirstName, LastName = @LastName, LastUpdated = @LastUpdated
                    WHERE ID = @ID", employee);
                updated++;
            }
            else
            {
                skipped++;
            }
        }

        _logger.LogInformation("Employees: Inserted={Inserted} Updated={Updated} Skipped={Skipped}", inserted, updated, skipped);
        return (inserted, updated, skipped);
    }

    private async Task<(int Inserted, int Updated, int Skipped)> UpsertBidsAsync(List<BidEntity> bids)
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.OpenAsync();

        var existingMap = await BatchSelectLastUpdatedAsync(connection, "MP_Bids", bids.Select(b => b.ID));

        int inserted = 0, updated = 0, skipped = 0;

        foreach (var bid in bids)
        {
            if (!existingMap.TryGetValue(bid.ID, out var dbLastUpdated))
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO MP_Bids (ID, ProposalNum, Name, ActiveProposal, [DateTime], EstimatedSum,
                        ProbabilityID, ProbabilityName, StatusID, ProposalStatus, LastUpdated)
                    VALUES (@ID, @ProposalNum, @Name, @ActiveProposal, @DateTime, @EstimatedSum,
                        @ProbabilityID, @ProbabilityName, @StatusID, @ProposalStatus, @LastUpdated)", bid);
                inserted++;
            }
            else if (bid.LastUpdated.HasValue && (!dbLastUpdated.HasValue || bid.LastUpdated > dbLastUpdated))
            {
                await connection.ExecuteAsync(@"
                    UPDATE MP_Bids SET 
                        ProposalNum = @ProposalNum, Name = @Name, ActiveProposal = @ActiveProposal,
                        [DateTime] = @DateTime, EstimatedSum = @EstimatedSum, ProbabilityID = @ProbabilityID,
                        ProbabilityName = @ProbabilityName, StatusID = @StatusID, ProposalStatus = @ProposalStatus,
                        LastUpdated = @LastUpdated
                    WHERE ID = @ID", bid);
                updated++;
            }
            else
            {
                skipped++;
            }
        }

        _logger.LogInformation("Bids: Inserted={Inserted} Updated={Updated} Skipped={Skipped}", inserted, updated, skipped);
        return (inserted, updated, skipped);
    }

    private async Task<(int Inserted, int Updated, int Skipped)> UpsertBillsAsync(List<BillEntity> bills)
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.OpenAsync();

        var existingMap = await BatchSelectLastUpdatedAsync(connection, "MP_Bills", bills.Select(b => b.ID));

        int inserted = 0, updated = 0, skipped = 0;

        foreach (var bill in bills)
        {
            if (!existingMap.TryGetValue(bill.ID, out var dbLastUpdated))
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO MP_Bills (ID, BillNum, ProjectName, ProjectID, BillInternalNum, [Sum],
                        SubmitDate, CollectionDate, Status, StatusID, ResponsibleEmployee,
                        ResponsibleEmployeeID, StudioDepartment, StudioDepartmentTypeID, LastUpdated)
                    VALUES (@ID, @BillNum, @ProjectName, @ProjectID, @BillInternalNum, @Sum,
                        @SubmitDate, @CollectionDate, @Status, @StatusID, @ResponsibleEmployee,
                        @ResponsibleEmployeeID, @StudioDepartment, @StudioDepartmentTypeID, @LastUpdated)", bill);
                inserted++;
            }
            else if (bill.LastUpdated.HasValue && (!dbLastUpdated.HasValue || bill.LastUpdated > dbLastUpdated))
            {
                await connection.ExecuteAsync(@"
                    UPDATE MP_Bills SET 
                        BillNum = @BillNum, ProjectName = @ProjectName, ProjectID = @ProjectID,
                        BillInternalNum = @BillInternalNum, [Sum] = @Sum, SubmitDate = @SubmitDate,
                        CollectionDate = @CollectionDate, Status = @Status, StatusID = @StatusID,
                        ResponsibleEmployee = @ResponsibleEmployee, ResponsibleEmployeeID = @ResponsibleEmployeeID,
                        StudioDepartment = @StudioDepartment, StudioDepartmentTypeID = @StudioDepartmentTypeID,
                        LastUpdated = @LastUpdated
                    WHERE ID = @ID", bill);
                updated++;
            }
            else
            {
                skipped++;
            }
        }

        _logger.LogInformation("Bills: Inserted={Inserted} Updated={Updated} Skipped={Skipped}", inserted, updated, skipped);
        return (inserted, updated, skipped);
    }

    private async Task<(int Inserted, int Updated, int Skipped)> UpsertIntakesAsync(List<IntakeEntity> intakes)
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.OpenAsync();

        var existingMap = await BatchSelectLastUpdatedAsync(connection, "MP_Intakes", intakes.Select(i => i.ID));

        int inserted = 0, updated = 0, skipped = 0;

        foreach (var intake in intakes)
        {
            if (!existingMap.TryGetValue(intake.ID, out var dbLastUpdated))
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO MP_Intakes (ID, OpenDate, [Sum], CustomerID, CustomerName, PaymentType,
                        PayTypeID, Description, LastUpdated)
                    VALUES (@ID, @OpenDate, @Sum, @CustomerID, @CustomerName, @PaymentType,
                        @PayTypeID, @Description, @LastUpdated)", intake);
                inserted++;
            }
            else if (intake.LastUpdated.HasValue && (!dbLastUpdated.HasValue || intake.LastUpdated > dbLastUpdated))
            {
                await connection.ExecuteAsync(@"
                    UPDATE MP_Intakes SET 
                        OpenDate = @OpenDate, [Sum] = @Sum, CustomerID = @CustomerID,
                        CustomerName = @CustomerName, PaymentType = @PaymentType, PayTypeID = @PayTypeID,
                        Description = @Description, LastUpdated = @LastUpdated
                    WHERE ID = @ID", intake);
                updated++;
            }
            else
            {
                skipped++;
            }
        }

        _logger.LogInformation("Intakes: Inserted={Inserted} Updated={Updated} Skipped={Skipped}", inserted, updated, skipped);
        return (inserted, updated, skipped);
    }

    private async Task<(int Inserted, int Updated, int Skipped)> UpsertTasksAsync(List<TaskEntity> tasks)
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.OpenAsync();

        var existingMap = await BatchSelectLastUpdatedAsync(connection, "MP_Tasks", tasks.Select(t => t.ID));

        int inserted = 0, updated = 0, skipped = 0;

        foreach (var task in tasks)
        {
            if (!existingMap.TryGetValue(task.ID, out var dbLastUpdated))
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO MP_Tasks (ID, TaskDescription, IsHandled, IsClosed, StartDate, DueDate,
                        SenderName, SenderID, ReceiverName, ReceiverID, CompletionDate, Priority,
                        PriorityID, LastUpdated)
                    VALUES (@ID, @TaskDescription, @IsHandled, @IsClosed, @StartDate, @DueDate,
                        @SenderName, @SenderID, @ReceiverName, @ReceiverID, @CompletionDate, @Priority,
                        @PriorityID, @LastUpdated)", task);
                inserted++;
            }
            else if (task.LastUpdated.HasValue && (!dbLastUpdated.HasValue || task.LastUpdated > dbLastUpdated))
            {
                await connection.ExecuteAsync(@"
                    UPDATE MP_Tasks SET 
                        TaskDescription = @TaskDescription, IsHandled = @IsHandled, IsClosed = @IsClosed,
                        StartDate = @StartDate, DueDate = @DueDate, SenderName = @SenderName,
                        SenderID = @SenderID, ReceiverName = @ReceiverName, ReceiverID = @ReceiverID,
                        CompletionDate = @CompletionDate, Priority = @Priority, PriorityID = @PriorityID,
                        LastUpdated = @LastUpdated
                    WHERE ID = @ID", task);
                updated++;
            }
            else
            {
                skipped++;
            }
        }

        _logger.LogInformation("Tasks: Inserted={Inserted} Updated={Updated} Skipped={Skipped}", inserted, updated, skipped);
        return (inserted, updated, skipped);
    }

    private async Task<(int Inserted, int Updated)> UpsertConversationsAsync(List<ConversationEntity> conversations)
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.OpenAsync();

        var existingIds = await BatchSelectExistingIdsAsync(connection, "MP_Conversations", conversations.Select(c => c.ID));

        int inserted = 0, updated = 0;

        foreach (var conv in conversations)
        {
            if (existingIds.Contains(conv.ID))
            {
                await connection.ExecuteAsync(@"
                    UPDATE MP_Conversations SET 
                        ProjectID = @ProjectID, ProjectName = @ProjectName, ContactID = @ContactID,
                        ContactName = @ContactName, EmployeeID = @EmployeeID, EmployeeName = @EmployeeName,
                        CreatedDate = @CreatedDate, DueDate = @DueDate, Subject = @Subject, Notes = @Notes
                    WHERE ID = @ID", conv);
                updated++;
            }
            else
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO MP_Conversations (ID, ProjectID, ProjectName, ContactID, ContactName,
                        EmployeeID, EmployeeName, CreatedDate, DueDate, Subject, Notes)
                    VALUES (@ID, @ProjectID, @ProjectName, @ContactID, @ContactName,
                        @EmployeeID, @EmployeeName, @CreatedDate, @DueDate, @Subject, @Notes)", conv);
                inserted++;
            }
        }

        _logger.LogInformation("Conversations: {Inserted} inserted, {Updated} updated", inserted, updated);
        return (inserted, updated);
    }

    private async Task<(int Inserted, int Updated)> UpsertProjectHoursAsync(List<ProjectHoursEntity> hours)
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.OpenAsync();

        var existingIds = await BatchSelectExistingIdsAsync(connection, "MP_ProjectHours", hours.Select(h => h.ID));

        int inserted = 0, updated = 0;

        foreach (var hour in hours)
        {
            if (existingIds.Contains(hour.ID))
            {
                await connection.ExecuteAsync(@"
                    UPDATE MP_ProjectHours SET 
                        ProjectID = @ProjectID, ProjectName = @ProjectName, ProjectNumber = @ProjectNumber,
                        EmployeeID = @EmployeeID, EmployeeName = @EmployeeName, ReportDate = @ReportDate,
                        StepName = @StepName, Description = @Description, StartTime = @StartTime,
                        EndTime = @EndTime, TotalHours = @TotalHours
                    WHERE ID = @ID", hour);
                updated++;
            }
            else
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO MP_ProjectHours (ID, ProjectID, ProjectName, ProjectNumber, EmployeeID,
                        EmployeeName, ReportDate, StepName, Description, StartTime, EndTime, TotalHours)
                    VALUES (@ID, @ProjectID, @ProjectName, @ProjectNumber, @EmployeeID,
                        @EmployeeName, @ReportDate, @StepName, @Description, @StartTime, @EndTime, @TotalHours)", hour);
                inserted++;
            }
        }

        _logger.LogInformation("ProjectHours: {Inserted} inserted, {Updated} updated", inserted, updated);
        return (inserted, updated);
    }

    /// <summary>
    /// UPSERT-by-ID sync for TimeHourReports using set-based MERGE:
    /// 1. Normalize Duration in C# (validate + fallback from StartTime/EndTime)
    /// 2. SqlBulkCopy normalized records into #Incoming temp table
    /// 3. MERGE: always UPDATE on match (no LastUpdated to compare), INSERT if new
    /// No bulk DELETE. No N+1 queries. Safe after monthly restore.
    /// </summary>
    private async Task<(int Inserted, int Updated)> UpsertTimeHourReportsAsync(List<TimeHourReportEntity> reports)
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.OpenAsync();

        // Step 1: Normalize Duration in C# before loading to temp table
        var derivedFromTimeRange = 0;
        foreach (var report in reports)
        {
            report.Duration = HoursNormalization.ValidateDecimalHours(report.Duration);
            if (!report.Duration.HasValue || report.Duration.Value == 0m)
            {
                var derived = HoursNormalization.DeriveDecimalHoursFromTimeRange(report.StartTime, report.EndTime);
                if (derived.HasValue)
                {
                    report.Duration = derived;
                    derivedFromTimeRange++;
                }
            }
        }
        if (derivedFromTimeRange > 0)
            _logger.LogInformation("TimeHourReports: derived Duration from StartTime/EndTime for {Count} records", derivedFromTimeRange);

        // Step 2: Create temp table + SqlBulkCopy
        await connection.ExecuteAsync(@"
            CREATE TABLE #Incoming (
                ID INT NOT NULL,
                EmployeeID INT,
                EmployeeName NVARCHAR(500),
                ReportDateTime DATETIME2,
                StartTime TIME,
                EndTime TIME,
                Duration DECIMAL(10,4)
            )");

        var dt = new DataTable();
        dt.Columns.Add("ID", typeof(int));
        dt.Columns.Add("EmployeeID", typeof(int));
        dt.Columns.Add("EmployeeName", typeof(string));
        dt.Columns.Add("ReportDateTime", typeof(DateTime));
        dt.Columns.Add("StartTime", typeof(TimeSpan));
        dt.Columns.Add("EndTime", typeof(TimeSpan));
        dt.Columns.Add("Duration", typeof(decimal));

        foreach (var r in reports)
        {
            dt.Rows.Add(
                r.ID,
                (object?)r.EmployeeID ?? DBNull.Value,
                (object?)r.EmployeeName ?? DBNull.Value,
                (object?)r.ReportDateTime ?? DBNull.Value,
                (object?)r.StartTime ?? DBNull.Value,
                (object?)r.EndTime ?? DBNull.Value,
                (object?)r.Duration ?? DBNull.Value);
        }

        using (var bulkCopy = new SqlBulkCopy(connection))
        {
            bulkCopy.DestinationTableName = "#Incoming";
            await bulkCopy.WriteToServerAsync(dt);
        }

        // Step 3: Set-based MERGE — no LastUpdated, always update on match
        var actions = (await connection.QueryAsync<string>(@"
            MERGE MP_TimeHourReports AS t
            USING #Incoming AS s ON t.ID = s.ID
            WHEN MATCHED THEN
                UPDATE SET t.EmployeeID = s.EmployeeID, t.EmployeeName = s.EmployeeName,
                    t.ReportDateTime = s.ReportDateTime, t.StartTime = s.StartTime,
                    t.EndTime = s.EndTime, t.Duration = s.Duration
            WHEN NOT MATCHED BY TARGET THEN
                INSERT (ID, EmployeeID, EmployeeName, ReportDateTime, StartTime, EndTime, Duration)
                VALUES (s.ID, s.EmployeeID, s.EmployeeName, s.ReportDateTime, s.StartTime, s.EndTime, s.Duration)
            OUTPUT $action;")).ToList();

        await connection.ExecuteAsync("DROP TABLE #Incoming");

        var inserted = actions.Count(a => a == "INSERT");
        var updated = actions.Count(a => a == "UPDATE");

        _logger.LogInformation("TimeHourReports: Inserted={Inserted} Updated={Updated}", inserted, updated);
        return (inserted, updated);
    }

    /// <summary>
    /// UPSERT-by-ID sync for ProjectHoursExtended using set-based MERGE:
    /// 1. Build StepName lookup from existing replica data
    /// 2. Normalize Duration/TotalHours/StepName in C# (before SQL)
    /// 3. SqlBulkCopy normalized records into #Incoming temp table
    /// 4. MERGE: UPDATE when API LastUpdated is newer (and non-null), OR repair null Duration/TotalHours
    ///    when API has a value (repair does not require source LastUpdated). SET uses COALESCE so
    ///    API null never wipes good replica Duration/TotalHours/LastUpdated.
    /// No bulk DELETE. No N+1 queries.
    /// </summary>
    private async Task<(int Inserted, int Updated, int Skipped)> UpsertProjectHoursExtendedAsync(
        List<ProjectHoursExtendedEntity> hours)
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.OpenAsync();

        // Build StepName lookup from existing replica data
        // This preserves StepName values from monthly ETL (which JOINs HoursReportsSteps)
        var stepNameLookup = new Dictionary<int, string>();
        var lookupData = await connection.QueryAsync<dynamic>(
            @"SELECT DISTINCT HoursReportsStepID, StepName 
              FROM MP_ProjectHoursExtended 
              WHERE HoursReportsStepID IS NOT NULL 
                AND StepName IS NOT NULL 
                AND StepName <> ''");
        foreach (var row in lookupData)
        {
            stepNameLookup.TryAdd((int)row.HoursReportsStepID, (string)row.StepName);
        }
        _logger.LogDebug("StepName lookup loaded: {Count} distinct step IDs", stepNameLookup.Count);

        // Step 1: Normalize all records in C# before loading to temp table
        var rejectedDurations = 0;
        var resolvedStepNames = 0;
        var derivedFromTimeRange = 0;
        var loggedSamples = 0;

        foreach (var hour in hours)
        {
            if (loggedSamples < 3)
            {
                _logger.LogInformation(
                    "[DIAG RAW API] ID={ID} Duration={Duration} TotalHours={TotalHours} " +
                    "StartTime={StartTime} EndTime={EndTime} StepName={StepName} " +
                    "HoursReportsStepID={StepID} LastUpdated={LastUpdated}",
                    hour.ID, hour.Duration, hour.TotalHours,
                    hour.StartTime, hour.EndTime, hour.StepName,
                    hour.HoursReportsStepID, hour.LastUpdated);
            }

            var originalDuration = hour.Duration;
            hour.Duration = HoursNormalization.ValidateDecimalHours(hour.Duration);
            if (originalDuration.HasValue && !hour.Duration.HasValue)
            {
                rejectedDurations++;
                _logger.LogDebug("Rejected out-of-range Duration for ID {ID}: raw={Raw} (exceeds 24h)",
                    hour.ID, originalDuration.Value);
            }

            if (!hour.Duration.HasValue)
            {
                var derived = HoursNormalization.DeriveDecimalHoursFromTimeRange(hour.StartTime, hour.EndTime);
                if (derived.HasValue)
                {
                    hour.Duration = derived;
                    derivedFromTimeRange++;
                }
            }

            hour.TotalHours = HoursNormalization.DecimalHoursToTimeSpan(hour.Duration);

            if (string.IsNullOrEmpty(hour.StepName) && hour.HoursReportsStepID.HasValue)
            {
                if (stepNameLookup.TryGetValue(hour.HoursReportsStepID.Value, out var resolvedName))
                {
                    hour.StepName = resolvedName;
                    resolvedStepNames++;
                }
            }

            if (loggedSamples < 3)
            {
                _logger.LogInformation(
                    "[DIAG UPSERT] ID={ID} Duration={Duration} TotalHours={TotalHours} " +
                    "StepName={StepName} HoursReportsStepID={StepID} " +
                    "StartTime={StartTime} EndTime={EndTime}",
                    hour.ID, hour.Duration, hour.TotalHours,
                    hour.StepName, hour.HoursReportsStepID,
                    hour.StartTime, hour.EndTime);
                loggedSamples++;
            }
        }

        if (rejectedDurations > 0)
            _logger.LogWarning("ProjectHoursExtended: {Count} records had out-of-range Duration (>24h), set to NULL", rejectedDurations);
        if (derivedFromTimeRange > 0)
            _logger.LogInformation("ProjectHoursExtended: derived Duration from StartTime/EndTime for {Count} records", derivedFromTimeRange);
        if (resolvedStepNames > 0)
            _logger.LogInformation("ProjectHoursExtended: resolved StepName from lookup for {Count} records", resolvedStepNames);

        // Step 2: Create temp table + SqlBulkCopy
        await connection.ExecuteAsync(@"
            CREATE TABLE #Incoming (
                ID INT NOT NULL,
                EmployeeID INT,
                EmployeeName NVARCHAR(500),
                ProjectID INT,
                ProjectName NVARCHAR(500),
                ProjectNumber NVARCHAR(100),
                SubContractID INT,
                SubContractName NVARCHAR(500),
                SubContractStepID INT,
                SubContractStepName NVARCHAR(500),
                ReportDate DATETIME2,
                StepName NVARCHAR(500),
                HoursReportsStepID INT,
                Description NVARCHAR(MAX),
                StartTime TIME,
                EndTime TIME,
                TotalHours TIME,
                Duration DECIMAL(10,4),
                LastUpdated DATETIME2
            )");

        var dt = new DataTable();
        dt.Columns.Add("ID", typeof(int));
        dt.Columns.Add("EmployeeID", typeof(int));
        dt.Columns.Add("EmployeeName", typeof(string));
        dt.Columns.Add("ProjectID", typeof(int));
        dt.Columns.Add("ProjectName", typeof(string));
        dt.Columns.Add("ProjectNumber", typeof(string));
        dt.Columns.Add("SubContractID", typeof(int));
        dt.Columns.Add("SubContractName", typeof(string));
        dt.Columns.Add("SubContractStepID", typeof(int));
        dt.Columns.Add("SubContractStepName", typeof(string));
        dt.Columns.Add("ReportDate", typeof(DateTime));
        dt.Columns.Add("StepName", typeof(string));
        dt.Columns.Add("HoursReportsStepID", typeof(int));
        dt.Columns.Add("Description", typeof(string));
        dt.Columns.Add("StartTime", typeof(TimeSpan));
        dt.Columns.Add("EndTime", typeof(TimeSpan));
        dt.Columns.Add("TotalHours", typeof(TimeSpan));
        dt.Columns.Add("Duration", typeof(decimal));
        dt.Columns.Add("LastUpdated", typeof(DateTime));

        foreach (var h in hours)
        {
            dt.Rows.Add(
                h.ID,
                (object?)h.EmployeeID ?? DBNull.Value,
                (object?)h.EmployeeName ?? DBNull.Value,
                (object?)h.ProjectID ?? DBNull.Value,
                (object?)h.ProjectName ?? DBNull.Value,
                (object?)h.ProjectNumber ?? DBNull.Value,
                (object?)h.SubContractID ?? DBNull.Value,
                (object?)h.SubContractName ?? DBNull.Value,
                (object?)h.SubContractStepID ?? DBNull.Value,
                (object?)h.SubContractStepName ?? DBNull.Value,
                (object?)h.ReportDate ?? DBNull.Value,
                (object?)h.StepName ?? DBNull.Value,
                (object?)h.HoursReportsStepID ?? DBNull.Value,
                (object?)h.Description ?? DBNull.Value,
                (object?)h.StartTime ?? DBNull.Value,
                (object?)h.EndTime ?? DBNull.Value,
                (object?)h.TotalHours ?? DBNull.Value,
                (object?)h.Duration ?? DBNull.Value,
                (object?)h.LastUpdated ?? DBNull.Value);
        }

        using (var bulkCopy = new SqlBulkCopy(connection))
        {
            bulkCopy.DestinationTableName = "#Incoming";
            await bulkCopy.WriteToServerAsync(dt);
        }

        // Step 3: Set-based MERGE (logic mirrored by ProjectHoursExtendedMergeDecision)
        // UPDATE when: (source LastUpdated non-null AND (target null OR source newer))
        //           OR repair null Duration/TotalHours when source has a value (independent of LastUpdated).
        // COALESCE protects good replica Duration/TotalHours/LastUpdated from API null.
        var actions = (await connection.QueryAsync<string>(@"
            MERGE MP_ProjectHoursExtended AS t
            USING #Incoming AS s ON t.ID = s.ID
            WHEN MATCHED AND (
                    (
                        s.LastUpdated IS NOT NULL
                        AND (
                            t.LastUpdated IS NULL
                            OR s.LastUpdated > t.LastUpdated
                        )
                    )
                    OR (t.Duration IS NULL AND s.Duration IS NOT NULL)
                    OR (t.TotalHours IS NULL AND s.TotalHours IS NOT NULL)
                ) THEN
                UPDATE SET 
                    t.EmployeeID = s.EmployeeID, t.EmployeeName = s.EmployeeName,
                    t.ProjectID = s.ProjectID, t.ProjectName = s.ProjectName,
                    t.ProjectNumber = s.ProjectNumber, t.SubContractID = s.SubContractID,
                    t.SubContractName = s.SubContractName, t.SubContractStepID = s.SubContractStepID,
                    t.SubContractStepName = s.SubContractStepName, t.ReportDate = s.ReportDate,
                    t.StepName = s.StepName, t.HoursReportsStepID = s.HoursReportsStepID,
                    t.Description = s.Description, t.StartTime = s.StartTime, t.EndTime = s.EndTime,
                    t.TotalHours = COALESCE(s.TotalHours, t.TotalHours),
                    t.Duration = COALESCE(s.Duration, t.Duration),
                    t.LastUpdated = COALESCE(s.LastUpdated, t.LastUpdated)
            WHEN NOT MATCHED BY TARGET THEN
                INSERT (ID, EmployeeID, EmployeeName, ProjectID, ProjectName, ProjectNumber,
                    SubContractID, SubContractName, SubContractStepID, SubContractStepName,
                    ReportDate, StepName, HoursReportsStepID, Description, StartTime, EndTime,
                    TotalHours, Duration, LastUpdated)
                VALUES (s.ID, s.EmployeeID, s.EmployeeName, s.ProjectID, s.ProjectName, s.ProjectNumber,
                    s.SubContractID, s.SubContractName, s.SubContractStepID, s.SubContractStepName,
                    s.ReportDate, s.StepName, s.HoursReportsStepID, s.Description, s.StartTime, s.EndTime,
                    s.TotalHours, s.Duration, s.LastUpdated)
            OUTPUT $action;")).ToList();

        await connection.ExecuteAsync("DROP TABLE #Incoming");

        var inserted = actions.Count(a => a == "INSERT");
        var updated = actions.Count(a => a == "UPDATE");
        var skipped = hours.Count - inserted - updated;

        _logger.LogInformation("ProjectHoursExtended: Inserted={Inserted} Updated={Updated} Skipped={Skipped}",
            inserted, updated, skipped);
        return (inserted, updated, skipped);
    }

    #endregion

    #region Batch Query Helpers

    /// <summary>
    /// Batch SELECT existing IDs and their LastUpdated values from a table.
    /// Returns Dictionary&lt;ID, LastUpdated?&gt; — key present = row exists, value = LastUpdated (may be null).
    /// Fixes the N+1 per-record SELECT bug and also fixes ambiguity where ExecuteScalarAsync returns null
    /// for both "row not found" and "row exists with NULL LastUpdated".
    /// Chunked by 2000 for SQL Server IN clause safety.
    /// </summary>
    private static async Task<Dictionary<int, DateTime?>> BatchSelectLastUpdatedAsync(
        SqlConnection connection, string tableName, IEnumerable<int> ids)
    {
        var result = new Dictionary<int, DateTime?>();
        foreach (var chunk in ids.Distinct().Chunk(2000))
        {
            var rows = await connection.QueryAsync<(int ID, DateTime? LastUpdated)>(
                $"SELECT ID, LastUpdated FROM {tableName} WHERE ID IN @Ids",
                new { Ids = chunk.ToList() });
            foreach (var row in rows)
                result[row.ID] = row.LastUpdated;
        }
        return result;
    }

    /// <summary>
    /// Batch SELECT existing IDs from a table (for entities without LastUpdated).
    /// Returns HashSet&lt;int&gt; of existing IDs. Fixes N+1 SELECT COUNT per record.
    /// Chunked by 2000 for SQL Server IN clause safety.
    /// </summary>
    private static async Task<HashSet<int>> BatchSelectExistingIdsAsync(
        SqlConnection connection, string tableName, IEnumerable<int> ids)
    {
        var result = new HashSet<int>();
        foreach (var chunk in ids.Distinct().Chunk(2000))
        {
            var existingIds = await connection.QueryAsync<int>(
                $"SELECT ID FROM {tableName} WHERE ID IN @Ids",
                new { Ids = chunk.ToList() });
            foreach (var id in existingIds)
                result.Add(id);
        }
        return result;
    }

    #endregion

    #region Sync State Management

    private async Task EnsureSyncStateTableAsync()
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.OpenAsync();

        await connection.ExecuteAsync(@"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Sync_State')
            BEGIN
                CREATE TABLE Sync_State (
                    EntityName NVARCHAR(100) PRIMARY KEY,
                    LastWatermark DATETIME2,
                    LastSyncTime DATETIME2,
                    UpdatedAt DATETIME2 DEFAULT GETUTCDATE()
                )
            END

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Sync_RunHistory')
            BEGIN
                CREATE TABLE Sync_RunHistory (
                    ID INT IDENTITY(1,1) PRIMARY KEY,
                    StartTime DATETIME2 NOT NULL,
                    EndTime DATETIME2 NOT NULL,
                    Success BIT NOT NULL,
                    ErrorMessage NVARCHAR(MAX),
                    RecordsSynced INT,
                    Details NVARCHAR(MAX)
                )
            END

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Sync_Lock')
            BEGIN
                CREATE TABLE Sync_Lock (
                    LockName NVARCHAR(100) PRIMARY KEY,
                    AcquiredAt DATETIME2,
                    AcquiredBy NVARCHAR(200)
                )
            END

            -- Ensure the DailySync lock row exists (idempotent)
            IF NOT EXISTS (SELECT 1 FROM Sync_Lock WHERE LockName = 'DailySync')
            BEGIN
                INSERT INTO Sync_Lock (LockName) VALUES ('DailySync')
            END
        ");
    }

    /// <summary>
    /// ANALYSIS MODE: Default watermark for initial full data fetch from Web Service
    /// When Replica is empty (no watermark), use this date to fetch ALL historical data
    /// </summary>
    private static readonly DateTime AnalysisModeDefaultWatermark = new DateTime(2017, 1, 1);

    /// <summary>
    /// Log watermark diagnostics before each entity API call.
    /// Shows: entity name, watermark column, computed fromDate, and the API URL.
    /// </summary>
    private void LogWatermarkDiagnostics(string entity, string watermarkColumn, DateTime? fromDate, string apiFilter)
    {
        _logger.LogInformation(
            "[WATERMARK] Entity={Entity} WatermarkColumn={WatermarkColumn} FromDate={FromDate} ApiFilter={ApiFilter}",
            entity, watermarkColumn, fromDate?.ToString("yyyy-MM-ddTHH:mm:ss") ?? "(null/full-load)", apiFilter);
    }

    /// <summary>
    /// An entity that returns nothing for this many days is reported as stale.
    /// </summary>
    private const int StaleEntityWarningDays = 14;

    /// <summary>
    /// Close a successful entity sync: stamp the freshness marker, log the counts, and warn when the
    /// entity has been returning nothing for a long time.
    /// The stamp is written even for an empty batch, so that "did not run" and "ran and found
    /// nothing" stop looking identical in <c>Sync_State</c>. See docs/MASTERPLAN_SYNC_WATERMARKS.md §3.5.
    /// </summary>
    private async Task CompleteEntitySyncAsync(EntitySyncResult result, string watermarkColumn)
    {
        await TouchSyncStateAsync(result.EntityName).ConfigureAwait(false);

        _logger.LogInformation(
            "[SYNC COMPLETE] Entity={Entity} Fetched={Fetched} Inserted={Inserted} Updated={Updated} Skipped={Skipped} " +
            "PrevWatermark={PrevWatermark} NewWatermark={NewWatermark} WatermarkColumn={WatermarkColumn}",
            result.EntityName, result.RecordsFetched, result.RecordsInserted, result.RecordsUpdated, result.RecordsSkipped,
            result.PreviousWatermark?.ToString("yyyy-MM-ddTHH:mm:ss") ?? "(none)",
            result.NewWatermark?.ToString("yyyy-MM-ddTHH:mm:ss") ?? "(unchanged)",
            watermarkColumn);

        if (result.RecordsFetched == 0
            && result.PreviousWatermark.HasValue
            && result.PreviousWatermark.Value < DateTime.UtcNow.AddDays(-StaleEntityWarningDays))
        {
            _logger.LogWarning(
                "[STALE] {Entity}: API returned no rows and {WatermarkColumn} watermark has not moved since {Watermark:yyyy-MM-dd} " +
                "({Days} days). Either the entity is genuinely idle or the endpoint stopped returning data.",
                result.EntityName, watermarkColumn, result.PreviousWatermark.Value,
                (int)(DateTime.UtcNow - result.PreviousWatermark.Value).TotalDays);
        }
    }

    /// <summary>
    /// Record that the entity completed a pass, without touching its watermark.
    /// </summary>
    private async Task TouchSyncStateAsync(string entityName)
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.ExecuteAsync(@"
            MERGE Sync_State AS target
            USING (SELECT @EntityName AS EntityName) AS source
            ON target.EntityName = source.EntityName
            WHEN MATCHED THEN
                UPDATE SET LastSyncTime = GETUTCDATE(), UpdatedAt = GETUTCDATE()
            WHEN NOT MATCHED THEN
                INSERT (EntityName, LastWatermark, LastSyncTime, UpdatedAt)
                VALUES (@EntityName, NULL, GETUTCDATE(), GETUTCDATE());",
            new { EntityName = entityName }).ConfigureAwait(false);
    }

    /// <summary>
    /// Build the date filter for a back-datable hours entity.
    /// Every request reaches <see cref="HoursSyncOptions.LookbackDays"/> further back than the stored
    /// watermark so that late and retroactive reports are picked up, and once every
    /// <see cref="HoursSyncOptions.ReconcileIntervalDays"/> days the filter is dropped entirely.
    /// See docs/MASTERPLAN_SYNC_WATERMARKS.md.
    /// </summary>
    private async Task<(DateTime? FromDate, bool IsReconciliation)> ResolveHoursFromDateAsync(string entityName, DateTime? watermark)
    {
        if (await IsReconciliationDueAsync(entityName).ConfigureAwait(false))
        {
            _logger.LogWarning(
                "[RECONCILE] {Entity}: full unfiltered pass (interval {IntervalDays}d, forced={Forced})",
                entityName, _hoursOptions.ReconcileIntervalDays, _hoursOptions.ForceReconcile);
            return (null, true);
        }

        var fromDate = (watermark ?? AnalysisModeDefaultWatermark).AddDays(-_hoursOptions.LookbackDays);
        return (fromDate, false);
    }

    private async Task<bool> IsReconciliationDueAsync(string entityName)
    {
        if (_hoursOptions.SkipReconcile)
            return false;
        if (_hoursOptions.ForceReconcile)
            return true;

        await using var connection = new SqlConnection(_replicaConnectionString);
        var lastRun = await connection.ExecuteScalarAsync<DateTime?>(
            "SELECT LastSyncTime FROM Sync_State WHERE EntityName = @EntityName",
            new { EntityName = entityName + ReconcileStateSuffix }).ConfigureAwait(false);

        return !lastRun.HasValue
            || lastRun.Value <= DateTime.UtcNow.AddDays(-_hoursOptions.ReconcileIntervalDays);
    }

    /// <summary>
    /// Records the reconciliation timestamp in its own <c>Sync_State</c> row. LastWatermark stays
    /// NULL so the row cannot be mistaken for an entity watermark by <see cref="IsInitialLoadAsync"/>.
    /// </summary>
    private async Task MarkReconciliationCompleteAsync(string entityName)
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.ExecuteAsync(@"
            MERGE Sync_State AS target
            USING (SELECT @EntityName AS EntityName) AS source
            ON target.EntityName = source.EntityName
            WHEN MATCHED THEN
                UPDATE SET LastSyncTime = GETUTCDATE(), UpdatedAt = GETUTCDATE()
            WHEN NOT MATCHED THEN
                INSERT (EntityName, LastWatermark, LastSyncTime, UpdatedAt)
                VALUES (@EntityName, NULL, GETUTCDATE(), GETUTCDATE());",
            new { EntityName = entityName + ReconcileStateSuffix }).ConfigureAwait(false);
    }

    /// <summary>
    /// After an unfiltered pass, report replica rows the API no longer returns, then
    /// DEV-025 purge (JSON archive + DELETE) via <see cref="TryPurgeOrphansAsync"/>.
    /// </summary>
    private async Task<int> CountOrphanCandidatesAsync(string entityName, IEnumerable<int> apiIds)
    {
        if (!EntityConfig.TryGetValue(entityName, out var config))
            throw new ArgumentException($"No entity configuration for '{entityName}'.", nameof(entityName));

        var apiIdSet = apiIds.ToHashSet();

        await using var connection = new SqlConnection(_replicaConnectionString);
        var dbIds = await connection.QueryAsync<int>($"SELECT ID FROM {config.TableName}").ConfigureAwait(false);
        var orphans = dbIds.Where(id => !apiIdSet.Contains(id)).ToList();

        if (orphans.Count > 0)
        {
            _logger.LogWarning(
                "[RECONCILE] {Entity}: {OrphanCount} row(s) in {Table} were not returned by the API. " +
                "DEV-025 purge follows when gates pass (JSON archive then DELETE). Sample IDs: {SampleIds}",
                entityName, orphans.Count, config.TableName, string.Join(", ", orphans.Take(20)));
        }
        else
        {
            _logger.LogInformation("[RECONCILE] {Entity}: replica matches the API, no orphan rows.", entityName);
        }

        return orphans.Count;
    }

    /// <summary>
    /// DEV-025: evaluate remaining fail-closed gates and DELETE orphans for PH / PHE
    /// after writing the JSON archive.
    /// </summary>
    private async Task TryPurgeOrphansAsync(
        EntitySyncResult result,
        string entityName,
        string tableName,
        string reportDateColumn,
        DateTime? fromDate,
        int fetchedCount,
        IEnumerable<int> apiIds,
        CancellationToken cancellationToken)
    {
        var runner = new OrphanPurgeRunner(
            _replicaConnectionString,
            _hoursOptions.OrphanPurge,
            _logger);

        var purgeResult = await runner.RunAsync(
            entityName,
            tableName,
            reportDateColumn,
            isFullReconcile: true,
            fromDate,
            fetchedCount,
            apiIds.ToList(),
            cancellationToken).ConfigureAwait(false);

        result.OrphanPurged = purgeResult.PurgedCount;
        result.OrphanDeferred = purgeResult.DeferredCount;
        result.OrphanPurgeBlockedReason = purgeResult.BlockReason;
        if (purgeResult.OrphanCount > result.OrphanCandidates)
        {
            result.OrphanCandidates = purgeResult.OrphanCount;
        }
    }

    /// <summary>
    /// A single future-dated report must not push the watermark past today — everything between
    /// would then be skipped permanently (observed 2026-07-07, see docs/MASTERPLAN_SYNC_WATERMARKS.md).
    /// </summary>
    private static DateTime? ClampToToday(DateTime? candidate)
    {
        if (!candidate.HasValue)
            return null;

        var endOfToday = DateTime.Today.AddDays(1).AddTicks(-1);
        return candidate.Value > endOfToday ? endOfToday : candidate.Value;
    }

    private async Task<DateTime?> GetWatermarkAsync(string entityName)
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        var watermark = await connection.ExecuteScalarAsync<DateTime?>(
            "SELECT LastWatermark FROM Sync_State WHERE EntityName = @EntityName",
            new { EntityName = entityName });

        // ANALYSIS MODE: If no watermark exists, use 2017-01-01 to fetch full dataset
        if (!watermark.HasValue)
        {
            _logger.LogInformation(
                "[ANALYSIS MODE] No watermark for {Entity} - using default date {DefaultDate:yyyy-MM-dd} to fetch full dataset",
                entityName, AnalysisModeDefaultWatermark);
            return AnalysisModeDefaultWatermark;
        }

        return watermark;
    }

    private async Task UpdateWatermarkAsync(string entityName, DateTime? watermark)
    {
        if (!watermark.HasValue) return;

        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.ExecuteAsync(@"
            MERGE Sync_State AS target
            USING (SELECT @EntityName AS EntityName) AS source
            ON target.EntityName = source.EntityName
            WHEN MATCHED THEN
                UPDATE SET LastWatermark = @Watermark, LastSyncTime = GETUTCDATE(), UpdatedAt = GETUTCDATE()
            WHEN NOT MATCHED THEN
                INSERT (EntityName, LastWatermark, LastSyncTime, UpdatedAt)
                VALUES (@EntityName, @Watermark, GETUTCDATE(), GETUTCDATE());",
            new { EntityName = entityName, Watermark = watermark.Value });
    }

    private async Task<bool> TryAcquireLockAsync()
    {
        if (_lockConnection is not null)
            throw new InvalidOperationException("The daily-sync application lock is already held by this service instance.");

        var connection = new SqlConnection(_replicaConnectionString);
        try
        {
            await connection.OpenAsync().ConfigureAwait(false);
            var result = await connection.ExecuteScalarAsync<int>(
                """
                DECLARE @result int;
                EXEC @result = sp_getapplock
                    @Resource = 'SiNetDailySync',
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Session',
                    @LockTimeout = 0;
                SELECT @result;
                """).ConfigureAwait(false);

            if (result < 0)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                return false;
            }

            _lockConnection = connection;
            return true;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task ReleaseLockAsync()
    {
        var connection = Interlocked.Exchange(ref _lockConnection, null);
        if (connection is null)
            return;

        try
        {
            await connection.ExecuteAsync(
                """
                EXEC sp_releaseapplock
                    @Resource = 'SiNetDailySync',
                    @LockOwner = 'Session';
                """).ConfigureAwait(false);
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task RecordRunHistoryAsync(DailySyncResult result)
    {
        await using var connection = new SqlConnection(_replicaConnectionString);

        var totalRecords = result.EntityResults.Values.Sum(r => r.RecordsInserted + r.RecordsUpdated);
        var details = System.Text.Json.JsonSerializer.Serialize(result.EntityResults);

        // SQL Server datetime type has minimum value of 1753-01-01
        // Clamp DateTime values to prevent SqlDateTime overflow
        var minSqlDateTime = new DateTime(1753, 1, 1);
        var startTime = result.StartTime < minSqlDateTime ? minSqlDateTime : result.StartTime;
        var endTime = result.EndTime < minSqlDateTime ? minSqlDateTime : result.EndTime;

        // If end time is not set (default), use current time
        if (endTime == default || endTime < minSqlDateTime)
        {
            endTime = DateTime.UtcNow;
        }

        await connection.ExecuteAsync(@"
            INSERT INTO Sync_RunHistory (StartTime, EndTime, Success, ErrorMessage, RecordsSynced, Details)
            VALUES (@StartTime, @EndTime, @Success, @ErrorMessage, @RecordsSynced, @Details)",
            new
            {
                StartTime = startTime,
                EndTime = endTime,
                result.Success,
                result.ErrorMessage,
                RecordsSynced = totalRecords,
                Details = details
            });
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════════════════════
    // CROSS-SYNC: Push mapped MasterPlan data into SiNet Company/Contact tables
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For every SiNet Company/Contact that has a MasterPlan mapping ID,
    /// reads the corresponding MP_Companies/MP_Contacts row from Replica
    /// and updates the SiNet fields with the latest MasterPlan values.
    /// 
    /// Field mapping:
    ///   Company: MP Name→Title, Email→Email, PhoneNum→WorkPhone, Address→WorkAddress, city→WorkCity
    ///   Contact: MP FirstName→FirstName, FirstName+LastName→FullName, Email→Email,
    ///            Phone→WorkPhone, Mobile→CellPhone
    /// </summary>
    private async Task<EntitySyncResult> CrossSyncToSiNetAsync(CancellationToken ct)
    {
        var result = new EntitySyncResult { EntityName = "CrossSync" };

        if (string.IsNullOrEmpty(_siDataConnectionString))
        {
            _logger.LogWarning("[CrossSync] SiData connection string not configured — skipping");
            return result;
        }

        try
        {
            int companiesUpdated = 0, contactsUpdated = 0;

            await using var siConn = new SqlConnection(_siDataConnectionString);
            await siConn.OpenAsync(ct);

            await using var replicaConn = new SqlConnection(_replicaConnectionString);
            await replicaConn.OpenAsync(ct);

            // ── Companies ──
            // Get all SiNet companies that have a MasterPlan mapping
            var mappedCompanies = await siConn.QueryAsync<(int Id, int MpId)>(
                "SELECT ID, MasterPlanCompanyId FROM Company WHERE MasterPlanCompanyId IS NOT NULL AND MasterPlanSync = 1");

            if (mappedCompanies.Any())
            {
                var mpIds = mappedCompanies.Select(c => c.MpId).ToList();

                // Fetch corresponding MP records from Replica
                var mpCompanies = (await replicaConn.QueryAsync<dynamic>(
                    "SELECT ID, Name, Email, PhoneNum, Address, City, RegistrationNumber FROM MP_Companies WHERE ID IN @Ids",
                    new { Ids = mpIds }))
                    .ToDictionary(r => (int)r.ID);

                foreach (var (siId, mpId) in mappedCompanies)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!mpCompanies.TryGetValue(mpId, out var mp)) continue;

                    var rowsAffected = await siConn.ExecuteAsync(@"
                        UPDATE Company SET
                            Title              = @Title,
                            Email              = @Email,
                            WorkPhone          = @WorkPhone,
                            WorkAddress        = @WorkAddress,
                            WorkCity           = @WorkCity,
                            RegistrationNumber = @RegistrationNumber,
                            Modified           = GETDATE()
                        WHERE ID = @Id
                          AND (ISNULL(Title,'')              != ISNULL(@Title,'')
                            OR ISNULL(Email,'')              != ISNULL(@Email,'')
                            OR ISNULL(WorkPhone,'')          != ISNULL(@WorkPhone,'')
                            OR ISNULL(WorkAddress,'')        != ISNULL(@WorkAddress,'')
                            OR ISNULL(WorkCity,'')           != ISNULL(@WorkCity,'')
                            OR ISNULL(RegistrationNumber,'') != ISNULL(@RegistrationNumber,''))",
                        new
                        {
                            Id = siId,
                            Title = (string?)mp.Name,
                            Email = (string?)mp.Email,
                            WorkPhone = (string?)mp.PhoneNum,
                            WorkAddress = (string?)mp.Address,
                            WorkCity = (string?)mp.City,
                            RegistrationNumber = (string?)mp.RegistrationNumber
                        });

                    if (rowsAffected > 0) companiesUpdated++;
                }
            }

            // ── Contacts ──
            var mappedContacts = await siConn.QueryAsync<(int Id, int MpId)>(
                "SELECT ID, MasterPlanContactId FROM Contacts WHERE MasterPlanContactId IS NOT NULL AND MasterPlanSync = 1");

            if (mappedContacts.Any())
            {
                var mpIds = mappedContacts.Select(c => c.MpId).ToList();

                var mpContacts = (await replicaConn.QueryAsync<dynamic>(
                    "SELECT ID, FirstName, LastName, Email, Phone, Mobile FROM MP_Contacts WHERE ID IN @Ids",
                    new { Ids = mpIds }))
                    .ToDictionary(r => (int)r.ID);

                foreach (var (siId, mpId) in mappedContacts)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!mpContacts.TryGetValue(mpId, out var mp)) continue;

                    string? firstName = (string?)mp.FirstName;
                    string? lastName = (string?)mp.LastName;
                    string? fullName = string.IsNullOrWhiteSpace(lastName)
                        ? firstName
                        : $"{firstName} {lastName}".Trim();

                    var rowsAffected = await siConn.ExecuteAsync(@"
                        UPDATE Contacts SET
                            FirstName = @FirstName,
                            FullName  = @FullName,
                            Email     = @Email,
                            WorkPhone = @WorkPhone,
                            CellPhone = @CellPhone,
                            Modified  = GETDATE()
                        WHERE ID = @Id
                          AND (ISNULL(FirstName,'') != ISNULL(@FirstName,'')
                            OR ISNULL(FullName,'')  != ISNULL(@FullName,'')
                            OR ISNULL(Email,'')     != ISNULL(@Email,'')
                            OR ISNULL(WorkPhone,'') != ISNULL(@WorkPhone,'')
                            OR ISNULL(CellPhone,'') != ISNULL(@CellPhone,''))",
                        new
                        {
                            Id = siId,
                            FirstName = firstName,
                            FullName = fullName,
                            Email = (string?)mp.Email,
                            WorkPhone = (string?)mp.Phone,
                            CellPhone = (string?)mp.Mobile
                        });

                    if (rowsAffected > 0) contactsUpdated++;
                }
            }

            result.RecordsUpdated = companiesUpdated + contactsUpdated;
            _logger.LogInformation("[CrossSync] Companies updated={CompaniesUpdated}, Contacts updated={ContactsUpdated}",
                companiesUpdated, contactsUpdated);
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "[CrossSync] Failed to cross-sync MP → SiNet");
        }

        return result;
    }
}
