using System.Net.Http.Json;
using SiOffice.AccService.Contracts;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Configuration;
using SiNet.Application.Diagnostics;

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
            // #region agent log
            AgentDebugNdjson.Write("H1", "RemoteAccInboxBootstrapService.EnsureAsync", "missing baseUrl");
            // #endregion
            throw new InvalidOperationException("ACC service base URL is not configured for remote inbox bootstrap.");
        }

        var apiKey = _secretVaultStore.GetSecret(SecretCatalog.AccServiceApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // #region agent log
            AgentDebugNdjson.Write("H1", "RemoteAccInboxBootstrapService.EnsureAsync", "missing apiKey");
            // #endregion
            throw new InvalidOperationException("ACC service API key is not configured in the native secret vault.");
        }

        var requestUri = BuildRequestUri(baseUrl);
        // #region agent log
        AgentDebugNdjson.Write(
            "H2",
            "RemoteAccInboxBootstrapService.EnsureAsync",
            "POST inbox/ensure starting",
            new Dictionary<string, object?>
            {
                ["requestPath"] = "/v1/acc/inbox/ensure",
                ["hasApiKey"] = true,
            });
        // #endregion

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(new EmptyAccInboxBootstrapRequest()),
        };
        request.Headers.Add(AccServiceContracts.ApiKeyHeader, apiKey);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            // #region agent log
            AgentDebugNdjson.Write(
                "H2",
                "RemoteAccInboxBootstrapService.EnsureAsync",
                "POST inbox/ensure response",
                new Dictionary<string, object?>
                {
                    ["statusCode"] = (int)response.StatusCode,
                    ["isSuccess"] = response.IsSuccessStatusCode,
                });
            // #endregion
            response.EnsureSuccessStatusCode();

            var payload = await response.Content
                .ReadFromJsonAsync<RemoteAccInboxBootstrapResponse>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (payload is null)
            {
                throw new InvalidOperationException("ACC service returned an empty inbox bootstrap response.");
            }

            // #region agent log
            AgentDebugNdjson.Write(
                "H2",
                "RemoteAccInboxBootstrapService.EnsureAsync",
                "bootstrap payload ok",
                new Dictionary<string, object?>
                {
                    ["hasHubId"] = !string.IsNullOrWhiteSpace(payload.HubId),
                    ["hasProjectId"] = !string.IsNullOrWhiteSpace(payload.AccProjectId),
                    ["hasRootFolderId"] = !string.IsNullOrWhiteSpace(payload.AccRootFolderId),
                    ["hasInboxFolderId"] = !string.IsNullOrWhiteSpace(payload.AccInboxFolderId),
                    ["projectIdPrefix"] = TruncateId(payload.AccProjectId),
                    ["inboxFolderIdPrefix"] = TruncateId(payload.AccInboxFolderId),
                });
            // #endregion

            return new AccInboxBootstrapResult(
                payload.HubId,
                payload.AccProjectId,
                payload.AccRootFolderId,
                payload.AccInboxFolderId);
        }
        catch (Exception ex)
        {
            // #region agent log
            AgentDebugNdjson.Write(
                "H2",
                "RemoteAccInboxBootstrapService.EnsureAsync",
                "bootstrap failed",
                new Dictionary<string, object?>
                {
                    ["exceptionType"] = ex.GetType().Name,
                    ["message"] = ex.Message,
                });
            // #endregion
            throw;
        }
    }

    private static string? TruncateId(string? id) =>
        string.IsNullOrEmpty(id) ? null : id.Length <= 12 ? id : id[..12] + "…";

    private static string BuildRequestUri(string baseUrl) =>
        $"{baseUrl.TrimEnd('/')}{AccServiceContracts.ApiVersionPrefix}/acc/inbox/ensure";

    private sealed record EmptyAccInboxBootstrapRequest;
}
