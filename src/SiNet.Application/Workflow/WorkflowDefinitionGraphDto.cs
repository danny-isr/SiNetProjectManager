using System.Collections.Generic;

namespace SiNet.Application.Workflow;

/// <summary>Full workflow definition graph for the closed-world viewer.</summary>
public sealed record WorkflowDefinitionGraphDto(
    int Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    bool IsSystem,
    IReadOnlyList<WorkflowStageGraphDto> Stages,
    IReadOnlyList<WorkflowTransitionGraphDto> Transitions);

/// <summary>Stage row within a definition graph.</summary>
public sealed record WorkflowStageGraphDto(
    int Id,
    string Code,
    string Name,
    string? Description,
    int SortOrder,
    bool IsInitial,
    bool IsFinal,
    string NodeType,
    bool NodeTypeKnown,
    bool IsSystem,
    string? AssignedGroupName,
    string? AssignedGroupCode,
    string? SubWorkflowName,
    string? SubWorkflowCode,
    double CanvasX,
    double CanvasY,
    IReadOnlyList<WorkflowStageTaskGraphDto> StageTasks);

/// <summary>Transition rule within a definition graph.</summary>
public sealed record WorkflowTransitionGraphDto(
    int Id,
    string? Name,
    int FromStageId,
    int ToStageId,
    string FromStageName,
    string ToStageName,
    string TriggerType,
    bool TriggerTypeKnown,
    string ConditionType,
    bool ConditionTypeKnown,
    string EvaluationMode,
    bool EvaluationModeKnown,
    int Priority,
    string? ConditionJson,
    string? ConditionTaskResultCode,
    bool ConditionTaskResultOk,
    IReadOnlyList<WorkflowTransitionActionGraphDto> Actions);

/// <summary>Action on a transition rule.</summary>
public sealed record WorkflowTransitionActionGraphDto(
    string ActionType,
    bool ActionTypeKnown,
    string? ActionCode,
    string? ConfigJson,
    string? ConfigProjectStatusCode,
    bool ConfigProjectStatusOk,
    string? ConfigTaskResultCode,
    bool ConfigTaskResultOk,
    int SortOrder);

/// <summary>Stage-task template within a stage.</summary>
public sealed record WorkflowStageTaskGraphDto(
    int Id,
    int StageId,
    int SortOrder,
    bool IsRequired,
    string? Notes,
    string TaskTypeName,
    string TaskTypeCode,
    string? AssigneeDisplay,
    bool HasInteraction,
    string? OpenMode,
    string? ComponentKey,
    IReadOnlyList<string> AllowedTaskResultCodes);
