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
