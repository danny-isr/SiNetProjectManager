namespace SiNet.Application.Workflow;

/// <summary>
/// Lightweight, read-only summary of a task (<c>ProjectAssignment</c>) created or touched
/// by a workflow write operation (start / stage advance).
/// <para>
/// Lives in the Application layer so workflow write results never expose the EF
/// <c>ProjectAssignment</c> entity. Carries only the scalar identity/display fields a caller
/// or UI would reasonably need; deeper details should be fetched through the task query path.
/// </para>
/// </summary>
/// <param name="Id">Task identifier.</param>
/// <param name="ProjectId">Owning project id, when bound.</param>
/// <param name="Title">Task title.</param>
/// <param name="AssignedToId">Assignee user id, when assigned.</param>
/// <param name="StatusId">FK to the task status definition, when set.</param>
/// <param name="LegacyStatus">Legacy string status (back-compat snapshot).</param>
/// <param name="TaskTypeId">FK to the task type, when set.</param>
/// <param name="WorkPriority">Position in the assignee's work queue (1 = highest); null for closed tasks.</param>
/// <param name="DueDate">Optional due date.</param>
public sealed record ProjectAssignmentSummaryDto(
    int Id,
    int? ProjectId,
    string? Title,
    int? AssignedToId,
    int? StatusId,
    string? LegacyStatus,
    int? TaskTypeId,
    int? WorkPriority,
    System.DateTime? DueDate);
