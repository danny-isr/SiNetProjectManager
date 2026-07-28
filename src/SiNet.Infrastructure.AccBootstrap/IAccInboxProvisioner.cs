namespace SiNetSQL.Services.AccBootstrap;

/// <summary>
/// Abstraction over "ensure the Office Inbox project + folder + members exist".
/// Two implementations exist:
///   • <see cref="LocalAccInboxProvisioner"/> — runs <see cref="AccBootstrapService"/> in-process
///     (legacy / single-machine deployments where the WPF user IS the ACC Account Admin).
///   • RemoteAccInboxProvisioner (in the WPF project) — POSTs to
///     <c>/v1/acc/inbox/ensure</c> on SiOffice.AccService so a centrally-hosted
///     service account does the privileged work and regular users don't need
///     Account Admin credentials.
///
/// Both return the same (AccProjectId, AccInboxFolderId) pair so callers
/// (notably <c>EmailIngestionServiceFactory</c>) can stay agnostic.
/// </summary>
public interface IAccInboxProvisioner
{
    /// <summary>
    /// Ensures the Office Inbox ACC project + "_Inbox" folder exist and that the
    /// configured member emails have access. Idempotent: subsequent calls are
    /// cheap when everything is already in place.
    /// </summary>
    /// <returns>The resolved ACC project id and inbox folder id.</returns>
    Task<(string AccProjectId, string AccInboxFolderId)> EnsureAsync(CancellationToken cancellationToken);
}
