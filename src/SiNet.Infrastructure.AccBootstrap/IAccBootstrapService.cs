namespace SiNetSQL.Services.AccBootstrap;

/// <summary>
/// Service for bootstrapping ACC (Autodesk Construction Cloud) resources.
/// Ensures that required hubs, projects, and folders exist in ACC and are tracked in the database.
/// </summary>
public interface IAccBootstrapService
{
    /// <summary>
    /// Ensures the Office Inbox ACC project and folder structure exist.
    /// 
    /// This method:
    /// 1. Ensures the AccHub row exists in the database
    /// 2. Checks if AccSystemResource "OfficeInbox" has valid IDs
    /// 3. If not, bootstraps ACC (finds/creates project and _Inbox folder)
    /// 4. Updates the database with resolved IDs
    /// </summary>
    /// <param name="currentLogin">Windows login of the user initiating the bootstrap (for audit logging).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved Office Inbox targets containing all ACC identifiers.</returns>
    /// <exception cref="InvalidOperationException">When required configuration is missing.</exception>
    Task<OfficeInboxTargets> EnsureOfficeInboxAsync(string currentLogin, CancellationToken cancellationToken);
}
