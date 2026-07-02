namespace SiNet.Application.Identity;

/// <summary>
/// Read-only role and feature authorization queries for the New System (see
/// <c>docs/IDENTITY_AND_PERMISSIONS.md</c> §7.3). Does not mutate users or permissions; does not
/// replace action-level allow-lists (<see cref="IActionPermissionQueryService"/>).
/// </summary>
public interface IAuthorizationQueryService
{
    /// <summary>
    /// Returns whether the current authenticated user has at least <paramref name="requiredRole"/>
    /// (hierarchical: <c>Role &gt;= requiredRole</c>). Returns <see langword="false"/> when unauthenticated.
    /// </summary>
    Task<bool> IsCurrentUserInRoleAsync(
        AppRole requiredRole,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns whether the current user may access <paramref name="featureCode"/> per
    /// <see cref="AppFeatureAuthorization"/>. Returns <see langword="false"/> when unauthenticated.
    /// </summary>
    /// <exception cref="ArgumentException">When <paramref name="featureCode"/> is not registered.</exception>
    Task<bool> CanCurrentUserAccessFeatureAsync(
        string featureCode,
        CancellationToken cancellationToken = default);
}
