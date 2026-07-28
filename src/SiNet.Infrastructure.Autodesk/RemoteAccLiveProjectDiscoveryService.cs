using System.Net.Http.Json;
using SiOffice.AccService.Contracts;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Configuration;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class RemoteAccLiveProjectDiscoveryService(
    HttpClient httpClient,
    ISecretVaultStore secretVaultStore,
    IAccServiceModeProvider serviceModeProvider) : IAccLiveProjectDiscoveryService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ISecretVaultStore _secretVaultStore = secretVaultStore;
    private readonly IAccServiceModeProvider _serviceModeProvider = serviceModeProvider;

    public async Task<IReadOnlyList<AccHubCatalogEntry>> GetHubsAsync(CancellationToken cancellationToken = default)
    {
        var payload = await SendAsync<RemoteAccLiveHubsResponse>(
                "/acc/live/hubs",
                cancellationToken)
            .ConfigureAwait(false);

        var hubs = payload?.Hubs ?? [];
        return hubs
            .Where(static hub => !string.IsNullOrWhiteSpace(hub.HubId))
            .Select(static hub => new AccHubCatalogEntry(
                hub.HubId.Trim(),
                string.IsNullOrWhiteSpace(hub.DisplayName) ? hub.HubId.Trim() : hub.DisplayName.Trim(),
                hub.Region))
            .OrderBy(static hub => hub.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static hub => hub.HubId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<AccProjectCatalogEntry>> GetProjectsAsync(
        string hubId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hubId))
        {
            return [];
        }

        var encodedHubId = Uri.EscapeDataString(hubId.Trim());
        var payload = await SendAsync<RemoteAccLiveProjectsResponse>(
                $"/acc/live/hubs/{encodedHubId}/projects",
                cancellationToken)
            .ConfigureAwait(false);

        var projects = payload?.Projects ?? [];
        return projects
            .Where(static project => !string.IsNullOrWhiteSpace(project.ProjectId))
            .Select(static project => new AccProjectCatalogEntry(
                project.ProjectId.Trim(),
                string.IsNullOrWhiteSpace(project.DisplayName) ? project.ProjectId.Trim() : project.DisplayName.Trim(),
                "LiveAcc"))
            .OrderBy(static project => project.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static project => project.ProjectId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<T?> SendAsync<T>(string relativePath, CancellationToken cancellationToken)
    {
        var baseUrl = _serviceModeProvider.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("ACC service base URL is not configured for remote live ACC discovery.");
        }

        var apiKey = _secretVaultStore.GetSecret(SecretCatalog.AccServiceApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("ACC service API key is not configured in the native secret vault.");
        }

        var requestUri = $"{baseUrl.TrimEnd('/')}{AccServiceContracts.ApiVersionPrefix}{relativePath}";
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Add(AccServiceContracts.ApiKeyHeader, apiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}
