namespace SiNet.Application.Identity;

/// <summary>Reloads the current SIUser row into the authenticated session (admin approval refresh).</summary>
public interface ICurrentUserSessionRefreshService
{
    /// <summary>
    /// Re-reads <c>SIUser</c> for the current session user id (or LoginName) and updates the session profile.
    /// Does not auto-reactivate inactive users.
    /// </summary>
    Task<CurrentUserProfileDto?> RefreshCurrentUserAsync(CancellationToken cancellationToken = default);
}
