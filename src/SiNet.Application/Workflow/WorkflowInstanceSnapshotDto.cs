using System.Collections.Generic;

namespace SiNet.Application.Workflow;

/// <summary>
/// Instance-centric snapshot for the floating workflow monitor:
/// a workflow instance together with its ordered stage list and the set of
/// already-visited stage ids (used to render pipeline progress).
/// </summary>
/// <param name="Instance">The workflow instance.</param>
/// <param name="AllStages">Ordered stages of the instance's definition.</param>
/// <param name="VisitedStageIds">Stage ids that have already been transitioned through.</param>
public sealed record WorkflowInstanceSnapshotDto(
    WorkflowInstanceDto Instance,
    IReadOnlyList<WorkflowStageDefinitionDto> AllStages,
    IReadOnlySet<int> VisitedStageIds);
