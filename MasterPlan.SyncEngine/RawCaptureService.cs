using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Dapper;

namespace MasterPlan.SyncEngine;

/// <summary>
/// Metadata about an API capture operation
/// </summary>
public class CaptureMetadata
{
    public string EntityName { get; set; } = string.Empty;
    public string EndpointUrl { get; set; } = string.Empty;
    public string? RequestParams { get; set; }
    public DateTime CaptureTimestamp { get; set; }
    public int StatusCode { get; set; }
    public string? ContentType { get; set; }
    public long ResponseSizeBytes { get; set; }
    public int RecordCount { get; set; }
    public string? FilterUsed { get; set; }
    public bool WasWrappedResponse { get; set; }
    public string? WrapperPropertyName { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Schema mismatch report for an entity
/// </summary>
public class SchemaMismatchReport
{
    public string EntityName { get; set; } = string.Empty;
    public string TargetTable { get; set; } = string.Empty;
    public DateTime CheckTimestamp { get; set; }
    public bool TableExists { get; set; }
    public List<string> MissingColumns { get; set; } = new();
    public List<string> UnexpectedApiFields { get; set; } = new();
    public List<string> MatchedColumns { get; set; } = new();
    public Dictionary<string, object?> SamplePayload { get; set; } = new();
}

/// <summary>
/// Service for capturing raw API responses to disk for offline analysis.
/// 
/// This is a temporary debugging/validation tool to:
/// - Preserve raw API data without re-calling the rate-limited API
/// - Validate schema mappings before SQL inserts
/// - Diagnose transformation issues
/// 
/// Output location: D:\file\MasterPlanApiDump\{timestamp}\
/// </summary>
public class RawCaptureService
{
    private readonly string _baseOutputPath;
    private readonly string _sessionPath;
    private readonly ILogger<RawCaptureService> _logger;
    private readonly string _replicaConnectionString;
    private readonly JsonSerializerOptions _jsonOptions;

    // Table name mappings for schema validation
    private static readonly Dictionary<string, string> EntityTableMap = new()
    {
        ["Projects"] = "MP_Projects",
        ["Companies"] = "MP_Companies",
        ["Contacts"] = "MP_Contacts",
        ["Employees"] = "MP_Employees",
        ["Bids"] = "MP_Bids",
        ["Bills"] = "MP_Bills",
        ["Intakes"] = "MP_Intakes",
        ["Tasks"] = "MP_Tasks",
        ["Conversations"] = "MP_Conversations",
        ["ProjectHours"] = "MP_ProjectHours"
    };

    public string SessionPath => _sessionPath;

    public RawCaptureService(
        string replicaConnectionString,
        ILogger<RawCaptureService> logger,
        string? baseOutputPath = null)
    {
        _replicaConnectionString = replicaConnectionString;
        _logger = logger;
        _baseOutputPath = baseOutputPath ?? @"D:\file\MasterPlanApiDump";
        
        // Create session folder with timestamp
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _sessionPath = Path.Combine(_baseOutputPath, timestamp);
        
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        // Ensure output directory exists
        Directory.CreateDirectory(_sessionPath);
        _logger.LogInformation("Raw Capture Mode enabled. Output path: {Path}", _sessionPath);
        Console.WriteLine($"[RAW CAPTURE] Session folder: {_sessionPath}");
    }

    /// <summary>
    /// Capture raw API response to disk (before deserialization)
    /// </summary>
    public async Task CaptureRawResponseAsync(
        string entityName,
        string endpointUrl,
        string? requestParams,
        int statusCode,
        string? contentType,
        string rawBody,
        string? filterUsed = null)
    {
        try
        {
            var metadata = new CaptureMetadata
            {
                EntityName = entityName,
                EndpointUrl = endpointUrl,
                RequestParams = requestParams,
                CaptureTimestamp = DateTime.UtcNow,
                StatusCode = statusCode,
                ContentType = contentType,
                ResponseSizeBytes = rawBody.Length,
                FilterUsed = filterUsed
            };

            // Always save the raw response body
            var rawFilePath = Path.Combine(_sessionPath, $"{entityName}.raw.json");
            await File.WriteAllTextAsync(rawFilePath, rawBody);
            _logger.LogDebug("Saved raw response to {Path} ({Size} bytes)", rawFilePath, rawBody.Length);

            // Try to parse and save as NDJSON if it's valid JSON
            await TryConvertToNdjsonAsync(entityName, rawBody, metadata);

            // Save metadata
            var metaFilePath = Path.Combine(_sessionPath, $"{entityName}.meta.json");
            var metaJson = JsonSerializer.Serialize(metadata, _jsonOptions);
            await File.WriteAllTextAsync(metaFilePath, metaJson);

            Console.WriteLine($"    [CAPTURE] {entityName}: {metadata.RecordCount} records saved to {_sessionPath}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to capture raw response for {Entity}", entityName);
            Console.WriteLine($"    [CAPTURE] ERROR saving {entityName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Try to convert JSON response to NDJSON format (one JSON object per line)
    /// </summary>
    private async Task TryConvertToNdjsonAsync(string entityName, string rawBody, CaptureMetadata metadata)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;
            var items = new List<JsonElement>();

            if (root.ValueKind == JsonValueKind.Array)
            {
                // Direct array response
                foreach (var item in root.EnumerateArray())
                {
                    items.Add(item);
                }
                metadata.WasWrappedResponse = false;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                // Check for wrapped response
                foreach (var propName in new[] { "data", "Data", "items", "Items", "results", "Results", "value", "Value" })
                {
                    if (root.TryGetProperty(propName, out var arrayProp) && arrayProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in arrayProp.EnumerateArray())
                        {
                            items.Add(item);
                        }
                        metadata.WasWrappedResponse = true;
                        metadata.WrapperPropertyName = propName;
                        break;
                    }
                }

                // If no array found but it's a single object, treat as one item
                if (items.Count == 0 && root.ValueKind == JsonValueKind.Object)
                {
                    items.Add(root);
                    metadata.WasWrappedResponse = false;
                }
            }

            metadata.RecordCount = items.Count;

            // Write NDJSON file
            if (items.Count > 0)
            {
                var ndjsonPath = Path.Combine(_sessionPath, $"{entityName}.ndjson");
                await using var writer = new StreamWriter(ndjsonPath);
                foreach (var item in items)
                {
                    await writer.WriteLineAsync(item.GetRawText());
                }
                _logger.LogDebug("Saved {Count} records to NDJSON: {Path}", items.Count, ndjsonPath);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Could not parse {Entity} response as JSON for NDJSON conversion", entityName);
            metadata.ErrorMessage = $"JSON parse error: {ex.Message}";
        }
    }

    /// <summary>
    /// Validate schema mapping before SQL insert and generate mismatch report
    /// </summary>
    public async Task<SchemaMismatchReport> ValidateSchemaAsync(string entityName, string rawBody)
    {
        var report = new SchemaMismatchReport
        {
            EntityName = entityName,
            CheckTimestamp = DateTime.UtcNow
        };

        if (!EntityTableMap.TryGetValue(entityName, out var tableName))
        {
            report.TargetTable = "UNKNOWN";
            report.TableExists = false;
            await SaveSchemaMismatchReportAsync(report);
            return report;
        }

        report.TargetTable = tableName;

        try
        {
            // Get table columns from SQL Server
            await using var connection = new SqlConnection(_replicaConnectionString);
            await connection.OpenAsync();

            // Check if table exists
            var tableExists = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @TableName",
                new { TableName = tableName });

            report.TableExists = tableExists > 0;

            if (!report.TableExists)
            {
                _logger.LogWarning("Table {Table} does not exist for entity {Entity}", tableName, entityName);
                await SaveSchemaMismatchReportAsync(report);
                return report;
            }

            // Get column names from table
            var columns = (await connection.QueryAsync<string>(
                "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @TableName",
                new { TableName = tableName })).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Get field names from API response
            var apiFields = ExtractApiFieldNames(rawBody);

            // Compare
            foreach (var col in columns)
            {
                if (apiFields.Contains(col))
                {
                    report.MatchedColumns.Add(col);
                }
                else
                {
                    // Column in table but not in API response (may be populated by transformation)
                    // Not necessarily a problem, but worth noting
                }
            }

            foreach (var field in apiFields)
            {
                if (!columns.Contains(field))
                {
                    report.UnexpectedApiFields.Add(field);
                }
            }

            // Check for columns we expect to map but are missing
            // These are columns that aren't in the API response and aren't auto-generated
            var nonGeneratedColumns = columns.Where(c => 
                !c.Equals("ID", StringComparison.OrdinalIgnoreCase) &&
                !c.Contains("Name", StringComparison.OrdinalIgnoreCase) || 
                apiFields.Any(f => f.Equals(c, StringComparison.OrdinalIgnoreCase))).ToList();

            // Extract sample payload
            report.SamplePayload = ExtractSamplePayload(rawBody);

            // Log summary
            if (report.UnexpectedApiFields.Count > 0 || report.MissingColumns.Count > 0)
            {
                _logger.LogWarning(
                    "Schema mismatch for {Entity}: {UnexpectedCount} unexpected API fields, {MissingCount} missing columns",
                    entityName, report.UnexpectedApiFields.Count, report.MissingColumns.Count);
            }

            await SaveSchemaMismatchReportAsync(report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate schema for {Entity}", entityName);
            report.MissingColumns.Add($"ERROR: {ex.Message}");
        }

        return report;
    }

    /// <summary>
    /// Extract field names from the first object in the API response
    /// </summary>
    private HashSet<string> ExtractApiFieldNames(string rawBody)
    {
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;
            JsonElement? firstItem = null;

            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                firstItem = root[0];
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                // Check for wrapper
                foreach (var propName in new[] { "data", "Data", "items", "Items", "results", "Results", "value", "Value" })
                {
                    if (root.TryGetProperty(propName, out var arrayProp) && 
                        arrayProp.ValueKind == JsonValueKind.Array && 
                        arrayProp.GetArrayLength() > 0)
                    {
                        firstItem = arrayProp[0];
                        break;
                    }
                }

                // If no array wrapper, use the object itself
                if (firstItem == null)
                {
                    firstItem = root;
                }
            }

            if (firstItem?.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in firstItem.Value.EnumerateObject())
                {
                    fields.Add(prop.Name);
                }
            }
        }
        catch (JsonException)
        {
            // Ignore parse errors
        }

        return fields;
    }

    /// <summary>
    /// Extract a sample payload (first object) from the response
    /// </summary>
    private Dictionary<string, object?> ExtractSamplePayload(string rawBody)
    {
        var sample = new Dictionary<string, object?>();

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;
            JsonElement? firstItem = null;

            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                firstItem = root[0];
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var propName in new[] { "data", "Data", "items", "Items", "results", "Results", "value", "Value" })
                {
                    if (root.TryGetProperty(propName, out var arrayProp) && 
                        arrayProp.ValueKind == JsonValueKind.Array && 
                        arrayProp.GetArrayLength() > 0)
                    {
                        firstItem = arrayProp[0];
                        break;
                    }
                }

                if (firstItem == null)
                {
                    firstItem = root;
                }
            }

            if (firstItem?.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in firstItem.Value.EnumerateObject())
                {
                    sample[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString(),
                        JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Null => null,
                        _ => prop.Value.GetRawText()
                    };
                }
            }
        }
        catch (JsonException)
        {
            // Ignore
        }

        return sample;
    }

    /// <summary>
    /// Save schema mismatch report to disk
    /// </summary>
    private async Task SaveSchemaMismatchReportAsync(SchemaMismatchReport report)
    {
        try
        {
            var reportPath = Path.Combine(_sessionPath, $"SchemaMismatch.{report.EntityName}.json");
            var json = JsonSerializer.Serialize(report, _jsonOptions);
            await File.WriteAllTextAsync(reportPath, json);
            
            if (report.UnexpectedApiFields.Count > 0)
            {
                Console.WriteLine($"    [SCHEMA] {report.EntityName}: {report.UnexpectedApiFields.Count} unexpected API fields: {string.Join(", ", report.UnexpectedApiFields.Take(5))}...");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save schema mismatch report for {Entity}", report.EntityName);
        }
    }

    /// <summary>
    /// Generate a session summary file
    /// </summary>
    public async Task GenerateSessionSummaryAsync(Dictionary<string, int> entityCounts, bool success, string? errorMessage = null)
    {
        try
        {
            var summary = new
            {
                SessionPath = _sessionPath,
                CaptureTimestamp = DateTime.UtcNow,
                Success = success,
                ErrorMessage = errorMessage,
                EntityCounts = entityCounts,
                TotalRecords = entityCounts.Values.Sum(),
                Files = Directory.GetFiles(_sessionPath).Select(Path.GetFileName).ToList()
            };

            var summaryPath = Path.Combine(_sessionPath, "_SESSION_SUMMARY.json");
            var json = JsonSerializer.Serialize(summary, _jsonOptions);
            await File.WriteAllTextAsync(summaryPath, json);

            Console.WriteLine();
            Console.WriteLine($"[RAW CAPTURE] Session summary saved to: {summaryPath}");
            Console.WriteLine($"[RAW CAPTURE] Total files captured: {summary.Files.Count}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate session summary");
        }
    }
}
