using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SiNetSQL.Services.Health;

namespace SiNetProjectManagerV2.Services.Health;

/// <summary>
/// Probes the internal SiOffice.AccService over GET <c>/v1/acc/health</c> when
/// <c>AccService:BaseUrl</c> is configured. When the setting is absent the check
/// reports <see cref="ServiceHealthState.NotConfigured"/> without falling back to
/// any local provisioning path.
/// </summary>
public sealed class InternalAccServiceHealthCheck : IServiceHealthCheck
{
    private static readonly HttpClient _http = CreateClient();

    public string Key => "acc-service";
    public string DisplayName => "SiOffice.AccService (פנימי)";
    public string Category => "Core";
    public bool IsCritical => true;

    public async Task<ServiceHealthStatus> CheckAsync(CancellationToken ct)
    {
        var status = new ServiceHealthStatus
        {
            Key = Key,
            DisplayName = DisplayName,
            Category = Category,
            IsCritical = IsCritical,
        };

        var baseUrl = AppConfiguration.Configuration["AccService:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            status.State = ServiceHealthState.NotConfigured;
            status.Message = "ריצה במצב מקומי (AccService:BaseUrl לא מוגדר)";
            return status;
        }

        // Server maps the route as `<ApiVersionPrefix>/acc/health` (i.e. /v1/acc/health).
        // Using `/acc/health` returns 404 even when the service is up, which previously
        // pinned the indicator to Offline/Red after a recovery.
        var url = baseUrl.TrimEnd('/') + SiOffice.AccService.Contracts.AccServiceContracts.ApiVersionPrefix + "/acc/health";
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            using var resp = await _http.GetAsync(url, cts.Token).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                status.State = ServiceHealthState.Online;
                status.Message = "מחובר";
                status.TechnicalDetails = url;
            }
            else
            {
                status.State = ServiceHealthState.Offline;
                status.Message = $"AccService החזיר {(int)resp.StatusCode}";
                status.TechnicalDetails = url;
            }
        }
        catch (TaskCanceledException)
        {
            status.State = ServiceHealthState.Offline;
            status.Message = "AccService לא מגיב (timeout)";
            status.TechnicalDetails = url;
        }
        catch (Exception ex)
        {
            status.State = ServiceHealthState.Offline;
            status.Message = "כשל בגישה ל-AccService";
            status.TechnicalDetails = ex.GetType().Name + ": " + ex.Message;
        }

        return status;
    }

    // Approved internal hosts for self-signed certificate acceptance (same list as App.xaml.cs)
    private static readonly HashSet<string> _approvedInternalHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "SI-WIN-2K19",
        "localhost",
        "127.0.0.1"
    };

    private static bool IsApprovedInternalHost(string? host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        if (_approvedInternalHosts.Contains(host)) return true;
        if (host.EndsWith(".si-eng.local", StringComparison.OrdinalIgnoreCase)) return true;
        if (host.StartsWith("192.168.", StringComparison.Ordinal)) return true;
        return false;
    }

    private static HttpClient CreateClient()
    {
        // Accept self-signed certificates ONLY for approved internal hosts.
        // This mirrors the policy in App.xaml.cs for AccService typed clients.
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) =>
            {
                if (errors == System.Net.Security.SslPolicyErrors.None)
                    return true;

                // Self-signed (chain errors) — only accept for loopback or approved hosts
                if (errors == System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors)
                {
                    var host = msg?.RequestUri?.Host;
                    return msg?.RequestUri?.IsLoopback == true || IsApprovedInternalHost(host);
                }

                return false;
            }
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
    }
}
