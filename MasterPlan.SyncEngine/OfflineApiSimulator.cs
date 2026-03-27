using System.Text.Json;
using MasterPlan.SyncEngine.Models;
using Microsoft.Extensions.Logging;

namespace MasterPlan.SyncEngine;

/// <summary>
/// Offline API Simulator - Reads from local dump files instead of calling the live API.
/// Used for testing the full sync pipeline without consuming API requests.
/// 
/// Dump folder structure:
///   {DumpFolder}/
///     Projects.ndjson
///     Companies.ndjson
///     Contacts.ndjson
///     Employees.ndjson
///     Bids.ndjson
///     Bills.ndjson
///     Intakes.ndjson
///     Tasks.ndjson
///     Conversations.ndjson
///     ProjectHours.ndjson
/// </summary>
public class OfflineApiSimulator : IDisposable
{
    private readonly string _dumpFolderPath;
    private readonly ILogger<OfflineApiSimulator> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    // Entity to filename mapping
    private static readonly Dictionary<string, string> EntityFileNames = new()
    {
        ["Projects"] = "Projects.ndjson",
        ["Companies"] = "Companies.ndjson",
        ["Contacts"] = "Contacts.ndjson",
        ["Employees"] = "Employees.ndjson",
        ["Bids"] = "Bids.ndjson",
        ["Bills"] = "Bills.ndjson",
        ["Intakes"] = "Intakes.ndjson",
        ["Tasks"] = "Tasks.ndjson",
        ["Conversations"] = "Conversations.ndjson",
        ["ProjectHours"] = "ProjectHours.ndjson"
    };

    public OfflineApiSimulator(string dumpFolderPath, ILogger<OfflineApiSimulator> logger)
    {
        _dumpFolderPath = dumpFolderPath ?? throw new ArgumentNullException(nameof(dumpFolderPath));
        _logger = logger;

        if (!Directory.Exists(_dumpFolderPath))
        {
            throw new DirectoryNotFoundException($"Dump folder not found: {_dumpFolderPath}");
        }

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new TimeSpanHHmmConverter() }
        };

        _logger.LogInformation("OfflineApiSimulator initialized with dump folder: {Path}", _dumpFolderPath);
    }

    /// <summary>
    /// Loads entities from NDJSON file, optionally filtering by lastUpdated watermark.
    /// </summary>
    private async Task<List<T>> LoadEntitiesAsync<T>(string entityName, DateTime? lastUpdated = null, Func<T, DateTime?>? getLastUpdated = null)
    {
        if (!EntityFileNames.TryGetValue(entityName, out var fileName))
        {
            throw new ArgumentException($"Unknown entity: {entityName}");
        }

        var filePath = Path.Combine(_dumpFolderPath, fileName);
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("NDJSON file not found for {Entity}: {Path}", entityName, filePath);
            return new List<T>();
        }

        var entities = new List<T>();
        var lines = await File.ReadAllLinesAsync(filePath);
        var parseErrors = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var entity = JsonSerializer.Deserialize<T>(line, _jsonOptions);
                if (entity != null)
                {
                    // Apply watermark filter if provided
                    if (lastUpdated.HasValue && getLastUpdated != null)
                    {
                        var entityDate = getLastUpdated(entity);
                        if (entityDate.HasValue && entityDate.Value <= lastUpdated.Value)
                        {
                            continue; // Skip records older than or equal to watermark
                        }
                    }
                    entities.Add(entity);
                }
            }
            catch (JsonException ex)
            {
                parseErrors++;
                if (parseErrors <= 3)
                {
                    _logger.LogWarning("JSON parse error in {Entity}: {Error}", entityName, ex.Message);
                }
            }
        }

        if (parseErrors > 3)
        {
            _logger.LogWarning("Total of {Count} parse errors in {Entity}", parseErrors, entityName);
        }

        _logger.LogInformation("[OFFLINE] Loaded {Count} {Entity} records from dump (filter: {Filter})", 
            entities.Count, entityName, lastUpdated?.ToString("yyyy-MM-dd HH:mm:ss") ?? "none");

        return entities;
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Entity-specific methods matching MasterPlanApiClient interface
    // ═══════════════════════════════════════════════════════════════════════════════

    public async Task<List<ProjectEntity>> GetProjectsAsync(DateTime? lastUpdated = null, CancellationToken ct = default)
    {
        return await LoadEntitiesAsync<ProjectEntity>("Projects", lastUpdated, e => e.LastUpdated);
    }

    public async Task<List<CompanyEntity>> GetCompaniesAsync(DateTime? lastUpdated = null, CancellationToken ct = default)
    {
        return await LoadEntitiesAsync<CompanyEntity>("Companies", lastUpdated, e => e.LastUpdated);
    }

    public async Task<List<ContactEntity>> GetContactsAsync(DateTime? lastUpdated = null, CancellationToken ct = default)
    {
        return await LoadEntitiesAsync<ContactEntity>("Contacts", lastUpdated, e => e.LastUpdated);
    }

    public async Task<List<EmployeeEntity>> GetEmployeesAsync(DateTime? lastUpdated = null, CancellationToken ct = default)
    {
        return await LoadEntitiesAsync<EmployeeEntity>("Employees", lastUpdated, e => e.LastUpdated);
    }

    public async Task<List<BidEntity>> GetBidsAsync(DateTime? lastUpdated = null, CancellationToken ct = default)
    {
        return await LoadEntitiesAsync<BidEntity>("Bids", lastUpdated, e => e.LastUpdated);
    }

    public async Task<List<BillEntity>> GetBillsAsync(DateTime? lastUpdated = null, CancellationToken ct = default)
    {
        return await LoadEntitiesAsync<BillEntity>("Bills", lastUpdated, e => e.LastUpdated);
    }

    public async Task<List<IntakeEntity>> GetIntakesAsync(DateTime? lastUpdated = null, CancellationToken ct = default)
    {
        return await LoadEntitiesAsync<IntakeEntity>("Intakes", lastUpdated, e => e.LastUpdated);
    }

    public async Task<List<TaskEntity>> GetTasksAsync(DateTime? lastUpdated = null, CancellationToken ct = default)
    {
        return await LoadEntitiesAsync<TaskEntity>("Tasks", lastUpdated, e => e.LastUpdated);
    }

    public async Task<List<ConversationEntity>> GetConversationsAsync(DateTime? createdDate = null, CancellationToken ct = default)
    {
        return await LoadEntitiesAsync<ConversationEntity>("Conversations", createdDate, e => e.CreatedDate);
    }

    public async Task<List<ProjectHoursEntity>> GetProjectHoursAsync(DateTime? fromDate = null, CancellationToken ct = default)
    {
        return await LoadEntitiesAsync<ProjectHoursEntity>("ProjectHours", fromDate, e => e.ReportDate);
    }

    /// <summary>
    /// Validates that all required dump files exist.
    /// Returns a dictionary of entity name to (success, message).
    /// </summary>
    public async Task<Dictionary<string, (bool Success, int StatusCode, string Message)>> ValidateAllEndpointsAsync(CancellationToken ct = default)
    {
        var results = new Dictionary<string, (bool Success, int StatusCode, string Message)>();

        foreach (var (entity, fileName) in EntityFileNames)
        {
            var filePath = Path.Combine(_dumpFolderPath, fileName);
            if (File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);
                var lineCount = (await File.ReadAllLinesAsync(filePath, ct)).Length;
                results[entity] = (true, 200, $"OK - {lineCount} records, {fileInfo.Length / 1024}KB");
            }
            else
            {
                results[entity] = (false, 404, $"File not found: {fileName}");
            }
        }

        return results;
    }

    /// <summary>
    /// Returns summary statistics about the dump folder.
    /// </summary>
    public async Task<DumpFolderStats> GetDumpStatsAsync()
    {
        var stats = new DumpFolderStats
        {
            FolderPath = _dumpFolderPath,
            FolderName = Path.GetFileName(_dumpFolderPath)
        };

        foreach (var (entity, fileName) in EntityFileNames)
        {
            var filePath = Path.Combine(_dumpFolderPath, fileName);
            if (File.Exists(filePath))
            {
                var lines = await File.ReadAllLinesAsync(filePath);
                var nonEmptyLines = lines.Count(l => !string.IsNullOrWhiteSpace(l));
                stats.EntityCounts[entity] = nonEmptyLines;
            }
            else
            {
                stats.EntityCounts[entity] = 0;
            }
        }

        stats.TotalRecords = stats.EntityCounts.Values.Sum();
        return stats;
    }

    public void Dispose()
    {
        // No resources to dispose for file-based simulator
    }
}

/// <summary>
/// Statistics about a dump folder.
/// </summary>
public class DumpFolderStats
{
    public string FolderPath { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public Dictionary<string, int> EntityCounts { get; set; } = new();
    public int TotalRecords { get; set; }
}
