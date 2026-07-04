using System.Net.Http.Json;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Configuration;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class RemoteAccProjectTreeSearchService(
    HttpClient httpClient,
    ISecretVaultStore secretVaultStore,
    IAccServiceModeProvider serviceModeProvider) : IAccProjectTreeSearchService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ISecretVaultStore _secretVaultStore = secretVaultStore;
    private readonly IAccServiceModeProvider _serviceModeProvider = serviceModeProvider;

    public async Task<AccProjectTreeSearchResult> SearchAsync(
        string projectId,
        string fileName,
        string? folderId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(fileName))
        {
            return new AccProjectTreeSearchResult([], 0, false, false);
        }

        var baseUrl = _serviceModeProvider.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("ACC service base URL is not configured for remote project tree search.");
        }

        var apiKey = _secretVaultStore.GetSecret(SecretCatalog.AccServiceApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("ACC service API key is not configured in the native secret vault.");
        }

        var requestUri = BuildRequestUri(baseUrl, projectId, fileName, folderId);
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Add(AccServiceContractConstants.ApiKeyHeader, apiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<RemoteAccProjectTreeSearchResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (payload is null)
        {
            throw new InvalidOperationException("ACC service returned an empty project tree search response.");
        }

        return new AccProjectTreeSearchResult(
            payload.Matches ?? [],
            payload.VisitedFolderCount,
            payload.HitFolderLimit,
            payload.HitResultLimit);
    }

    private static string BuildRequestUri(string baseUrl, string projectId, string fileName, string? folderId)
    {
        var trimmedBaseUrl = baseUrl.TrimEnd('/');
        var requestUri =
            $"{trimmedBaseUrl}{AccServiceContractConstants.ApiVersionPrefix}/acc/projects/{Uri.EscapeDataString(projectId.Trim())}/folders/search?fileName={Uri.EscapeDataString(fileName.Trim())}";
        if (!string.IsNullOrWhiteSpace(folderId))
        {
            requestUri += $"&folderId={Uri.EscapeDataString(folderId.Trim())}";
        }

        return requestUri;
    }
}
