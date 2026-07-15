namespace SiNet.Application.Workflow;

/// <summary>
/// Inputs for <see cref="IWorkflowCommandService"/> write operations. Each command collapses
/// the orchestrator's positional parameters into a single Application-layer record so callers
/// never depend on the concrete infrastructure orchestrator signature.
/// </summary>

/// <summary>
/// Application-layer mirror of the infrastructure trigger enum, so commands stay free of
/// EF model types. Values are kept numerically aligned with the infrastructure enum and the
/// adapter maps between them.
/// </summary>
public enum WorkflowTriggerTypeDto
{
    /// <summary>Manually started by a user.</summary>
    Manual = 0,

    /// <summary>Started in response to an incoming email.</summary>
    Email = 1,

    /// <summary>Started automatically by the system.</summary>
    System = 2,
}

/// <summary>Starts a new workflow instance and provisions its initial-stage tasks.</summary>
/// <param name="DefinitionId">Workflow definition to start.</param>
/// <param name="ProjectId">Owning project id (placeholder when <paramref name="IsProjectBound"/> is false).</param>
/// <param name="TriggerType">What triggered the start (e.g. Email, Manual).</param>
/// <param name="TriggerEntityId">Optional id of the entity that triggered the start (e.g. email message id).</param>
/// <param name="UserId">Acting user id.</param>
/// <param name="Notes">Optional free-text notes.</param>
/// <param name="IsProjectBound">Whether the workflow is truly attached to a project.</param>
/// <param name="InitialStageCode">Optional explicit initial stage code; null lets the engine choose.</param>
public sealed record StartWorkflowCommand(
    int DefinitionId,
    int ProjectId,
    WorkflowTriggerTypeDto TriggerType,
    int? TriggerEntityId,
    int UserId,
    string? Notes,
    bool IsProjectBound = true,
    string? InitialStageCode = null);

/// <summary>Advances an existing workflow to a target stage and provisions the new stage's tasks.</summary>
/// <param name="InstanceId">Workflow instance to advance.</param>
/// <param name="TargetStageId">Stage to advance to.</param>
/// <param name="UserId">Acting user id.</param>
/// <param name="Notes">Optional free-text notes.</param>
public sealed record AdvanceWorkflowCommand(
    int InstanceId,
    int TargetStageId,
    int UserId,
    string? Notes);

/// <summary>Evaluates auto-advance after a task closed, using the task as condition context.</summary>
/// <param name="TaskId">The task that just closed.</param>
/// <param name="UserId">Acting user id.</param>
public sealed record TaskClosedCommand(
    int TaskId,
    int UserId);

/// <summary>Evaluates auto-advance for a stalled workflow (watchdog-triggered), with no task context.</summary>
/// <param name="InstanceId">Workflow instance to re-evaluate.</param>
/// <param name="UserId">Acting (system) user id.</param>
public sealed record StalledWorkflowCommand(
    int InstanceId,
    int UserId);

/// <summary>
/// Evaluates workflow advance after a workflow-advancing Process Action reported Completed
/// (e.g. <c>ApproveOrClose</c> / <c>CloseOpinion</c>). The <paramref name="ActionCode"/> and
/// <paramref name="ActionOutcome"/> are matched against <c>ActionCompleted</c> transition rules
/// from the instance's current stage. Native replacement for the legacy
/// <c>WorkflowActionCompletedHandler</c> bridge.
/// </summary>
/// <param name="InstanceId">Workflow instance whose transitions are evaluated.</param>
/// <param name="ActionCode">The completed action's code (e.g. <c>ApproveOrClose</c>).</param>
/// <param name="ActionOutcome">The completed action's outcome label (e.g. <c>Succeeded</c>).</param>
/// <param name="UserId">Acting user id.</param>
public sealed record ActionCompletedCommand(
    int InstanceId,
    string ActionCode,
    string? ActionOutcome,
    int UserId);

/// <summary>Pauses an active workflow instance (admin lifecycle).</summary>
public sealed record PauseWorkflowCommand(int InstanceId, int UserId, string? Notes);

/// <summary>Resumes a paused workflow instance (admin lifecycle).</summary>
public sealed record ResumeWorkflowCommand(int InstanceId, int UserId, string? Notes);

/// <summary>Marks a workflow instance completed (admin lifecycle).</summary>
public sealed record CompleteWorkflowCommand(int InstanceId, int UserId, string? Notes);

/// <summary>Cancels a workflow instance (admin lifecycle).</summary>
public sealed record CancelWorkflowCommand(int InstanceId, int UserId, string? Notes);
