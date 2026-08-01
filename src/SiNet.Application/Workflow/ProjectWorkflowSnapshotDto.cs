using System.Collections.Generic;

namespace SiNet.Application.Workflow;

/// <summary>
/// Lightweight projection for the cross-project workflow dashboard:
/// a project paired with workflow instance track(s), stage list, and visited stage ids.
/// B2: <see cref="TrackInstances"/> lists every Active/Paused track; <see cref="Instance"/>
/// remains the primary row for backward compatibility (first by status priority then newest).
/// </summary>
/// <param name="Project">The project this snapshot belongs to.</param>
/// <param name="Instance">Primary instance for compat, or null when none exists.</param>
/// <param name="AllStages">Ordered stages of the primary instance's definition.</param>
/// <param name="VisitedStageIds">Stage ids already transitioned on the primary instance.</param>
/// <param name="TrackInstances">All Active/Paused project-bound tracks for the project (B2).</param>
public sealed record ProjectWorkflowSnapshotDto(
    WorkflowProjectRefDto Project,
    WorkflowInstanceDto? Instance,
    IReadOnlyList<WorkflowStageDefinitionDto> AllStages,
    IReadOnlySet<int> VisitedStageIds,
    IReadOnlyList<WorkflowInstanceDto> TrackInstances = null!);
