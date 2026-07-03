using System.Net;
using System.Net.Http.Json;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Configuration;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class RemoteAccDocumentService(
    HttpClient httpClient,
    ISecretVaultStore secretVaultStore,
    IAccServiceModeProvider serviceModeProvider) : IAccDocumentLookupBackend
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ISecretVaultStore _secretVaultStore = secretVaultStore;
    private readonly IAccServiceModeProvider _serviceModeProvider = serviceModeProvider;

    public async Task<AccItemRef?> FindItemAsync(
        string projectId,
        string folderId,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId)
            || string.IsNullOrWhiteSpace(folderId)
            || string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var baseUrl = _serviceModeProvider.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("ACC service base URL is not configured for remote document lookup.");
        }

        var apiKey = _secretVaultStore.GetSecret(SecretCatalog.AccServiceApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("ACC service API key is not configured in the native secret vault.");
        }

        var requestUri = BuildRequestUri(baseUrl, projectId, folderId, fileName);
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Add(AccServiceContractConstants.ApiKeyHeader, apiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<RemoteAccDocumentLookupResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (payload is null)
        {
            throw new InvalidOperationException("ACC service returned an empty document lookup response.");
        }

        return new AccItemRef(payload.ProjectId, payload.ItemId, payload.VersionId, payload.ViewerUrl);
    }

    private static string BuildRequestUri(string baseUrl, string projectId, string folderId, string fileName)
    {
        var trimmedBaseUrl = baseUrl.TrimEnd('/');
        return $"{trimmedBaseUrl}{AccServiceContractConstants.ApiVersionPrefix}/acc/projects/{Uri.EscapeDataString(projectId)}/folders/{Uri.EscapeDataString(folderId)}/items/resolve?fileName={Uri.EscapeDataString(fileName)}";
    }
}
