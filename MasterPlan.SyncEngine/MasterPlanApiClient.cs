using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using MasterPlan.SyncEngine.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MasterPlan.SyncEngine;

/// <summary>
/// HTTP client for MasterPlan Web API
/// Handles authentication, requests, and response parsing for all entity endpoints.
/// 
/// ═══════════════════════════════════════════════════════════════════════════════════════════
/// ENDPOINT AUDIT TABLE
/// ═══════════════════════════════════════════════════════════════════════════════════════════
/// 
/// Entity               | Exact Endpoint Path                        | Query Parameters
/// ---------------------+--------------------------------------------+------------------------------------------
/// Projects             | projects/                                  | startDate, lastUpdated, isActive
/// Bids                 | bid/                                       | isActive, fromDate, lastUpdated, bidStatusId
/// Bills                | Bill/                                      | lastUpdated, submitDate, collectionDate, billStatusId
/// Companies            | Companies/                                 | isActive, lastUpdated
/// Contacts             | Contact/                                   | isActive, lastUpdated
/// Conversations        | Conversations/                             | createdDate, dueDate
/// Employees            | Employee/                                  | isActive, lastUpdated
/// Intakes              | Intake/                                    | openDate, lastUpdated, payTypeId
/// Tasks                | Tasks/                                     | dueDate, priorityId, isCompleted
/// ProjectHours         | ProjectHours/                              | fromDate
/// TimeHourReports      | projecthours/GetTimeHourReports            | FromDate
/// ProjectHoursExtended | projecthours/GetProjectHoursExtended       | FromDate
/// 
/// ═══════════════════════════════════════════════════════════════════════════════════════════
/// NOTE: Resource-based endpoints use pattern /MPWebAPI/api/&lt;resource&gt;/
/// TimeHourReports and ProjectHoursExtended use method-style routes (per API docs).
/// ═══════════════════════════════════════════════════════════════════════════════════════════
/// </summary>
public class MasterPlanApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MasterPlanApiClient> _logger;
    private readonly string _baseUrl;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly RawCaptureService? _captureService;
    private string? _dumpLoadFolder;

    // Per-run API call tracking: Key = normalized endpoint path, Value = call count
    private string? _currentRunId;
    private readonly ConcurrentDictionary<string, int> _apiCallCounts = new();

    // Response body preview length for logging
    private const int ResponsePreviewLength = 500;

    public MasterPlanApiClient(
        IConfiguration configuration, 
        ILogger<MasterPlanApiClient> logger,
        RawCaptureService? captureService = null)
    {
        _logger = logger;
        _captureService = captureService;

        // Read configuration
        var apiConfig = configuration.GetSection("MasterPlanApi");
        _baseUrl = apiConfig["BaseUrl"] ?? throw new InvalidOperationException("MasterPlanApi:BaseUrl is required");

        // API key precedence:
        //   1) Windows Credential Manager  (SecretKeys.MasterPlanApiKey) — preferred, per-user encrypted
        //   2) MASTERPLAN_API_KEY env var  — for ad-hoc/test runs on servers without WPF vault
        var apiKey = MasterPlan.SyncEngine.Shared.CredentialVaultService.GetSecret(
                MasterPlan.SyncEngine.Shared.SecretKeys.MasterPlanApiKey)
            ?? Environment.GetEnvironmentVariable("MASTERPLAN_API_KEY")
            ?? throw new InvalidOperationException(
                "MasterPlan API key not found. Provision via WPF SecretSetupWindow " +
                $"(vault key '{MasterPlan.SyncEngine.Shared.SecretKeys.MasterPlanApiKey}'), " +
                "or set the MASTERPLAN_API_KEY environment variable. " +
                "Do not store API keys in appsettings.json.");

        var timeoutSeconds = int.TryParse(apiConfig["TimeoutSeconds"], out var t) ? t : 300;

        // Configure HTTP client with required headers
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_baseUrl),
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };

        // Required headers per API guide
        _httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new TimeSpanHHmmConverter() }
        };

        _logger.LogInformation("MasterPlanApiClient initialized with BaseUrl: {BaseUrl}", _baseUrl);
    }

    /// <summary>
    /// Enable dump-load mode: GetEntitiesAsync reads from local JSON files instead of HTTP.
    /// Used by --load-from-dump CLI flag. Temporary for validation.
    /// </summary>
    public void SetDumpLoadMode(string dumpFolderPath)
    {
        _dumpLoadFolder = dumpFolderPath;
        _logger.LogInformation("API client set to DUMP-LOAD mode. Source folder: {Folder}", Path.GetFullPath(dumpFolderPath));
    }

    /// <summary>
    /// Start tracking API calls for a specific sync run.
    /// Resets all counters and sets the RunId.
    /// </summary>
    public void StartRunTracking(string runId)
    {
        _currentRunId = runId;
        _apiCallCounts.Clear();
        _logger.LogInformation("[API TRACKING] Started for RunId={RunId}", runId);
    }

    /// <summary>
    /// Returns the API call summary: endpoint → call count.
    /// </summary>
    public IReadOnlyDictionary<string, int> GetApiCallSummary() => _apiCallCounts;

    /// <summary>
    /// Records an HTTP call against the per-run counter.
    /// Key is the endpoint path (before query string) to group calls to the same endpoint.
    /// </summary>
    private void TrackApiCall(string url, string source)
    {
        // Normalize: strip query string to group by endpoint path
        var normalizedPath = url.Contains('?') ? url[..url.IndexOf('?')] : url;
        var fullKey = $"{source}:{normalizedPath}";
        var newCount = _apiCallCounts.AddOrUpdate(fullKey, 1, (_, c) => c + 1);
        _logger.LogInformation(
            "[API CALL #{Count}] RunId={RunId} Source={Source} Endpoint={Endpoint} FullUrl={FullUrl}",
            newCount, _currentRunId ?? "(none)", source, normalizedPath, url);
    }

    /// <summary>
    /// Get raw HTTP response body as string without deserialization.
    /// Used by --dump-api to save unprocessed API responses to disk.
    /// </summary>
    public async Task<(string RawJson, int StatusCode)> GetRawResponseAsync(
        string url, CancellationToken cancellationToken = default)
    {
        var fullUrl = $"{_baseUrl}{url}";
        _logger.LogInformation("[RAW] GET {Url}", fullUrl);

        var response = await _httpClient.GetAsync(url, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        return (content, (int)response.StatusCode);
    }

    /// <summary>
    /// Endpoint registry: maps entity names to their exact API paths and query params.
    /// This is the SINGLE SOURCE OF TRUTH for endpoint configuration.
    /// </summary>
    private static readonly Dictionary<string, (string Path, string[] QueryParams)> EndpointRegistry = new()
    {
        ["Projects"]      = ("projects/",       new[] { "startDate", "lastUpdated", "isActive" }),
        ["Bids"]          = ("bid/",            new[] { "isActive", "fromDate", "lastUpdated", "bidStatusId" }),
        ["Bills"]         = ("Bill/",           new[] { "lastUpdated", "submitDate", "collectionDate", "billStatusId" }),
        ["Companies"]     = ("Companies/",      new[] { "isActive", "lastUpdated" }),
        ["Contacts"]      = ("Contact/",        new[] { "isActive", "lastUpdated" }),
        ["Conversations"] = ("Conversations/",  new[] { "createdDate", "dueDate" }),
        ["Employees"]     = ("Employee/",       new[] { "isActive", "lastUpdated" }),
        ["Intakes"]       = ("Intake/",         new[] { "openDate", "lastUpdated", "payTypeId" }),
        ["Tasks"]         = ("Tasks/",          new[] { "dueDate", "priorityId", "isCompleted" }),
        ["ProjectHours"]  = ("ProjectHours/",   new[] { "fromDate" }),
        // New Hours endpoints - method-style routes (exception to resource-only pattern per API docs)
        ["TimeHourReports"]       = ("projecthours/GetTimeHourReports",       new[] { "FromDate" }),
        ["ProjectHoursExtended"]  = ("projecthours/GetProjectHoursExtended",  new[] { "FromDate" })
    };

    /// <summary>
    /// Validates all endpoints by making a HEAD or GET request and logging the result.
    /// Call this at startup to ensure all endpoints are accessible.
    /// </summary>
    public async Task<Dictionary<string, (bool Success, int StatusCode, string Message)>> ValidateAllEndpointsAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, (bool Success, int StatusCode, string Message)>();

        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                         ENDPOINT VALIDATION AUDIT                                        ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  Entity          │ Endpoint Path           │ Full URL                                    ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════════════════════╣");

        foreach (var (entityName, (path, queryParams)) in EndpointRegistry)
        {
            var fullUrl = $"{_baseUrl}{path}";
            Console.WriteLine($"║  {entityName,-15} │ {path,-23} │ {fullUrl,-44}║");
        }

        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  VALIDATION RESULTS:                                                                     ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════════════════════╣");

        foreach (var (entityName, (path, _)) in EndpointRegistry)
        {
            try
            {
                var fullUrl = $"{_baseUrl}{path}";
                TrackApiCall(path, "Validation");
                var response = await _httpClient.GetAsync(path, cancellationToken);
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
                var statusCode = (int)response.StatusCode;
                var preview = content.Length > 200 ? content[..200] + "..." : content;
                preview = preview.Replace("\n", " ").Replace("\r", "");

                var success = response.IsSuccessStatusCode;
                var statusIcon = success ? "✓" : "✗";
                var statusText = success ? "OK" : "FAIL";

                results[entityName] = (success, statusCode, success ? "OK" : response.ReasonPhrase ?? "Error");

                Console.WriteLine($"║  {statusIcon} {entityName,-13} │ {statusCode} {statusText,-4} │ {contentType,-16} │ Preview: {preview[..Math.Min(30, preview.Length)]}... ║");

                _logger.LogInformation(
                    "[ENDPOINT AUDIT] {Entity}: StatusCode={StatusCode}, ContentType={ContentType}, Preview={Preview}",
                    entityName, statusCode, contentType, preview);

                if (!success)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"║    ⚠ ENDPOINT MISMATCH: {entityName} returned {statusCode}. Check path: {path}");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                results[entityName] = (false, 0, ex.Message);
                Console.WriteLine($"║  ✗ {entityName,-13} │ ERROR │ {ex.Message[..Math.Min(50, ex.Message.Length)]}...");
                _logger.LogError(ex, "[ENDPOINT AUDIT] {Entity} failed with exception", entityName);
            }
        }

        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        return results;
    }

    #region Projects

    /// <summary>
    /// Get all projects, optionally filtered by lastUpdated date
    /// Endpoint: /MPWebAPI/api/projects/
    /// </summary>
    public async Task<List<ProjectEntity>> GetProjectsAsync(DateTime? lastUpdated = null, CancellationToken cancellationToken = default)
    {
        var url = "projects/";
        if (lastUpdated.HasValue)
        {
            url += $"?lastUpdated={lastUpdated.Value:yyyy-MM-ddTHH:mm:ss}";
        }

        return await GetEntitiesAsync<ProjectEntity>(url, "Projects", cancellationToken);
    }

    #endregion

    #region Bids

    /// <summary>
    /// Get all bids, optionally filtered by lastUpdated date
    /// Endpoint: /MPWebAPI/api/bid/
    /// </summary>
    public async Task<List<BidEntity>> GetBidsAsync(DateTime? lastUpdated = null, CancellationToken cancellationToken = default)
    {
        var url = "bid/";
        if (lastUpdated.HasValue)
        {
            url += $"?lastUpdated={lastUpdated.Value:yyyy-MM-ddTHH:mm:ss}";
        }

        return await GetEntitiesAsync<BidEntity>(url, "Bids", cancellationToken);
    }

    #endregion

    #region Bills

    /// <summary>
    /// Get all bills, optionally filtered by lastUpdated date
    /// Endpoint: /MPWebAPI/api/Bill/
    /// </summary>
    public async Task<List<BillEntity>> GetBillsAsync(DateTime? lastUpdated = null, CancellationToken cancellationToken = default)
    {
        var url = "Bill/";
        if (lastUpdated.HasValue)
        {
            url += $"?lastUpdated={lastUpdated.Value:yyyy-MM-ddTHH:mm:ss}";
        }

        return await GetEntitiesAsync<BillEntity>(url, "Bills", cancellationToken);
    }

    #endregion

    #region Companies

    /// <summary>
    /// Get all companies, optionally filtered by lastUpdated date
    /// Endpoint: /MPWebAPI/api/Companies/
    /// </summary>
    public async Task<List<CompanyEntity>> GetCompaniesAsync(DateTime? lastUpdated = null, CancellationToken cancellationToken = default)
    {
        var url = "Companies/";
        if (lastUpdated.HasValue)
        {
            url += $"?lastUpdated={lastUpdated.Value:yyyy-MM-ddTHH:mm:ss}";
        }

        return await GetEntitiesAsync<CompanyEntity>(url, "Companies", cancellationToken);
    }

    #endregion

    #region Contacts

    /// <summary>
    /// Get all contacts, optionally filtered by lastUpdated date
    /// Endpoint: /MPWebAPI/api/Contact/
    /// </summary>
    public async Task<List<ContactEntity>> GetContactsAsync(DateTime? lastUpdated = null, CancellationToken cancellationToken = default)
    {
        var url = "Contact/";
        if (lastUpdated.HasValue)
        {
            url += $"?lastUpdated={lastUpdated.Value:yyyy-MM-ddTHH:mm:ss}";
        }

        return await GetEntitiesAsync<ContactEntity>(url, "Contacts", cancellationToken);
    }

    #endregion

    #region Conversations

    /// <summary>
    /// Get all conversations, optionally filtered by createdDate
    /// Endpoint: /MPWebAPI/api/Conversations/
    /// </summary>
    public async Task<List<ConversationEntity>> GetConversationsAsync(DateTime? createdDate = null, CancellationToken cancellationToken = default)
    {
        var url = "Conversations/";
        if (createdDate.HasValue)
        {
            url += $"?createdDate={createdDate.Value:yyyy-MM-ddTHH:mm:ss}";
        }

        return await GetEntitiesAsync<ConversationEntity>(url, "Conversations", cancellationToken);
    }

    #endregion

    #region Employees

    /// <summary>
    /// Get all employees, optionally filtered by lastUpdated date
    /// Endpoint: /MPWebAPI/api/Employee/
    /// </summary>
    public async Task<List<EmployeeEntity>> GetEmployeesAsync(DateTime? lastUpdated = null, CancellationToken cancellationToken = default)
    {
        var url = "Employee/";
        if (lastUpdated.HasValue)
        {
            url += $"?lastUpdated={lastUpdated.Value:yyyy-MM-ddTHH:mm:ss}";
        }

        return await GetEntitiesAsync<EmployeeEntity>(url, "Employees", cancellationToken);
    }

    #endregion

    #region Intake

    /// <summary>
    /// Get all intake (payment receipts), optionally filtered by lastUpdated date
    /// Endpoint: /MPWebAPI/api/Intake/
    /// </summary>
    public async Task<List<IntakeEntity>> GetIntakesAsync(DateTime? lastUpdated = null, CancellationToken cancellationToken = default)
    {
        var url = "Intake/";
        if (lastUpdated.HasValue)
        {
            url += $"?lastUpdated={lastUpdated.Value:yyyy-MM-ddTHH:mm:ss}";
        }

        return await GetEntitiesAsync<IntakeEntity>(url, "Intakes", cancellationToken);
    }

    #endregion

    #region Tasks

    /// <summary>
    /// Get all tasks, optionally filtered by lastUpdated date
    /// Endpoint: /MPWebAPI/api/Tasks/
    /// NOTE: API docs show dueDate parameter, but we use lastUpdated for incremental sync
    /// (matches watermark strategy — dueDate would filter by completion date, not modification)
    /// </summary>
    public async Task<List<TaskEntity>> GetTasksAsync(DateTime? lastUpdated = null, CancellationToken cancellationToken = default)
    {
        var url = "Tasks/";
        if (lastUpdated.HasValue)
        {
            url += $"?lastUpdated={lastUpdated.Value:yyyy-MM-ddTHH:mm:ss}";
        }

        return await GetEntitiesAsync<TaskEntity>(url, "Tasks", cancellationToken);
    }

    #endregion

    #region Project Hours

        /// <summary>
        /// Get all project hours, optionally filtered by fromDate
        /// Endpoint: /MPWebAPI/api/ProjectHours/
        /// </summary>
        public async Task<List<ProjectHoursEntity>> GetProjectHoursAsync(DateTime? fromDate = null, CancellationToken cancellationToken = default)
        {
            var url = "ProjectHours/";
            if (fromDate.HasValue)
            {
                url += $"?fromDate={fromDate.Value:yyyy-MM-ddTHH:mm:ss}";
            }

            return await GetEntitiesAsync<ProjectHoursEntity>(url, "ProjectHours", cancellationToken);
        }

        #endregion

        #region Time Hour Reports

        /// <summary>
        /// Get time hour reports, optionally filtered by FromDate
        /// Endpoint: /MPWebAPI/api/projecthours/GetTimeHourReports
        /// 
        /// NOTE: Uses method-style route (exception to resource-only pattern per API docs)
        /// NOTE: Response field is "DateTime" not "ReportDate"
        /// </summary>
        public async Task<List<TimeHourReportEntity>> GetTimeHourReportsAsync(DateTime? fromDate = null, CancellationToken cancellationToken = default)
        {
            var url = "projecthours/GetTimeHourReports";
            if (fromDate.HasValue)
            {
                url += $"?FromDate={fromDate.Value:yyyy-MM-ddTHH:mm:ss}";
            }

            return await GetEntitiesAsync<TimeHourReportEntity>(url, "TimeHourReports", cancellationToken);
        }

        #endregion

        #region Project Hours Extended

        /// <summary>
        /// Get extended project hours with SubContract details, optionally filtered by FromDate
        /// Endpoint: /MPWebAPI/api/projecthours/GetProjectHoursExtended
        /// 
        /// NOTE: Uses method-style route (exception to resource-only pattern per API docs)
        /// NOTE: Response is wrapped in { "data": [...] } format
        /// NOTE: Supports incremental sync via LastUpdated field
        /// </summary>
        public async Task<List<ProjectHoursExtendedEntity>> GetProjectHoursExtendedAsync(DateTime? fromDate = null, CancellationToken cancellationToken = default)
        {
            var url = "projecthours/GetProjectHoursExtended";
            if (fromDate.HasValue)
            {
                url += $"?FromDate={fromDate.Value:yyyy-MM-ddTHH:mm:ss}";
            }

            return await GetEntitiesAsync<ProjectHoursExtendedEntity>(url, "ProjectHoursExtended", cancellationToken);
        }

        #endregion

    #region Private Methods

    private async Task<List<T>> GetEntitiesAsync<T>(string endpoint, string entityName, CancellationToken cancellationToken)
    {
        // DUMP-LOAD MODE: Read from local JSON files instead of HTTP API
        if (_dumpLoadFolder != null)
        {
            return await LoadEntitiesFromDumpFileAsync<T>(entityName, cancellationToken);
        }

        var fullUrl = $"{_baseUrl}{endpoint}";
        _logger.LogInformation("[API REQUEST] RunId={RunId} Entity={EntityName} URL={Url}", _currentRunId ?? "(none)", entityName, fullUrl);
        Console.WriteLine($"    [API] GET {fullUrl}");

        try
        {
            TrackApiCall(endpoint, $"Sync:{entityName}");
            var response = await _httpClient.GetAsync(endpoint, cancellationToken);

            // Read response content for logging
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
            var contentPreview = content.Length > ResponsePreviewLength 
                ? content[..ResponsePreviewLength] + "..." 
                : content;

            // Log response details before processing
            _logger.LogDebug(
                "[API Response] {EntityName}: StatusCode={StatusCode}, ContentType={ContentType}, BodyPreview={Preview}",
                entityName, (int)response.StatusCode, contentType, contentPreview);

            Console.WriteLine($"    [API] Response: {(int)response.StatusCode} {response.StatusCode}, ContentType: {contentType}");
            Console.WriteLine($"    [API] Body preview: {contentPreview.Replace("\n", " ").Replace("\r", "")}");

            // RAW CAPTURE MODE: Save response to disk before deserialization
            if (_captureService != null)
            {
                var requestParams = endpoint.Contains('?') ? endpoint[(endpoint.IndexOf('?') + 1)..] : null;
                await _captureService.CaptureRawResponseAsync(
                    entityName,
                    fullUrl,
                    requestParams,
                    (int)response.StatusCode,
                    contentType,
                    content,
                    requestParams);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("API request failed for {EntityName}: {StatusCode} - {Content}", 
                    entityName, (int)response.StatusCode, content);

                throw new MasterPlanApiException(
                    $"API request failed for {entityName}: {response.ReasonPhrase}", 
                    (int)response.StatusCode, 
                    content);
            }

            // Try to deserialize - handle both array and wrapped object responses
            List<T>? entities = null;

            try
            {
                // First try: direct array deserialization
                entities = JsonSerializer.Deserialize<List<T>>(content, _jsonOptions);
            }
            catch (JsonException)
            {
                // Second try: check if response is wrapped in an object (e.g., { "data": [...] })
                _logger.LogWarning("Direct array deserialization failed for {EntityName}, trying wrapped object...", entityName);

                try
                {
                    using var doc = JsonDocument.Parse(content);
                    var root = doc.RootElement;

                    // Look for common wrapper properties
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        // Try common wrapper property names
                        foreach (var propName in new[] { "data", "Data", "items", "Items", "results", "Results", "value", "Value" })
                        {
                            if (root.TryGetProperty(propName, out var arrayProp) && arrayProp.ValueKind == JsonValueKind.Array)
                            {
                                entities = JsonSerializer.Deserialize<List<T>>(arrayProp.GetRawText(), _jsonOptions);
                                _logger.LogInformation("Found {EntityName} data in wrapper property '{PropName}'", entityName, propName);
                                break;
                            }
                        }

                        // If still null and root is a single object, wrap it in a list
                        if (entities == null && root.ValueKind == JsonValueKind.Object)
                        {
                            var singleEntity = JsonSerializer.Deserialize<T>(content, _jsonOptions);
                            if (singleEntity != null)
                            {
                                entities = new List<T> { singleEntity };
                                _logger.LogInformation("Wrapped single {EntityName} object into list", entityName);
                            }
                        }
                    }
                }
                catch (Exception wrapEx)
                {
                    _logger.LogError(wrapEx, "Failed to parse wrapped response for {EntityName}", entityName);
                }
            }

            entities ??= new List<T>();

            _logger.LogInformation("Retrieved {Count} {EntityName} records", entities.Count, entityName);
            Console.WriteLine($"    [API] → Retrieved {entities.Count} {entityName} records");

            return entities;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error fetching {EntityName}", entityName);
            throw new MasterPlanApiException($"Network error fetching {entityName}: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (ex.CancellationToken != cancellationToken)
        {
            _logger.LogError(ex, "Timeout fetching {EntityName}", entityName);
            throw new MasterPlanApiException($"Request timeout fetching {entityName}", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON parsing error for {EntityName}", entityName);
            throw new MasterPlanApiException($"Failed to parse {entityName} response: {ex.Message}", ex);
        }
    }

    #endregion

    /// <summary>
    /// Load and deserialize entities from a local JSON dump file.
    /// Handles both direct array [...] and wrapped object { "data": [...] } formats.
    /// Used by --load-from-dump mode. Temporary for validation.
    /// </summary>
    private async Task<List<T>> LoadEntitiesFromDumpFileAsync<T>(string entityName, CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(_dumpLoadFolder!, $"{entityName}.json");
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Dump file missing for {entityName}: {filePath}", filePath);

        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        _logger.LogInformation("[DUMP-LOAD] Loading {EntityName} from {FilePath} ({Size:N0} bytes)",
            entityName, filePath, content.Length);
        Console.WriteLine($"    [DUMP] Loading {entityName} from {filePath} ({content.Length:N0} bytes)");

        List<T>? entities = null;
        try
        {
            entities = JsonSerializer.Deserialize<List<T>>(content, _jsonOptions);
        }
        catch (JsonException)
        {
            _logger.LogWarning("Direct array deserialization failed for {EntityName} dump file, trying wrapped object...", entityName);

            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Object)
                {
                    foreach (var propName in new[] { "data", "Data", "items", "Items", "results", "Results", "value", "Value" })
                    {
                        if (root.TryGetProperty(propName, out var arrayProp) && arrayProp.ValueKind == JsonValueKind.Array)
                        {
                            entities = JsonSerializer.Deserialize<List<T>>(arrayProp.GetRawText(), _jsonOptions);
                            _logger.LogInformation("Found {EntityName} data in wrapper property '{PropName}' (dump file)", entityName, propName);
                            break;
                        }
                    }
                }
            }
            catch (Exception wrapEx)
            {
                _logger.LogError(wrapEx, "Failed to parse wrapped response from dump file for {EntityName}", entityName);
            }
        }

        entities ??= new List<T>();
        _logger.LogInformation("[DUMP-LOAD] Deserialized {Count} {EntityName} records from dump file", entities.Count, entityName);
        Console.WriteLine($"    [DUMP] -> Deserialized {entities.Count} {entityName} records");

        return entities;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
