using System.Net;
using SiOffice.AccService.Contracts;
using System.Net.Http.Json;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Configuration;

namespace SiNet.Infrastructure.Autodesk;

internal sealed class RemoteAccFolderPathService(
    HttpClient httpClient,
    ISecretVaultStore secretVaultStore,
    IAccServiceModeProvider serviceModeProvider,
    IAppLogger logger) : IAccFolderPathService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ISecretVaultStore _secretVaultStore = secretVaultStore;
    private readonly IAccServiceModeProvider _serviceModeProvider = serviceModeProvider;
    private readonly IAppLogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public Task<string?> TryResolvePathAsync(
        string projectId,
        string rootFolderId,
        IReadOnlyList<string> pathSegments,
        CancellationToken cancellationToken = default) =>
        SendAsync(projectId, rootFolderId, pathSegments, ensurePath: false, cancellationToken);

    public async Task<string> EnsurePathAsync(
        string projectId,
        string rootFolderId,
        IReadOnlyList<string> pathSegments,
        CancellationToken cancellationToken = default)
    {
        var folderId = await SendAsync(projectId, rootFolderId, pathSegments, ensurePath: true, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(folderId))
        {
            _logger.Error(
                $"[AccEnsurePath] outcome=Failed project={projectId} root={rootFolderId} detail=empty folder id");
            throw new InvalidOperationException("ACC service returned an empty folder id for ensure-path.");
        }

        return folderId;
    }

    private async Task<string?> SendAsync(
        string projectId,
        string rootFolderId,
        IReadOnlyList<string> pathSegments,
        bool ensurePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(rootFolderId))
        {
            return null;
        }

        var baseUrl = _serviceModeProvider.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.Error($"[AccRemote] missing BaseUrl op={(ensurePath ? "AccEnsurePath" : "AccResolvePath")}");
            throw new InvalidOperationException("ACC service base URL is not configured for remote folder path resolution.");
        }

        var apiKey = _secretVaultStore.GetSecret(SecretCatalog.AccServiceApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.Error($"[AccRemote] missing ApiKey op={(ensurePath ? "AccEnsurePath" : "AccResolvePath")}");
            throw new InvalidOperationException("ACC service API key is not configured in the native secret vault.");
        }

        var requestUri = BuildRequestUri(baseUrl, projectId, ensurePath);
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(new RemoteAccFolderPathRequest(
                rootFolderId.Trim(),
                pathSegments
                    .Where(static segment => !string.IsNullOrWhiteSpace(segment))
                    .Select(static segment => segment.Trim())
                    .ToArray())),
        };
        request.Headers.Add(AccServiceContracts.ApiKeyHeader, apiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!ensurePath && response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            if (ensurePath)
            {
                _logger.Error(
                    $"[AccEnsurePath] outcome=Failed project={projectId} root={rootFolderId} http={(int)response.StatusCode} detail={response.ReasonPhrase}");
            }

            response.EnsureSuccessStatusCode();
        }

        var payload = await response.Content
            .ReadFromJsonAsync<RemoteAccFolderPathResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (payload is null)
        {
            if (ensurePath)
            {
                _logger.Error(
                    $"[AccEnsurePath] outcome=Failed project={projectId} root={rootFolderId} detail=empty folder path response");
            }

            throw new InvalidOperationException("ACC service returned an empty folder path response.");
        }

        return payload.FolderId;
    }

    private static string BuildRequestUri(string baseUrl, string projectId, bool ensurePath)
    {
        var trimmedBaseUrl = baseUrl.TrimEnd('/');
        var action = ensurePath ? "ensure-path" : "resolve-path";
        return $"{trimmedBaseUrl}{AccServiceContracts.ApiVersionPrefix}/acc/projects/{Uri.EscapeDataString(projectId.Trim())}/folders/{action}";
    }

    private sealed record RemoteAccFolderPathRequest(string RootFolderId, IReadOnlyList<string> PathSegments);
}
