using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Authentication;
using Serilog;
using SiNetSQL.Services.AccBootstrap;
using SiOffice.AccService.Contracts;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// HTTP client implementation of <see cref="IAccInboxProvisioner"/> that forwards
/// inbox bootstrap to <c>POST /v1/acc/inbox/ensure</c> on SiOffice.AccService.
/// 
/// Used by <see cref="SiNetSQL.Services.EmailIngestionServiceFactory"/> when running
/// in a multi-user deployment where regular users don't hold ACC Account Admin
/// credentials. The shared <c>X-AccService-Key</c> header is added by the typed-client
/// registration in <c>App.xaml.cs</c>.
/// </summary>
public sealed class RemoteAccInboxProvisioner : IAccInboxProvisioner
{
    private readonly HttpClient _http;

    public RemoteAccInboxProvisioner(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <inheritdoc/>
    public async Task<(string AccProjectId, string AccInboxFolderId)> EnsureAsync(CancellationToken cancellationToken)
    {
        const string operation = "EnsureInboxAsync";
        const string relativeUrl = "v1/acc/inbox/ensure";
        LogRequestStart(operation, "POST", relativeUrl);

        try
        {
            // Empty body = let the service resolve everything from SystemSettings + vault.
            using var resp = await _http.PostAsJsonAsync(
                relativeUrl,
                new EnsureInboxRequest(),
                cancellationToken);

            if (!resp.IsSuccessStatusCode)
            {
                ErrorDto? err = null;
                string rawBody = "";
                try
                {
                    rawBody = await resp.Content.ReadAsStringAsync(cancellationToken);
                    err = System.Text.Json.JsonSerializer.Deserialize<ErrorDto>(rawBody);
                }
                catch { /* fall through to status-code-only message */ }

                var truncatedBody = rawBody.Length > 500 ? rawBody[..500] + "..." : rawBody;
                var errorCategory = (int)resp.StatusCode switch
                {
                    401 or 403 => "ApiKeyRejected",
                    400 => "BadRequest",
                    404 => "NotFound",
                    >= 500 and < 600 => "ServerError",
                    _ => $"Http{(int)resp.StatusCode}"
                };

                Log.Error(
                    "[AccService] {Operation} FAILED — category={Category}, http={StatusCode}, " +
                    "method=POST, url={Url}, baseAddress={BaseAddress}, responseBody={ResponseBody}.",
                    operation, errorCategory, (int)resp.StatusCode, relativeUrl,
                    _http.BaseAddress?.ToString() ?? "(null)", truncatedBody);

                throw new InvalidOperationException(
                    $"AccService returned {(int)resp.StatusCode} {resp.ReasonPhrase}" +
                    (err is null ? "" : $": {err.Error}{(err.Detail is null ? "" : $" — {err.Detail}")}"));
            }

            LogRequestSuccess(operation, (int)resp.StatusCode);

            var body = await resp.Content.ReadFromJsonAsync<EnsureInboxResponse>(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("AccService returned empty inbox-ensure response.");

            return (body.AccProjectId, body.AccInboxFolderId);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            LogRequestException(operation, ex, cancellationToken);
            throw;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Diagnostic logging helpers (same pattern as RemoteAccProjectProvisioningService)
    // ═══════════════════════════════════════════════════════════════════════════

    private void LogRequestStart(string operation, string method, string relativeUrl)
    {
        var baseAddress = _http.BaseAddress?.ToString() ?? "(null)";
        // Header presence only — a key hash prefix is a secret fingerprint and stays out of the log.
        var hasApiKeyHeader = _http.DefaultRequestHeaders.Contains(AccServiceContracts.ApiKeyHeader);

        Log.Information(
            "[AccService] {Operation} START — method={Method}, url={RelativeUrl}, baseAddress={BaseAddress}, " +
            "hasApiKeyHeader={HasApiKeyHeader}.",
            operation, method, relativeUrl, baseAddress, hasApiKeyHeader);
    }

    private static void LogRequestSuccess(string operation, int statusCode)
    {
        Log.Information("[AccService] {Operation} SUCCESS — http={StatusCode}.", operation, statusCode);
    }

    private void LogRequestException(string operation, Exception ex, CancellationToken ct)
    {
        var errorCategory = ex switch
        {
            TaskCanceledException when ct.IsCancellationRequested => "Cancelled",
            TaskCanceledException or OperationCanceledException => "Timeout",
            HttpRequestException { InnerException: SocketException { SocketErrorCode: SocketError.ConnectionRefused } }
                => "ConnectionRefused",
            HttpRequestException { InnerException: SocketException { SocketErrorCode: SocketError.HostNotFound } }
                => "DnsResolutionFailed",
            HttpRequestException { InnerException: AuthenticationException } => "SslCertificateError",
            HttpRequestException hre => $"HttpError_{(int?)hre.StatusCode}",
            _ => "UnknownError"
        };
        var innerMsg = ex.InnerException?.Message;

        Log.Error(ex,
            "[AccService] {Operation} FAILED — category={Category}, exceptionType={ExType}, message={Message}, " +
            "innerException={InnerMessage}, baseAddress={BaseAddress}.",
            operation, errorCategory, ex.GetType().Name, ex.Message, innerMsg ?? "(none)",
            _http.BaseAddress?.ToString() ?? "(null)");
    }
}
