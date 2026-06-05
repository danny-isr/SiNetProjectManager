using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Authentication;
using Serilog;
using SiNetSQL.Services.AccBootstrap;
using SiNetSQL.Services.AccBootstrap.Contracts;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// HTTP client implementation of <see cref="IAccProjectProvisioningService"/> that
/// forwards every privileged ACC operation to the SiOffice.AccService running on the
/// office server. Eliminates the need for the WPF client to hold Autodesk Account
/// Admin credentials — only the service does.
/// </summary>
/// <remarks>
/// <para>
/// Authentication: shared API key in <c>X-AccService-Key</c> header. The key + base URL
/// are wired by <see cref="AddSiOfficeAccServiceClient"/> from the same vault used for
/// every other secret (key <c>SiNet/AccService/ApiKey</c> = <see cref="SiNetSQL.Services.SecretKeys.AccServiceApiKey"/>).
/// </para>
/// <para>
/// Long-running endpoints (e.g. <see cref="EnsureProjectMappingAsync"/> can take 1–2 minutes
/// while ACC provisions Docs) rely on the per-request <see cref="CancellationToken"/> rather
/// than a hard <see cref="HttpClient.Timeout"/> — the typed-client registration sets
/// <see cref="HttpClient.Timeout"/> to <see cref="Timeout.InfiniteTimeSpan"/>.
/// </para>
/// </remarks>
public sealed class RemoteAccProjectProvisioningService : IAccProjectProvisioningService
{
    private readonly HttpClient _http;

    public RemoteAccProjectProvisioningService(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <inheritdoc/>
    public async Task<ProjectAccTargets> EnsureProjectMappingAsync(int projectId, CancellationToken cancellationToken)
    {
        const string operation = "EnsureProjectMappingAsync";
        const string relativeUrl = "v1/acc/projects/ensure-mapping";
        LogRequestStart(operation, "POST", relativeUrl);

        try
        {
            using var resp = await _http.PostAsJsonAsync(
                relativeUrl,
                new EnsureProjectMappingRequest(projectId),
                cancellationToken);
            await EnsureSuccessAsync(resp, operation, cancellationToken);
            LogRequestSuccess(operation, (int)resp.StatusCode);
            return await resp.Content.ReadFromJsonAsync<ProjectAccTargets>(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("AccService returned an empty body for ensure-mapping.");
        }
        catch (Exception ex) when (ex is not HttpRequestException)
        {
            LogRequestException(operation, ex, cancellationToken);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task ReconcileProjectMembersAsync(string accProjectId, CancellationToken cancellationToken)
    {
        const string operation = "ReconcileProjectMembersAsync";
        var relativeUrl = $"v1/acc/projects/{Uri.EscapeDataString(accProjectId)}/members/reconcile";
        LogRequestStart(operation, "POST", relativeUrl);

        try
        {
            using var resp = await _http.PostAsync(relativeUrl, content: null, cancellationToken);
            await EnsureSuccessAsync(resp, operation, cancellationToken);
            LogRequestSuccess(operation, (int)resp.StatusCode);
        }
        catch (Exception ex) when (ex is not HttpRequestException)
        {
            LogRequestException(operation, ex, cancellationToken);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<string> ReconcileAllProjectsAsync(CancellationToken cancellationToken)
    {
        const string operation = "ReconcileAllProjectsAsync";
        const string relativeUrl = "v1/acc/projects/reconcile-all";
        LogRequestStart(operation, "POST", relativeUrl);

        try
        {
            using var resp = await _http.PostAsync(relativeUrl, content: null, cancellationToken);
            await EnsureSuccessAsync(resp, operation, cancellationToken);
            LogRequestSuccess(operation, (int)resp.StatusCode);
            var dto = await resp.Content.ReadFromJsonAsync<SummaryDto>(cancellationToken: cancellationToken);
            return dto?.Summary ?? string.Empty;
        }
        catch (Exception ex) when (ex is not HttpRequestException)
        {
            LogRequestException(operation, ex, cancellationToken);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> EnsureCustomAttributeDefinitionsAsync(
        string accProjectId, string accFolderId, int? siProjectId, CancellationToken cancellationToken)
    {
        const string operation = "EnsureCustomAttributeDefinitionsAsync";
        var relativeUrl = $"v1/acc/projects/{Uri.EscapeDataString(accProjectId)}/attribute-defs/ensure";
        LogRequestStart(operation, "POST", relativeUrl);

        try
        {
            using var resp = await _http.PostAsJsonAsync(
                relativeUrl,
                new EnsureAttributeDefsRequest(accFolderId, siProjectId),
                cancellationToken);
            await EnsureSuccessAsync(resp, operation, cancellationToken);
            LogRequestSuccess(operation, (int)resp.StatusCode);
            var dto = await resp.Content.ReadFromJsonAsync<BoolResultDto>(cancellationToken: cancellationToken);
            return dto?.Success == true;
        }
        catch (Exception ex) when (ex is not HttpRequestException)
        {
            LogRequestException(operation, ex, cancellationToken);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<(string Id, string Name)>> ListAvailableTemplatesAsync(CancellationToken cancellationToken)
    {
        const string operation = "ListAvailableTemplatesAsync";
        const string relativeUrl = "v1/acc/templates";
        LogRequestStart(operation, "GET", relativeUrl);

        try
        {
            using var resp = await _http.GetAsync(relativeUrl, cancellationToken);
            await EnsureSuccessAsync(resp, operation, cancellationToken);
            LogRequestSuccess(operation, (int)resp.StatusCode);
            var list = await resp.Content.ReadFromJsonAsync<List<AccTemplateDto>>(cancellationToken: cancellationToken)
                ?? new List<AccTemplateDto>();
            return list.Select(t => (t.Id, t.Name)).ToList();
        }
        catch (Exception ex) when (ex is not HttpRequestException)
        {
            LogRequestException(operation, ex, cancellationToken);
            throw;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Diagnostic-only probes are not exposed over HTTP. They run against a freshly
    /// created throwaway ACC project and were used to validate the template-permissions
    /// hypothesis during Phase A. If they need to run again, invoke them directly on a
    /// machine where the local <c>AccProjectProvisioningService</c> is registered.
    /// </remarks>
    public Task<string> ProbeFolderPermissionsAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException(
            "ProbeFolderPermissionsAsync is a diagnostic-only API and is not exposed by SiOffice.AccService. " +
            "Run it locally with the in-process AccProjectProvisioningService implementation.");

    /// <inheritdoc/>
    public Task<string> ProbeFolderPermissionsFromTemplateAsync(string templateName, CancellationToken cancellationToken)
        => throw new NotSupportedException(
            "ProbeFolderPermissionsFromTemplateAsync is a diagnostic-only API and is not exposed by SiOffice.AccService.");

    // ═══════════════════════════════════════════════════════════════════════════
    //  Diagnostic logging helpers
    // ═══════════════════════════════════════════════════════════════════════════

    private void LogRequestStart(string operation, string method, string relativeUrl)
    {
        var baseAddress = _http.BaseAddress?.ToString() ?? "(null)";
        var hasApiKeyHeader = _http.DefaultRequestHeaders.Contains(AccServiceContracts.ApiKeyHeader);
        var keyHashPrefix = "(none)";
        if (hasApiKeyHeader && _http.DefaultRequestHeaders.TryGetValues(AccServiceContracts.ApiKeyHeader, out var values))
        {
            var key = values.FirstOrDefault();
            if (!string.IsNullOrEmpty(key))
            {
                var hashBytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key));
                keyHashPrefix = Convert.ToHexString(hashBytes)[..12].ToLowerInvariant();
            }
        }

        Log.Information(
            "[AccService] {Operation} START — method={Method}, url={RelativeUrl}, baseAddress={BaseAddress}, " +
            "hasApiKeyHeader={HasApiKeyHeader}, keyHashPrefix={KeyHashPrefix}.",
            operation, method, relativeUrl, baseAddress, hasApiKeyHeader, keyHashPrefix);
    }

    private static void LogRequestSuccess(string operation, int statusCode)
    {
        Log.Information("[AccService] {Operation} SUCCESS — http={StatusCode}.", operation, statusCode);
    }

    private void LogRequestException(string operation, Exception ex, CancellationToken ct)
    {
        var errorCategory = ClassifyException(ex, ct);
        var innerMsg = ex.InnerException?.Message;

        Log.Error(ex,
            "[AccService] {Operation} FAILED — category={Category}, exceptionType={ExType}, message={Message}, " +
            "innerException={InnerMessage}, baseAddress={BaseAddress}.",
            operation, errorCategory, ex.GetType().Name, ex.Message, innerMsg ?? "(none)",
            _http.BaseAddress?.ToString() ?? "(null)");
    }

    private static string ClassifyException(Exception ex, CancellationToken ct)
    {
        return ex switch
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
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage resp, string operation, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;

        string detail;
        try
        {
            // Server emits ErrorDto for known-bad inputs; fall back to raw body otherwise.
            var err = await resp.Content.ReadFromJsonAsync<ErrorDto>(cancellationToken: ct);
            detail = err?.Error ?? await resp.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            detail = await resp.Content.ReadAsStringAsync(ct);
        }

        // Truncate response body for logging (max 500 chars)
        var truncatedDetail = detail.Length > 500 ? detail[..500] + "..." : detail;

        // Friendly hint so the UI can tell the user *where* the failure originated
        var errorCategory = (int)resp.StatusCode switch
        {
            401 or 403 => "ApiKeyRejected",
            400 => "BadRequest",
            404 => "NotFound",
            504 => "AccUpstreamTimeout",  // Must come before the 5xx range
            >= 500 and < 600 => "ServerError",
            _ => $"Http{(int)resp.StatusCode}"
        };

        var hint = (int)resp.StatusCode switch
        {
            504 => "ACC (Autodesk) timed out responding to the service. Retry in a moment.",
            409 when detail.Contains("FOLDER_ALREADY_EXIST", StringComparison.OrdinalIgnoreCase)
                => "ACC reports the folder already exists — this is usually safe to ignore.",
            401 or 403 => "SiOffice.AccService rejected the API key (X-AccService-Key). Verify the secret in Credential Manager.",
            _ => "Unexpected response from SiOffice.AccService."
        };

        Log.Error(
            "[AccService] {Operation} FAILED — category={Category}, http={StatusCode}, " +
            "method={Method}, url={Url}, responseBody={ResponseBody}.",
            operation, errorCategory, (int)resp.StatusCode,
            resp.RequestMessage?.Method.ToString() ?? "?",
            resp.RequestMessage?.RequestUri?.ToString() ?? "?",
            truncatedDetail);

        throw new HttpRequestException(
            $"{hint} (HTTP {(int)resp.StatusCode} {resp.ReasonPhrase} from " +
            $"{resp.RequestMessage?.Method} {resp.RequestMessage?.RequestUri}). Detail: {detail}",
            inner: null,
            statusCode: resp.StatusCode);
    }
}
