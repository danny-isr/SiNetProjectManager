namespace SiNet.Application.Identity;

/// <summary>
/// Admin write port for action-level permission rows (see <c>docs/IDENTITY_AND_PERMISSIONS.md</c> §7.4).
/// Separate from read-only <see cref="IActionPermissionQueryService"/>.
/// </summary>
public interface IActionPermissionAdminService
{
    /// <summary>
    /// Returns assignable users (active, non-empty email, role &gt;= Employee) for the admin checklist.
    /// </summary>
    Task<IReadOnlyList<ActionPermissionAssigneeDto>> GetAssignableUsersAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns active permission rows grouped by action code (user ids only).
    /// </summary>
    Task<IReadOnlyDictionary<string, IReadOnlySet<int>>> GetActivePermissionsByActionAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists permission changes for one action (soft-delete removals, reactivate or insert additions).
    /// Requires <see cref="AppFeatureCodes.ActionPermissionsManage"/>.
    /// </summary>
    Task SaveActionPermissionsAsync(
        string actionCode,
        IReadOnlySet<int> authorizedUserIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists permission changes for all known actions in one transaction.
    /// Requires <see cref="AppFeatureCodes.ActionPermissionsManage"/>.
    /// </summary>
    Task SaveAllActionPermissionsAsync(
        IReadOnlyDictionary<string, IReadOnlySet<int>> permissionsByActionCode,
        CancellationToken cancellationToken = default);
}

/// <summary>User row in the action-permissions admin checklist.</summary>
public sealed record ActionPermissionAssigneeDto(
    int UserId,
    string DisplayName,
    string? Email);
