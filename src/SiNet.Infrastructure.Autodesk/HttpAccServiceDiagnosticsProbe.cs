using System.Text.Json;
using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Autodesk;

/// <summary>HTTP probe over the auth-exempt <c>/v1/acc/diag</c> endpoint.</summary>
public sealed class HttpAccServiceDiagnosticsProbe(
    HttpClient httpClient,
    IAccServiceModeProvider modeProvider,
    AccServiceControlPlaneOptions options) : IAccServiceDiagnosticsProbe
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly IAccServiceModeProvider _modeProvider = modeProvider ?? throw new ArgumentNullException(nameof(modeProvider));
    private readonly AccServiceControlPlaneOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    public async Task<AccServiceDiagnosticsResult> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var endpoint = BuildEndpoint("/acc/diag");
        if (endpoint is null)
        {
            return new AccServiceDiagnosticsResult(
                Reachable: false,
                WindowsUser: null,
                HasApiKey: false,
                KeySource: null,
                KeyLength: 0,
                KeyHashPrefix: null,
                AutodeskOk: false,
                AutodeskDetail: "AccService:BaseUrl is not configured.",
                DbOk: false,
                DbDetail: "AccService:BaseUrl is not configured.");
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.DiagnosticsTimeout);

            using var response = await _httpClient.GetAsync(endpoint, cts.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new AccServiceDiagnosticsResult(
                    Reachable: false,
                    WindowsUser: null,
                    HasApiKey: false,
                    KeySource: null,
                    KeyLength: 0,
                    KeyHashPrefix: null,
                    AutodeskOk: false,
                    AutodeskDetail: $"HTTP {(int)response.StatusCode}",
                    DbOk: false,
                    DbDetail: Truncate(body, 200));
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            return new AccServiceDiagnosticsResult(
                Reachable: true,
                WindowsUser: GetString(root, "windowsUser"),
                HasApiKey: GetBool(root, "hasApiKey"),
                KeySource: GetString(root, "keySource"),
                KeyLength: GetInt(root, "keyLength"),
                KeyHashPrefix: GetString(root, "keyHashPrefix"),
                AutodeskOk: GetBool(root, "autodeskStatus"),
                AutodeskDetail: GetString(root, "autodeskDetail"),
                DbOk: GetBool(root, "dbStatus"),
                DbDetail: GetString(root, "dbDetail"));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AccServiceDiagnosticsResult(
                Reachable: false,
                WindowsUser: null,
                HasApiKey: false,
                KeySource: null,
                KeyLength: 0,
                KeyHashPrefix: null,
                AutodeskOk: false,
                AutodeskDetail: "Timeout",
                DbOk: false,
                DbDetail: "Timeout");
        }
        catch (Exception ex)
        {
            return new AccServiceDiagnosticsResult(
                Reachable: false,
                WindowsUser: null,
                HasApiKey: false,
                KeySource: null,
                KeyLength: 0,
                KeyHashPrefix: null,
                AutodeskOk: false,
                AutodeskDetail: ex.Message,
                DbOk: false,
                DbDetail: ex.Message);
        }
    }

    private string? BuildEndpoint(string relativePath)
    {
        if (_modeProvider.Mode != AccServiceMode.Remote || string.IsNullOrWhiteSpace(_modeProvider.BaseUrl))
        {
            return null;
        }

        return _modeProvider.BaseUrl + AccServiceContractConstants.ApiVersionPrefix + relativePath;
    }

    private static bool GetBool(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind == JsonValueKind.True;
    }

    private static int GetInt(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : 0;

    private static string? GetString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
