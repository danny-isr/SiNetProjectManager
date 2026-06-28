using System;

namespace SiNet.Application.Workflow;

/// <summary>
/// A single audited stage transition within a workflow instance's history.
/// </summary>
/// <param name="Id">Transition identifier.</param>
/// <param name="FromStageId">Source stage id, if any.</param>
/// <param name="ToStageId">Target stage id.</param>
/// <param name="ToStage">Target stage reference (for display).</param>
/// <param name="TransitionedByUser">User who performed the transition.</param>
/// <param name="TransitionedAtUtc">UTC timestamp of the transition.</param>
/// <param name="Notes">Optional free-text notes.</param>
public sealed record WorkflowStageTransitionDto(
    int Id,
    int? FromStageId,
    int ToStageId,
    WorkflowStageDefinitionDto? ToStage,
    WorkflowUserRefDto? TransitionedByUser,
    DateTime TransitionedAtUtc,
    string? Notes);
