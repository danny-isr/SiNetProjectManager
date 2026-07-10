using System.Threading;
using System.Threading.Tasks;

namespace SiNet.Application.Workflow;

/// <summary>
/// Write port for workflow commands (start, advance, and auto-advance evaluation).
/// <para>
/// Lives in the Application layer and exposes clean command/DTO types only; EF entities never
/// cross this boundary. The SQL infrastructure implements this port by delegating to the
/// existing workflow orchestrator and mapping its results to Application DTOs.
/// </para>
/// <para>
/// This is the write counterpart to <see cref="IWorkflowQueryService"/>. It is introduced
/// additively: the concrete orchestrator and its result records remain in place while consumers
/// migrate onto this port.
/// </para>
/// </summary>
public interface IWorkflowCommandService
{
    /// <summary>Starts a workflow instance and provisions its initial-stage tasks.</summary>
    ValueTask<WorkflowStartResultDto> StartAsync(StartWorkflowCommand command, CancellationToken ct);

    /// <summary>Advances a workflow to a target stage and provisions the new stage's tasks.</summary>
    ValueTask<WorkflowAdvanceResultDto> AdvanceAsync(AdvanceWorkflowCommand command, CancellationToken ct);

    /// <summary>
    /// Evaluates auto-advance after a task closed. Returns <see langword="null"/> when the task
    /// is not linked to a workflow or no transition applies.
    /// </summary>
    ValueTask<StageCompletionResultDto?> CheckAndAutoAdvanceAsync(TaskClosedCommand command, CancellationToken ct);

    /// <summary>
    /// Evaluates auto-advance for a stalled workflow (watchdog-triggered). Returns
    /// <see langword="null"/> when the workflow is not active or no transition applies.
    /// </summary>
    ValueTask<StageCompletionResultDto?> CheckAndAutoAdvanceStalledAsync(StalledWorkflowCommand command, CancellationToken ct);

    /// <summary>
    /// Re-provisions the current stage's tasks for a stalled workflow that has no open tasks
    /// and could not be auto-advanced (last-resort watchdog recovery). Returns the number of
    /// tasks created.
    /// </summary>
    ValueTask<int> ReprovisionStalledStageTasksAsync(StalledWorkflowCommand command, CancellationToken ct);

    /// <summary>Pauses an active workflow instance.</summary>
    ValueTask PauseAsync(PauseWorkflowCommand command, CancellationToken ct);

    /// <summary>Resumes a paused workflow instance.</summary>
    ValueTask ResumeAsync(ResumeWorkflowCommand command, CancellationToken ct);

    /// <summary>Completes a workflow instance (admin lifecycle).</summary>
    ValueTask CompleteInstanceAsync(CompleteWorkflowCommand command, CancellationToken ct);

    /// <summary>Cancels a workflow instance.</summary>
    ValueTask CancelAsync(CancelWorkflowCommand command, CancellationToken ct);
}
