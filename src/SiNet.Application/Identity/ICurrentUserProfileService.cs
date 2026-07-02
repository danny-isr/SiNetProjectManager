namespace SiNet.Application.Identity;

/// <summary>
/// Read-only access to the authenticated user's profile for display/context.
/// <para>
/// Authorization decisions stay in dedicated services — this port does not grant or deny access.
/// Returns <see langword="null"/> when no authenticated user is available (same semantics as
/// <see cref="ICurrentUserContext.UserId"/> being null).
/// </para>
/// </summary>
public interface ICurrentUserProfileService
{
    /// <summary>
    /// Returns the current user's profile after host authentication, or <see langword="null"/> when
    /// unauthenticated.
    /// </summary>
    Task<CurrentUserProfileDto?> GetCurrentUserAsync(
        CancellationToken cancellationToken = default);
}
