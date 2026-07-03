using System.Net.Http.Json;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Configuration;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class RemoteAccProjectCatalogService(
    HttpClient httpClient,
    ISecretVaultStore secretVaultStore,
    IAccServiceModeProvider serviceModeProvider) : IAccProjectCatalogService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ISecretVaultStore _secretVaultStore = secretVaultStore;
    private readonly IAccServiceModeProvider _serviceModeProvider = serviceModeProvider;

    public async Task<IReadOnlyList<AccProjectCatalogEntry>> GetProjectsAsync(CancellationToken cancellationToken = default)
    {
        var baseUrl = _serviceModeProvider.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("ACC service base URL is not configured for remote project catalog discovery.");
        }

        var apiKey = _secretVaultStore.GetSecret(SecretCatalog.AccServiceApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("ACC service API key is not configured in the native secret vault.");
        }

        var requestUri = $"{baseUrl.TrimEnd('/')}{AccServiceContractConstants.ApiVersionPrefix}/acc/projects/catalog";
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Add(AccServiceContractConstants.ApiKeyHeader, apiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<RemoteAccProjectCatalogResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var projects = payload?.Projects ?? [];
        return projects
            .Where(static project => !string.IsNullOrWhiteSpace(project.ProjectId))
            .Select(static project => new AccProjectCatalogEntry(
                project.ProjectId.Trim(),
                string.IsNullOrWhiteSpace(project.DisplayName) ? project.ProjectId.Trim() : project.DisplayName.Trim(),
                string.IsNullOrWhiteSpace(project.SourceLabel) ? "RemoteCatalog" : project.SourceLabel.Trim()))
            .GroupBy(static project => project.ProjectId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group
                .OrderBy(static project => project.DisplayName, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(static project => project.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static project => project.ProjectId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
