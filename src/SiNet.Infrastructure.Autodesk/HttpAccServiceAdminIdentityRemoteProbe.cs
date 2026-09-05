using System.Net.Http;
using System.Text.Json;
using SiOffice.AccService.Contracts;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Configuration;
using SiNet.Application.Identity;

namespace SiNet.Infrastructure.Autodesk;

/// <summary>HTTP probe over authenticated <c>/v1/acc/admin-identity</c>.</summary>
public sealed class HttpAccServiceAdminIdentityRemoteProbe(
    HttpClient httpClient,
    IAccServiceModeProvider modeProvider,
    ISecretVaultStore secretVaultStore,
    AccServiceControlPlaneOptions options) : IAccServiceAdminIdentityRemoteProbe
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly IAccServiceModeProvider _modeProvider = modeProvider ?? throw new ArgumentNullException(nameof(modeProvider));
    private readonly ISecretVaultStore _secretVaultStore = secretVaultStore ?? throw new ArgumentNullException(nameof(secretVaultStore));
    private readonly AccServiceControlPlaneOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    public async Task<AccServiceAdminIdentityRemoteResult> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var endpoint = BuildEndpoint("/acc/admin-identity");
        if (endpoint is null)
        {
            return Unreachable("AccService:BaseUrl is not configured (or mode is Local).");
        }

        var apiKey = _secretVaultStore.GetSecret(SecretCatalog.AccServiceApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Unreachable("AccService API key is not configured in the client vault.");
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.DiagnosticsTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Add(AccServiceContracts.ApiKeyHeader, apiKey);

            using var response = await _httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Unreachable($"HTTP {(int)response.StatusCode}");
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            return new AccServiceAdminIdentityRemoteResult(
                Reachable: true,
                ExpectedAdminEmail: GetString(root, "expectedAdminEmail"),
                ActualAdminEmail: GetString(root, "actualAdminEmail"),
                TokenAvailable: GetBool(root, "tokenAvailable"),
                ProfileResolved: GetBool(root, "profileResolved"),
                AutodeskUserId: GetString(root, "autodeskUserId"),
                DisplayName: GetString(root, "displayName"),
                EmailMatch: GetBool(root, "emailMatch"),
                IdentityStatus: GetString(root, "identityStatus") ?? GetString(root, "status"),
                AdminApiStatus: GetString(root, "adminApiStatus"),
                FailureReason: GetString(root, "failureReason"),
                Detail: null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unreachable("Timeout");
        }
        catch (Exception ex)
        {
            return Unreachable(ex.Message);
        }
    }

    private string? BuildEndpoint(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(_modeProvider.BaseUrl))
        {
            return null;
        }

        // Admin identity is always on AccService — probe whenever a BaseUrl is configured,
        // including DEV machines that run AccService alongside the WPF host.
        return _modeProvider.BaseUrl.TrimEnd('/') + AccServiceContracts.ApiVersionPrefix + relativePath;
    }

    private static AccServiceAdminIdentityRemoteResult Unreachable(string detail) =>
        new(
            Reachable: false,
            ExpectedAdminEmail: null,
            ActualAdminEmail: null,
            TokenAvailable: false,
            ProfileResolved: false,
            AutodeskUserId: null,
            DisplayName: null,
            EmailMatch: false,
            IdentityStatus: AccServiceAdminIdentityStatus.ServiceUnavailable.ToString(),
            AdminApiStatus: null,
            FailureReason: detail,
            Detail: detail);

    private static bool GetBool(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True;

    private static string? GetString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
