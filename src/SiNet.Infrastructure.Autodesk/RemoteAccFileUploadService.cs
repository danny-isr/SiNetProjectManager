using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Configuration;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class RemoteAccFileUploadService(
    HttpClient httpClient,
    ISecretVaultStore secretVaultStore,
    IAccServiceModeProvider serviceModeProvider) : IAccFileUploadService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ISecretVaultStore _secretVaultStore = secretVaultStore;
    private readonly IAccServiceModeProvider _serviceModeProvider = serviceModeProvider;

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
            throw new InvalidOperationException("ACC service base URL is not configured for remote file upload.");
        }

        var apiKey = _secretVaultStore.GetSecret(SecretCatalog.AccServiceApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
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

        using var fileStream = File.OpenRead(request.LocalSourcePath);
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
        message.Headers.Add(AccServiceContractConstants.ApiKeyHeader, apiKey);

        using var response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content
            .ReadFromJsonAsync<RemoteAccFileUploadResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (body is null)
        {
            throw new InvalidOperationException("ACC service returned an empty upload response.");
        }

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
        return $"{trimmedBaseUrl}{AccServiceContractConstants.ApiVersionPrefix}/acc/projects/{Uri.EscapeDataString(projectId)}/files/upload";
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
