using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MasterPlan.SyncEngine;

/// <summary>
/// Temporary service for dumping raw API responses to disk.
/// Used to work around strict API connection limits (3-4 per day).
/// 
/// Usage:
///   --dump-api         Call all API endpoints ONCE, save raw JSON to ./ApiDump/
///   --load-from-dump   Read from ./ApiDump/, run daily sync without calling API
/// 
/// Remove this service after validation is complete.
/// </summary>
public class ApiDumpService
{
    private readonly MasterPlanApiClient _apiClient;
    private readonly ILogger<ApiDumpService> _logger;
    private readonly string _dumpFolder;

    /// <summary>
    /// Endpoint map: entity name -> relative URL with early FromDate to fetch all data.
    /// URLs match MasterPlanApiClient typed methods exactly.
    /// </summary>
    private static readonly (string EntityName, string Url)[] DumpEndpoints =
    [
        ("Projects",              "projects/?lastUpdated=2000-01-01T00:00:00"),
        ("Bids",                  "bid/?lastUpdated=2000-01-01T00:00:00"),
        ("Bills",                 "Bill/?lastUpdated=2000-01-01T00:00:00"),
        ("Companies",             "Companies/?lastUpdated=2000-01-01T00:00:00"),
        ("Contacts",              "Contact/?lastUpdated=2000-01-01T00:00:00"),
        ("Conversations",         "Conversations/?createdDate=2000-01-01T00:00:00"),
        ("Employees",             "Employee/?lastUpdated=2000-01-01T00:00:00"),
        ("Intakes",               "Intake/?lastUpdated=2000-01-01T00:00:00"),
        ("Tasks",                 "Tasks/?dueDate=2000-01-01T00:00:00"),
        ("ProjectHours",          "ProjectHours/?fromDate=2000-01-01T00:00:00"),
        ("TimeHourReports",       "projecthours/GetTimeHourReports?FromDate=2000-01-01T00:00:00"),
        ("ProjectHoursExtended",  "projecthours/GetProjectHoursExtended?FromDate=2000-01-01T00:00:00"),
    ];

    /// <summary>
    /// All entity names that will be dumped/loaded (for file validation).
    /// </summary>
    public static IReadOnlyList<string> EntityNames { get; } =
        DumpEndpoints.Select(e => e.EntityName).ToArray();

    public ApiDumpService(
        MasterPlanApiClient apiClient,
        ILogger<ApiDumpService> logger,
        string dumpFolder = "ApiDump")
    {
        _apiClient = apiClient;
        _logger = logger;
        _dumpFolder = dumpFolder;
    }

    /// <summary>
    /// Call ALL API endpoints and save raw JSON responses to disk.
    /// Stops immediately if any endpoint returns a non-success status code.
    /// </summary>
    public async Task DumpAllEndpointsAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_dumpFolder);
        var fullPath = Path.GetFullPath(_dumpFolder);
        _logger.LogInformation("Dumping all API endpoints to {Folder}", fullPath);

        var successCount = 0;

        foreach (var (entityName, url) in DumpEndpoints)
        {
            Console.WriteLine($"  \u2206 {entityName}");

            var (rawJson, statusCode) = await _apiClient.GetRawResponseAsync(url, cancellationToken);

            if (statusCode < 200 || statusCode >= 300)
            {
                _logger.LogError("API call failed for {Entity}: HTTP {StatusCode}", entityName, statusCode);
                throw new InvalidOperationException(
                    $"--dump-api STOPPED: {entityName} returned HTTP {statusCode}. URL: {url}");
            }

            var filePath = Path.Combine(_dumpFolder, $"{entityName}.json");
            await File.WriteAllTextAsync(filePath, rawJson, cancellationToken);

            var fileInfo = new FileInfo(filePath);
            var recordCount = CountJsonRecords(rawJson);

            Console.WriteLine($"        URL:     {url}");
            Console.WriteLine($"        Status:  {statusCode} OK");
            Console.WriteLine($"        Records: {recordCount:N0}");
            Console.WriteLine($"        File:    {filePath}");
            Console.WriteLine($"        Size:    {fileInfo.Length:N0} bytes");

            _logger.LogInformation(
                "[DUMP] {Entity}: HTTP {StatusCode}, {Records} records, {Size:N0} bytes -> {File}",
                entityName, statusCode, recordCount, fileInfo.Length, filePath);

            successCount++;
        }

        Console.WriteLine();
        Console.WriteLine($"  Done: {successCount}/{DumpEndpoints.Length} endpoints dumped to {fullPath}");
    }

    /// <summary>
    /// Count JSON records in raw response.
    /// Handles both direct array [...] and wrapped object { "data": [...] } formats.
    /// </summary>
    private static int CountJsonRecords(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
                return root.GetArrayLength();

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var propName in new[] { "data", "Data", "items", "Items", "results", "Results", "value", "Value" })
                {
                    if (root.TryGetProperty(propName, out var arrayProp) && arrayProp.ValueKind == JsonValueKind.Array)
                        return arrayProp.GetArrayLength();
                }
            }

            return 0;
        }
        catch
        {
            return -1;
        }
    }
}
