namespace SiNet.Application.Identity;

/// <summary>
/// Read-only user-group catalog for native admin (members, default assignee, dependent stages).
/// </summary>
public interface IUserGroupQueryService
{
    /// <summary>Returns active groups ordered by display name.</summary>
    Task<IReadOnlyList<UserGroupSummaryDto>> GetActiveGroupsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Returns group detail including members and dependent non-final stages, or null when missing/inactive.</summary>
    Task<UserGroupDetailDto?> GetGroupDetailAsync(
        int groupId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns non-final stages on active workflows that assign this group.</summary>
    Task<IReadOnlyList<WorkflowStageGroupDependencyDto>> GetStagesUsingGroupAsync(
        int groupId,
        CancellationToken cancellationToken = default);
}
