using System.Collections.Generic;

namespace SiNet.Application.Workflow;

/// <summary>
/// Lightweight projection for the cross-project workflow dashboard:
/// a project paired with its most relevant workflow instance (if any),
/// the instance's ordered stage list, and the set of already-visited stage ids
/// (used to render the pipeline progress).
/// </summary>
/// <param name="Project">The project this snapshot belongs to.</param>
/// <param name="Instance">The most relevant instance for the project, or null when none exists.</param>
/// <param name="AllStages">Ordered stages of the instance's definition.</param>
/// <param name="VisitedStageIds">Stage ids that have already been transitioned through.</param>
public sealed record ProjectWorkflowSnapshotDto(
    WorkflowProjectRefDto Project,
    WorkflowInstanceDto? Instance,
    IReadOnlyList<WorkflowStageDefinitionDto> AllStages,
    IReadOnlySet<int> VisitedStageIds);
