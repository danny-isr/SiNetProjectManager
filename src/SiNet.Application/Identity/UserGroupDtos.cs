namespace SiNet.Application.Identity;

/// <summary>Active user-group row for admin lists.</summary>
public sealed record UserGroupSummaryDto(
    int Id,
    string Code,
    string Name,
    string? Description,
    int? DefaultAssigneeId,
    string? DefaultAssigneeDisplayName,
    int ActiveMemberCount);

/// <summary>Active member of a user group.</summary>
public sealed record UserGroupMemberDto(
    int UserId,
    string DisplayName,
    bool IsActive);

/// <summary>Read-only workflow stage that depends on a group for assignee resolution.</summary>
public sealed record WorkflowStageGroupDependencyDto(
    string WorkflowCode,
    string StageCode,
    string StageName);

/// <summary>Full group detail for the native admin surface.</summary>
public sealed record UserGroupDetailDto(
    int Id,
    string Code,
    string Name,
    string? Description,
    int? DefaultAssigneeId,
    IReadOnlyList<UserGroupMemberDto> Members,
    IReadOnlyList<WorkflowStageGroupDependencyDto> DependentStages);
