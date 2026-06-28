namespace SiNet.Application.Workflow;

/// <summary>
/// Application-layer results for <see cref="IWorkflowCommandService"/>. These mirror the
/// infrastructure orchestrator's result records but live in the Application layer so the write
/// port never exposes infrastructure types. The orchestrator's records are already DTO-backed
/// (instance + task summaries), so the adapter maps field-for-field with no entity access.
/// </summary>

/// <summary>Result of starting a workflow: the started instance plus the tasks created.</summary>
/// <param name="Instance">The started workflow instance.</param>
/// <param name="CreatedTasks">Summaries of the tasks provisioned for the initial stage.</param>
public sealed record WorkflowStartResultDto(
    WorkflowInstanceDto Instance,
    IReadOnlyList<ProjectAssignmentSummaryDto> CreatedTasks);

/// <summary>Result of advancing a workflow: the updated instance plus any tasks created.</summary>
/// <param name="Instance">The workflow instance after the advance.</param>
/// <param name="CreatedTasks">Summaries of the tasks provisioned for the new stage.</param>
public sealed record WorkflowAdvanceResultDto(
    WorkflowInstanceDto Instance,
    IReadOnlyList<ProjectAssignmentSummaryDto> CreatedTasks);

/// <summary>
/// Application-layer mirror of the orchestrator's stage-completion action, returned by the
/// auto-advance commands.
/// </summary>
public enum StageCompletionActionDto
{
    /// <summary>The workflow auto-advanced to the next stage.</summary>
    AutoAdvanced,

    /// <summary>A transition is available but must be advanced manually.</summary>
    ManualAdvanceRequired,

    /// <summary>An auto-advance was attempted but failed.</summary>
    AutoAdvanceFailed,

    /// <summary>A transition is available but requires user confirmation before firing.</summary>
    ConfirmationRequired,
}

/// <summary>Outcome of an auto-advance evaluation.</summary>
/// <param name="InstanceId">The workflow instance evaluated.</param>
/// <param name="CompletedStageId">The stage that was completed/evaluated.</param>
/// <param name="Action">What happened (or what is required).</param>
/// <param name="AdvancedInstance">The instance after advancing, when an advance occurred.</param>
/// <param name="TargetStageId">The target stage of the matched transition, when applicable.</param>
/// <param name="TransitionRuleId">The matched transition rule id, when applicable.</param>
public sealed record StageCompletionResultDto(
    int InstanceId,
    int CompletedStageId,
    StageCompletionActionDto Action,
    WorkflowInstanceDto? AdvancedInstance = null,
    int? TargetStageId = null,
    int? TransitionRuleId = null);
