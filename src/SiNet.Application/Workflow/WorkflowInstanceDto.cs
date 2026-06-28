using System;
using System.Collections.Generic;
using SiNet.Domain.Workflow;

namespace SiNet.Application.Workflow;

/// <summary>
/// A workflow instance with the related data needed by read consumers
/// (dashboard rows, instance detail, transition history).
/// </summary>
/// <param name="Id">Instance identifier.</param>
/// <param name="WorkflowDefinitionId">Owning definition id.</param>
/// <param name="ProjectId">Bound project id, if project-bound.</param>
/// <param name="Status">Lifecycle status.</param>
/// <param name="CurrentStageId">Current stage id, if started.</param>
/// <param name="CreatedAtUtc">UTC creation timestamp.</param>
/// <param name="CompletedAtUtc">UTC completion timestamp, if completed.</param>
/// <param name="Notes">Optional free-text notes.</param>
/// <param name="WorkflowDefinition">Owning definition (with stages), when loaded.</param>
/// <param name="CurrentStage">Current stage reference, when loaded.</param>
/// <param name="Project">Bound project reference, when loaded.</param>
/// <param name="CreatedByUser">User who created the instance, when loaded.</param>
/// <param name="StageTransitions">Ordered transition history, when loaded.</param>
public sealed record WorkflowInstanceDto(
    int Id,
    int WorkflowDefinitionId,
    int? ProjectId,
    WorkflowStatus Status,
    int? CurrentStageId,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    string? Notes,
    WorkflowDefinitionDto? WorkflowDefinition,
    WorkflowStageDefinitionDto? CurrentStage,
    WorkflowProjectRefDto? Project,
    WorkflowUserRefDto? CreatedByUser,
    IReadOnlyList<WorkflowStageTransitionDto> StageTransitions);
