using SiNet.Application.Abstractions.Autodesk;
using SiOffice.AccService.Contracts;

namespace SiNet.Infrastructure.Autodesk;

/// <summary>HTTP probe over the auth-exempt <c>/v1/acc/health</c> endpoint.</summary>
public sealed class HttpAccServiceHealthProbe(
    HttpClient httpClient,
    IAccServiceModeProvider modeProvider,
    AccServiceControlPlaneOptions options) : IAccServiceHealthProbe
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly IAccServiceModeProvider _modeProvider = modeProvider ?? throw new ArgumentNullException(nameof(modeProvider));
    private readonly AccServiceControlPlaneOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    public async Task<AccServiceHealthResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var endpoint = BuildEndpoint("/acc/health");
        if (endpoint is null)
        {
            return new AccServiceHealthResult(
                IsConfigured: false,
                State: AccServiceHealthState.NotConfigured,
                Endpoint: null,
                Detail: "AccService:BaseUrl is not configured.");
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.HealthTimeout);

            using var response = await _httpClient.GetAsync(endpoint, cts.Token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return new AccServiceHealthResult(
                    IsConfigured: true,
                    State: AccServiceHealthState.Online,
                    Endpoint: endpoint,
                    Detail: "Connected");
            }

            return new AccServiceHealthResult(
                IsConfigured: true,
                State: AccServiceHealthState.Offline,
                Endpoint: endpoint,
                Detail: $"HTTP {(int)response.StatusCode}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AccServiceHealthResult(
                IsConfigured: true,
                State: AccServiceHealthState.Offline,
                Endpoint: endpoint,
                Detail: "Timeout");
        }
        catch (Exception ex)
        {
            return new AccServiceHealthResult(
                IsConfigured: true,
                State: AccServiceHealthState.Offline,
                Endpoint: endpoint,
                Detail: ex.GetType().Name + ": " + ex.Message);
        }
    }

    private string? BuildEndpoint(string relativePath)
    {
        if (_modeProvider.Mode != AccServiceMode.Remote || string.IsNullOrWhiteSpace(_modeProvider.BaseUrl))
        {
            return null;
        }

        return _modeProvider.BaseUrl + AccServiceContracts.ApiVersionPrefix + relativePath;
    }
}
