namespace SiNet.Application.Tasks;

/// <summary>
/// Completes task work and is the <b>bridge back into workflow</b>: it records the result, completes
/// the selected work targets, closes the task according to policy, and routes workflow auto-advance
/// through <see cref="Workflow.IWorkflowCommandService"/>. Feature screens complete work through this
/// port; they must never advance workflow directly (see <c>docs/AI_DEVELOPMENT_GUIDE.md</c> §2 rule 11).
/// </summary>
public interface ITaskCompletionService
{
    /// <summary>
    /// Applies the completion described by <paramref name="command"/> and returns the outcome,
    /// including any workflow auto-advance result. Implementations must not throw for ordinary
    /// validation/business failures — they return a non-successful
    /// <see cref="TaskCompletionResultDto"/> instead.
    /// </summary>
    ValueTask<TaskCompletionResultDto> CompleteAsync(CompleteTaskCommand command, CancellationToken ct);
}
