namespace SiNet.Application.Identity;

/// <summary>
/// Read-only action-level permission queries for the New System (see
/// <c>docs/IDENTITY_AND_PERMISSIONS.md</c> §7.4). Separate from role/feature authorization
/// (<see cref="IAuthorizationQueryService"/>). Does not mutate permission rows.
/// </summary>
public interface IActionPermissionQueryService
{
    /// <summary>
    /// Returns whether <paramref name="userId"/> may execute <paramref name="actionCode"/>.
    /// Deny-by-default: returns <see langword="false"/> when no active permission rows exist,
    /// the user is inactive/unauthorized, or the user is not in the allow-list. Administrators bypass.
    /// </summary>
    /// <remarks>
    /// <paramref name="actionCode"/> must be an existing legacy action code (see
    /// <see cref="ActionPermissionCodes"/>). Unknown codes are not approved silently.
    /// </remarks>
    Task<bool> CanUserExecuteActionAsync(
        string actionCode,
        int userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns whether the current authenticated user may execute <paramref name="actionCode"/>.
    /// Returns <see langword="false"/> when <see cref="ICurrentUserContext.UserId"/> is
    /// <see langword="null"/> — never invents a user id.
    /// </summary>
    Task<bool> CanCurrentUserExecuteActionAsync(
        string actionCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns users authorized for <paramref name="actionCode"/> per persisted permission rows.
    /// Only active users with <c>Role &gt;= Employee</c> are included. Does not add Administrator
    /// bypass users who lack an explicit permission row (legacy <c>ActionPermissionService</c> semantics).
    /// </summary>
    Task<IReadOnlyList<UserRefDto>> GetAuthorizedUsersForActionAsync(
        string actionCode,
        CancellationToken cancellationToken = default);
}
