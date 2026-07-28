using System.Net;
using SiOffice.AccService.Contracts;
using System.Net.Http.Json;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Configuration;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class RemoteAccFolderBrowserService(
    HttpClient httpClient,
    ISecretVaultStore secretVaultStore,
    IAccServiceModeProvider serviceModeProvider) : IAccFolderBrowserService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ISecretVaultStore _secretVaultStore = secretVaultStore;
    private readonly IAccServiceModeProvider _serviceModeProvider = serviceModeProvider;

    public async Task<AccFolderBrowseResult?> BrowseAsync(
        string projectId,
        string? folderId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return null;
        }

        var baseUrl = _serviceModeProvider.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("ACC service base URL is not configured for remote folder browsing.");
        }

        var apiKey = _secretVaultStore.GetSecret(SecretCatalog.AccServiceApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("ACC service API key is not configured in the native secret vault.");
        }

        var requestUri = BuildRequestUri(baseUrl, projectId, folderId);
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Add(AccServiceContracts.ApiKeyHeader, apiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<RemoteAccFolderBrowseResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (payload is null)
        {
            throw new InvalidOperationException("ACC service returned an empty folder browse response.");
        }

        var entries = (payload.Entries ?? [])
            .Select(static entry => new AccFolderBrowseEntry(
                entry.Id,
                entry.DisplayName,
                (AccFolderEntryKind)entry.Kind,
                entry.FileSize,
                entry.LastModifiedTime,
                entry.CreateTime))
            .ToArray();

        return new AccFolderBrowseResult(payload.ProjectId, payload.FolderId, entries);
    }

    private static string BuildRequestUri(string baseUrl, string projectId, string? folderId)
    {
        var trimmedBaseUrl = baseUrl.TrimEnd('/');
        var requestUri = $"{trimmedBaseUrl}{AccServiceContracts.ApiVersionPrefix}/acc/projects/{Uri.EscapeDataString(projectId.Trim())}/folders/browse";
        if (!string.IsNullOrWhiteSpace(folderId))
        {
            requestUri += $"?folderId={Uri.EscapeDataString(folderId.Trim())}";
        }

        return requestUri;
    }
}
