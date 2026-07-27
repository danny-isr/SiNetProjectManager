namespace SiNet.Application.Identity;

/// <summary>
/// Mutating user-group operations for native admin: metadata, soft-delete, membership, default assignee.
/// </summary>
public interface IUserGroupCommandService
{
    /// <summary>Creates an active group. Throws when <paramref name="code"/> already exists.</summary>
    Task<int> CreateGroupAsync(
        string code,
        string name,
        string? description = null,
        CancellationToken cancellationToken = default);

    /// <summary>Updates code/name/description for an active group.</summary>
    Task UpdateGroupMetadataAsync(
        int groupId,
        string code,
        string name,
        string? description,
        CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes a group (<c>IsActive=false</c>).</summary>
    Task SoftDeleteGroupAsync(
        int groupId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds an active system user as a group member.</summary>
    Task AddMemberAsync(
        int groupId,
        int userId,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a membership. Clears default assignee when the removed user was the default.</summary>
    Task RemoveMemberAsync(
        int groupId,
        int userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets or clears the group's default assignee. When set, the user must be an active member of the group.
    /// </summary>
    Task SetDefaultAssigneeAsync(
        int groupId,
        int? userId,
        CancellationToken cancellationToken = default);
}
