namespace SiNet.Application.Workflow;

/// <summary>Why a workflow stage cannot resolve an assignee from its assigned group.</summary>
public enum WorkflowAssigneeIssueKind
{
    /// <summary>Non-final stage has no <c>AssignedGroupId</c>.</summary>
    MissingAssignedGroup = 0,

    /// <summary><c>AssignedGroupId</c> points at a missing group row.</summary>
    GroupMissing = 1,

    /// <summary>Assigned group has zero active members.</summary>
    NoActiveMembers = 2,

    /// <summary>Multiple active members and no valid <c>DefaultAssigneeId</c>.</summary>
    MultipleMembersWithoutDefault = 3,
}

/// <summary>One non-resolvable assignee configuration on an active workflow stage.</summary>
public sealed record WorkflowAssigneeReadinessIssueDto(
    string WorkflowCode,
    string StageCode,
    string StageName,
    string? GroupCode,
    WorkflowAssigneeIssueKind IssueKind,
    string SummaryHe);

/// <summary>
/// Reports workflow stages whose assigned group cannot resolve an assignee
/// (same rules as runtime <c>TryResolveAssigneeFromGroup</c>).
/// </summary>
public interface IWorkflowAssigneeReadinessQueryService
{
    /// <summary>Returns all assignee readiness issues across active, non-final stages.</summary>
    Task<IReadOnlyList<WorkflowAssigneeReadinessIssueDto>> GetIssuesAsync(
        CancellationToken cancellationToken = default);
}
