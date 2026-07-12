using System.Collections.Generic;

namespace SiNet.Application.Workflow;

/// <summary>Closed catalogs for the workflow viewer (enums + system codes).</summary>
public sealed record WorkflowClosedWorldCatalogDto(
    IReadOnlyList<string> NodeTypes,
    IReadOnlyList<string> ActionTypes,
    IReadOnlyList<string> TriggerTypes,
    IReadOnlyList<string> ConditionTypes,
    IReadOnlyList<string> EvaluationModes,
    IReadOnlyList<string> ProjectStatusCodes,
    IReadOnlyList<string> TaskResultCodes,
    IReadOnlyList<string> SystemWorkflowCodes,
    IReadOnlyList<string> SystemStageCodes);
