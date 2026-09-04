using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Authentication;
using Serilog;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Configuration;
using SiOffice.AccService.Contracts;

namespace SiNetSQL.Services.AccBootstrap;

/// <summary>
/// HTTP client for <see cref="IAccProjectProvisioningService"/> that forwards privileged
/// ACC project operations to <c>SiOffice.AccService</c> (same endpoints as the V2 remote client).
/// </summary>
/// <remarks>
/// Auth uses the vault API key on each request (same pattern as
/// <c>SiNet.Infrastructure.Autodesk</c> remotes). Long-running ensure-mapping relies on the
/// per-call <see cref="CancellationToken"/>; the typed <see cref="HttpClient"/> is registered
/// with <see cref="Timeout.InfiniteTimeSpan"/>.
/// </remarks>
internal sealed class RemoteAccProjectProvisioningService(
    HttpClient httpClient,
    ISecretVaultStore secretVaultStore,
    IAccServiceModeProvider serviceModeProvider) : IAccProjectProvisioningService
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly ISecretVaultStore _secretVaultStore =
        secretVaultStore ?? throw new ArgumentNullException(nameof(secretVaultStore));
    private readonly IAccServiceModeProvider _serviceModeProvider =
        serviceModeProvider ?? throw new ArgumentNullException(nameof(serviceModeProvider));

    public Task<ProjectAccTargets> EnsureProjectMappingAsync(int projectId, CancellationToken cancellationToken) =>
        PostJsonAsync<EnsureProjectMappingRequest, ProjectAccTargets>(
            "EnsureProjectMappingAsync",
            "v1/acc/projects/ensure-mapping",
            new EnsureProjectMappingRequest(projectId),
            cancellationToken);

    public async Task ReconcileProjectMembersAsync(string accProjectId, CancellationToken cancellationToken)
    {
        var relativeUrl = $"v1/acc/projects/{Uri.EscapeDataString(accProjectId)}/members/reconcile";
        using var response = await SendAsync(
            "ReconcileProjectMembersAsync",
            HttpMethod.Post,
            relativeUrl,
            content: null,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "ReconcileProjectMembersAsync", cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> ReconcileAllProjectsAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            "ReconcileAllProjectsAsync",
            HttpMethod.Post,
            "v1/acc/projects/reconcile-all",
            content: null,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "ReconcileAllProjectsAsync", cancellationToken).ConfigureAwait(false);
        var dto = await response.Content
            .ReadFromJsonAsync<SummaryDto>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return dto?.Summary ?? string.Empty;
    }

    public async Task<bool> EnsureCustomAttributeDefinitionsAsync(
        string accProjectId,
        string accFolderId,
        int? siProjectId,
        CancellationToken cancellationToken)
    {
        var relativeUrl = $"v1/acc/projects/{Uri.EscapeDataString(accProjectId)}/attribute-defs/ensure";
        using var response = await SendAsync(
            "EnsureCustomAttributeDefinitionsAsync",
            HttpMethod.Post,
            relativeUrl,
            JsonContent.Create(new EnsureAttributeDefsRequest(accFolderId, siProjectId)),
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "EnsureCustomAttributeDefinitionsAsync", cancellationToken)
            .ConfigureAwait(false);
        var dto = await response.Content
            .ReadFromJsonAsync<BoolResultDto>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return dto?.Success == true;
    }

    public async Task<IReadOnlyList<(string Id, string Name)>> ListAvailableTemplatesAsync(
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            "ListAvailableTemplatesAsync",
            HttpMethod.Get,
            "v1/acc/templates",
            content: null,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "ListAvailableTemplatesAsync", cancellationToken).ConfigureAwait(false);
        var list = await response.Content
            .ReadFromJsonAsync<List<AccTemplateDto>>(cancellationToken: cancellationToken)
            .ConfigureAwait(false)
            ?? [];
        return list.Select(t => (t.Id, t.Name)).ToList();
    }

    public async Task<IReadOnlyList<AccProjectMemberInfo>> ListProjectMembersAsync(
        string accProjectId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accProjectId))
            throw new ArgumentException("accProjectId is required.", nameof(accProjectId));

        var relativeUrl = $"v1/acc/projects/{Uri.EscapeDataString(accProjectId.Trim())}/members";
        using var response = await SendAsync(
            "ListProjectMembersAsync",
            HttpMethod.Get,
            relativeUrl,
            content: null,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "ListProjectMembersAsync", cancellationToken).ConfigureAwait(false);
        var list = await response.Content
            .ReadFromJsonAsync<List<AccProjectMemberDto>>(cancellationToken: cancellationToken)
            .ConfigureAwait(false)
            ?? [];
        return list
            .Where(m => !string.IsNullOrWhiteSpace(m.Email))
            .Select(m => new AccProjectMemberInfo(
                Email: m.Email.Trim(),
                Name: m.Name,
                AccessLevel: m.AccessLevel,
                Status: m.Status))
            .ToList();
    }

    public Task<string> ProbeFolderPermissionsAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "ProbeFolderPermissionsAsync is diagnostic-only and is not exposed by SiOffice.AccService. " +
            "Run it locally with AccProjectProvisioningService.");

    public Task<string> ProbeFolderPermissionsFromTemplateAsync(
        string templateName,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "ProbeFolderPermissionsFromTemplateAsync is diagnostic-only and is not exposed by SiOffice.AccService.");

    private async Task<TResponse> PostJsonAsync<TRequest, TResponse>(
        string operation,
        string relativeUrl,
        TRequest body,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            operation,
            HttpMethod.Post,
            relativeUrl,
            JsonContent.Create(body),
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, operation, cancellationToken).ConfigureAwait(false);
        return await response.Content
                   .ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken)
                   .ConfigureAwait(false)
               ?? throw new InvalidOperationException($"AccService returned an empty body for {operation}.");
    }

    private async Task<HttpResponseMessage> SendAsync(
        string operation,
        HttpMethod method,
        string relativeUrl,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        var baseUrl = _serviceModeProvider.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "ACC service base URL is not configured for remote project provisioning.");
        }

        var apiKey = _secretVaultStore.GetSecret(SecretCatalog.AccServiceApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "ACC service API key is not configured in the native secret vault.");
        }

        var requestUri = $"{baseUrl.TrimEnd('/')}/{relativeUrl.TrimStart('/')}";
        Log.Information(
            "[AccService] {Operation} START — method={Method}, url={RelativeUrl}, baseUrl={BaseUrl}.",
            operation,
            method.Method,
            relativeUrl,
            baseUrl);

        try
        {
            using var request = new HttpRequestMessage(method, requestUri) { Content = content };
            request.Headers.Add(AccServiceContracts.ApiKeyHeader, apiKey);
            return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not HttpRequestException)
        {
            Log.Error(
                ex,
                "[AccService] {Operation} FAILED — category={Category}, exceptionType={ExType}, message={Message}.",
                operation,
                ClassifyException(ex, cancellationToken),
                ex.GetType().Name,
                ex.Message);
            throw;
        }
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string detail;
        try
        {
            var err = await response.Content
                .ReadFromJsonAsync<ErrorDto>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            detail = err?.Error
                     ?? await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        var truncatedDetail = detail.Length > 500 ? detail[..500] + "..." : detail;
        var status = (int)response.StatusCode;
        var errorCategory = status switch
        {
            401 or 403 => "ApiKeyRejected",
            400 => "BadRequest",
            404 => "NotFound",
            504 => "AccUpstreamTimeout",
            >= 500 and < 600 => "ServerError",
            _ => $"Http{status}",
        };

        var hint = status switch
        {
            504 => "ACC (Autodesk) timed out responding to the service. Retry in a moment.",
            401 or 403 =>
                "SiOffice.AccService rejected the API key (X-AccService-Key). Verify the secret in Credential Manager.",
            _ => "Unexpected response from SiOffice.AccService.",
        };

        Log.Error(
            "[AccService] {Operation} FAILED — category={Category}, http={StatusCode}, responseBody={ResponseBody}.",
            operation,
            errorCategory,
            status,
            truncatedDetail);

        throw new HttpRequestException(
            $"{hint} (HTTP {status} {response.ReasonPhrase}). Detail: {detail}",
            inner: null,
            statusCode: response.StatusCode);
    }

    private static string ClassifyException(Exception ex, CancellationToken ct) =>
        ex switch
        {
            TaskCanceledException when ct.IsCancellationRequested => "Cancelled",
            TaskCanceledException or OperationCanceledException => "Timeout",
            HttpRequestException
            {
                InnerException: SocketException { SocketErrorCode: SocketError.ConnectionRefused },
            } => "ConnectionRefused",
            HttpRequestException
            {
                InnerException: SocketException { SocketErrorCode: SocketError.HostNotFound },
            } => "DnsResolutionFailed",
            HttpRequestException { InnerException: AuthenticationException } => "SslCertificateError",
            HttpRequestException hre => $"HttpError_{(int?)hre.StatusCode}",
            _ => "UnknownError",
        };
}
