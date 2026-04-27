using System.Net.Http;
using System.Net.Http.Json;
using SiNetSQL.Services.AccBootstrap;
using SiNetSQL.Services.AccBootstrap.Contracts;

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
        // Empty body = let the service resolve everything from SystemSettings + vault.
        using var resp = await _http.PostAsJsonAsync(
            "v1/acc/inbox/ensure",
            new EnsureInboxRequest(),
            cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            ErrorDto? err = null;
            try { err = await resp.Content.ReadFromJsonAsync<ErrorDto>(cancellationToken: cancellationToken); }
            catch { /* fall through to status-code-only message */ }

            throw new InvalidOperationException(
                $"AccService returned {(int)resp.StatusCode} {resp.ReasonPhrase}" +
                (err is null ? "" : $": {err.Error}{(err.Detail is null ? "" : $" — {err.Detail}")}"));
        }

        var body = await resp.Content.ReadFromJsonAsync<EnsureInboxResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("AccService returned empty inbox-ensure response.");

        return (body.AccProjectId, body.AccInboxFolderId);
    }
}
