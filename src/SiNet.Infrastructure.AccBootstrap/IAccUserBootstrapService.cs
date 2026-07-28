namespace SiNetSQL.Services.AccBootstrap;

/// <summary>
/// Service for bootstrapping SI users into ACC Emails Project based on their AccUserType.
/// Runs on startup as background fire-and-forget task.
/// </summary>
public interface IAccUserBootstrapService
{
    /// <summary>
    /// Provisions SI users into the ACC Emails Project.
    /// - AccUserType.Engineer → "member" access level
    /// - AccUserType.Admin → "administrator" access level
    /// - AccUserType.NoAccUser → skipped
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for graceful shutdown.</param>
    /// <returns>Task that completes when provisioning is done.</returns>
    Task ProvisionUsersAsync(CancellationToken cancellationToken);
}
