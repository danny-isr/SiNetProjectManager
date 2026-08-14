using System.Diagnostics;
using SiOffice.AccService.Contracts;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Configuration;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class RemoteAccFileUploadService(
    HttpClient httpClient,
    ISecretVaultStore secretVaultStore,
    IAccServiceModeProvider serviceModeProvider,
    IAppLogger logger) : IAccFileUploadService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ISecretVaultStore _secretVaultStore = secretVaultStore;
    private readonly IAccServiceModeProvider _serviceModeProvider = serviceModeProvider;
    private readonly IAppLogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<AccFileUploadResult> UploadAsync(
        AccFileUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.LocalSourcePath))
        {
            throw new ArgumentException("LocalSourcePath is required.", nameof(request));
        }

        if (!File.Exists(request.LocalSourcePath))
        {
            throw new FileNotFoundException("ACC upload source file not found.", request.LocalSourcePath);
        }

        var baseUrl = _serviceModeProvider.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.Error("[AccRemote] missing BaseUrl op=AccUpload");
            throw new InvalidOperationException("ACC service base URL is not configured for remote file upload.");
        }

        var apiKey = _secretVaultStore.GetSecret(SecretCatalog.AccServiceApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.Error("[AccRemote] missing ApiKey op=AccUpload");
            throw new InvalidOperationException("ACC service API key is not configured in the native secret vault.");
        }

        var payload = new RemoteAccFileUploadPayload(
            request.TargetFolderId,
            request.RootFolderId,
            request.PathSegments ?? Array.Empty<string>(),
            request.DisplayName,
            request.ExistingItemId,
            request.SourceIdentity,
            request.Snapshot,
            request.CompanionDocument);

        var fileInfo = new FileInfo(request.LocalSourcePath);
        var fileSizeBytes = fileInfo.Length;

        // #region agent log
        try
        {
            var dbg = JsonSerializer.Serialize(new
            {
                sessionId = "487a8a",
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                runId = "upload-timeout-fix",
                hypothesisId = "H-A",
                location = "RemoteAccFileUploadService.UploadAsync",
                message = "remote upload starting",
                data = new
                {
                    displayName = request.DisplayName,
                    fileSizeBytes,
                    httpClientTimeoutSec = _httpClient.Timeout.TotalSeconds,
                },
            });
            File.AppendAllText(@"d:\repos2026\debug-487a8a.log", dbg + Environment.NewLine);
        }
        catch { }
        // #endregion

        var stopwatch = Stopwatch.StartNew();
        using var fileStream = fileInfo.OpenRead();
        using var fileContent = new StreamContent(fileStream);
        using var requestContent = new MultipartFormDataContent();
        requestContent.Add(
            new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json"),
            "request");
        requestContent.Add(fileContent, "file", Path.GetFileName(request.LocalSourcePath));

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            BuildRequestUri(baseUrl, request.ProjectId))
        {
            Content = requestContent,
        };
        message.Headers.Add(AccServiceContracts.ApiKeyHeader, apiKey);

        using var response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.Error(
                $"[AccUpload] outcome=Failed project={request.ProjectId} displayName='{request.DisplayName}' folder={request.TargetFolderId} http={(int)response.StatusCode} detail={response.ReasonPhrase}");
            response.EnsureSuccessStatusCode();
        }

        var body = await response.Content
            .ReadFromJsonAsync<RemoteAccFileUploadResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (body is null)
        {
            _logger.Error(
                $"[AccUpload] outcome=Failed project={request.ProjectId} displayName='{request.DisplayName}' detail=empty upload response");
            throw new InvalidOperationException("ACC service returned an empty upload response.");
        }

        stopwatch.Stop();

        // #region agent log
        try
        {
            var dbg = JsonSerializer.Serialize(new
            {
                sessionId = "487a8a",
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                runId = "upload-timeout-fix",
                hypothesisId = "H-A",
                location = "RemoteAccFileUploadService.UploadAsync",
                message = "remote upload completed",
                data = new
                {
                    displayName = request.DisplayName,
                    fileSizeBytes,
                    durationMs = stopwatch.ElapsedMilliseconds,
                    itemId = body.ItemId,
                },
            });
            File.AppendAllText(@"d:\repos2026\debug-487a8a.log", dbg + Environment.NewLine);
        }
        catch { }
        // #endregion

        return new AccFileUploadResult(
            body.FolderId,
            body.ItemId,
            body.VersionId,
            body.FileName,
            body.AlreadySameSource);
    }

    private static string BuildRequestUri(string baseUrl, string projectId)
    {
        var trimmedBaseUrl = baseUrl.TrimEnd('/');
        return $"{trimmedBaseUrl}{AccServiceContracts.ApiVersionPrefix}/acc/projects/{Uri.EscapeDataString(projectId)}/files/upload";
    }

    private sealed record RemoteAccFileUploadPayload(
        string? TargetFolderId,
        string? RootFolderId,
        IReadOnlyList<string> PathSegments,
        string DisplayName,
        string? ExistingItemId,
        AccFileSourceIdentity? SourceIdentity,
        AccFileUploadSnapshot? Snapshot,
        AccFileUploadCompanionDocument? CompanionDocument);

    private sealed record RemoteAccFileUploadResponse(
        string FolderId,
        string ItemId,
        string? VersionId,
        string FileName,
        bool AlreadySameSource);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
