using System.Net.Http;
using System.Net.Http.Json;
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
        using var resp = await _http.PostAsJsonAsync(
            "v1/acc/projects/ensure-mapping",
            new EnsureProjectMappingRequest(projectId),
            cancellationToken);
        await EnsureSuccessAsync(resp, cancellationToken);
        return await resp.Content.ReadFromJsonAsync<ProjectAccTargets>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("AccService returned an empty body for ensure-mapping.");
    }

    /// <inheritdoc/>
    public async Task ReconcileProjectMembersAsync(string accProjectId, CancellationToken cancellationToken)
    {
        using var resp = await _http.PostAsync(
            $"v1/acc/projects/{Uri.EscapeDataString(accProjectId)}/members/reconcile",
            content: null,
            cancellationToken);
        await EnsureSuccessAsync(resp, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<string> ReconcileAllProjectsAsync(CancellationToken cancellationToken)
    {
        using var resp = await _http.PostAsync(
            "v1/acc/projects/reconcile-all",
            content: null,
            cancellationToken);
        await EnsureSuccessAsync(resp, cancellationToken);
        var dto = await resp.Content.ReadFromJsonAsync<SummaryDto>(cancellationToken: cancellationToken);
        return dto?.Summary ?? string.Empty;
    }

    /// <inheritdoc/>
    public async Task<bool> EnsureCustomAttributeDefinitionsAsync(
        string accProjectId, string accFolderId, int? siProjectId, CancellationToken cancellationToken)
    {
        using var resp = await _http.PostAsJsonAsync(
            $"v1/acc/projects/{Uri.EscapeDataString(accProjectId)}/attribute-defs/ensure",
            new EnsureAttributeDefsRequest(accFolderId, siProjectId),
            cancellationToken);
        await EnsureSuccessAsync(resp, cancellationToken);
        var dto = await resp.Content.ReadFromJsonAsync<BoolResultDto>(cancellationToken: cancellationToken);
        return dto?.Success == true;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<(string Id, string Name)>> ListAvailableTemplatesAsync(CancellationToken cancellationToken)
    {
        using var resp = await _http.GetAsync("v1/acc/templates", cancellationToken);
        await EnsureSuccessAsync(resp, cancellationToken);
        var list = await resp.Content.ReadFromJsonAsync<List<AccTemplateDto>>(cancellationToken: cancellationToken)
            ?? new List<AccTemplateDto>();
        return list.Select(t => (t.Id, t.Name)).ToList();
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

    private static async Task EnsureSuccessAsync(HttpResponseMessage resp, CancellationToken ct)
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

        throw new HttpRequestException(
            $"SiOffice.AccService returned {(int)resp.StatusCode} {resp.ReasonPhrase} for " +
            $"{resp.RequestMessage?.Method} {resp.RequestMessage?.RequestUri}. Detail: {detail}",
            inner: null,
            statusCode: resp.StatusCode);
    }
}
