using System.Net.Http.Json;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Configuration;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class RemoteAccItemService(
    HttpClient httpClient,
    ISecretVaultStore secretVaultStore,
    IAccServiceModeProvider serviceModeProvider) : IAccItemService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ISecretVaultStore _secretVaultStore = secretVaultStore;
    private readonly IAccServiceModeProvider _serviceModeProvider = serviceModeProvider;

    public async Task<string?> GetDisplayNameAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Get, projectId, itemId, "display-name", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<RemoteAccItemDisplayNameResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (payload is null)
        {
            throw new InvalidOperationException("ACC service returned an empty item display-name response.");
        }

        return payload.DisplayName;
    }

    public async Task<int?> GetVersionCountAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Get, projectId, itemId, "version-count", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<RemoteAccItemVersionCountResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (payload is null)
        {
            throw new InvalidOperationException("ACC service returned an empty item version-count response.");
        }

        return payload.VersionCount;
    }

    public async Task<bool> HideAsync(
        string projectId,
        string itemId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(HttpMethod.Post, projectId, itemId, "hide", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<RemoteAccItemHideResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (payload is null)
        {
            throw new InvalidOperationException("ACC service returned an empty item hide response.");
        }

        return payload.Ok;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string projectId,
        string itemId,
        string action,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(itemId))
        {
            throw new ArgumentException("ACC project id and item id are required.");
        }

        var baseUrl = _serviceModeProvider.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("ACC service base URL is not configured for remote item operations.");
        }

        var apiKey = _secretVaultStore.GetSecret(SecretCatalog.AccServiceApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("ACC service API key is not configured in the native secret vault.");
        }

        var requestUri = $"{baseUrl.TrimEnd('/')}{AccServiceContractConstants.ApiVersionPrefix}/acc/projects/{Uri.EscapeDataString(projectId.Trim())}/items/{Uri.EscapeDataString(itemId.Trim())}/{action}";
        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Add(AccServiceContractConstants.ApiKeyHeader, apiKey);

        return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
