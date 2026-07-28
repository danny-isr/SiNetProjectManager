using System.Net.Http.Json;
using SiOffice.AccService.Contracts;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Configuration;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class RemoteAccInboxBootstrapService(
    HttpClient httpClient,
    ISecretVaultStore secretVaultStore,
    IAccServiceModeProvider serviceModeProvider) : IAccInboxBootstrapService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ISecretVaultStore _secretVaultStore = secretVaultStore;
    private readonly IAccServiceModeProvider _serviceModeProvider = serviceModeProvider;

    public async Task<AccInboxBootstrapResult> EnsureAsync(CancellationToken cancellationToken = default)
    {
        var baseUrl = _serviceModeProvider.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("ACC service base URL is not configured for remote inbox bootstrap.");
        }

        var apiKey = _secretVaultStore.GetSecret(SecretCatalog.AccServiceApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("ACC service API key is not configured in the native secret vault.");
        }

        var requestUri = BuildRequestUri(baseUrl);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(new EmptyAccInboxBootstrapRequest()),
        };
        request.Headers.Add(AccServiceContracts.ApiKeyHeader, apiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<RemoteAccInboxBootstrapResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (payload is null)
        {
            throw new InvalidOperationException("ACC service returned an empty inbox bootstrap response.");
        }

        return new AccInboxBootstrapResult(
            payload.HubId,
            payload.AccProjectId,
            payload.AccRootFolderId,
            payload.AccInboxFolderId);
    }

    private static string BuildRequestUri(string baseUrl) =>
        $"{baseUrl.TrimEnd('/')}{AccServiceContracts.ApiVersionPrefix}/acc/inbox/ensure";

    private sealed record EmptyAccInboxBootstrapRequest;
}
