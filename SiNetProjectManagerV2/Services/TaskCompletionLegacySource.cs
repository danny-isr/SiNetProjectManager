using SiNet.LegacyBridge.Tasks;
using SiNetSQL.Services.Tasks;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Binds the new <see cref="ILegacyTaskCompletionSource"/> strangler seam to the existing legacy
/// <see cref="ITaskCompletionCoordinator"/> — the single decision point that records the result,
/// completes the selected work targets, closes the task per policy, and routes workflow auto-advance
/// through the official <c>IWorkflowCommandService.CheckAndAutoAdvanceAsync</c>.
/// <para>
/// This is the host-side fulfilment that lets the new Inspection Work Surface complete a task through
/// the official path
/// (<c>ITaskCompletionService</c> → seam → <c>TaskCompletionCoordinator</c> → <c>IWorkflowCommandService</c>).
/// It is the single place that knows both worlds for task completion: it projects the bridge-local
/// <see cref="LegacyCompleteTaskCommandDto"/> onto the coordinator's positional parameters and projects
/// the resulting <see cref="TaskCompletionResult"/> back into a bridge-local
/// <see cref="LegacyTaskCompletionResultDto"/> (no <c>SiNetSQL</c> type crosses the boundary, and the
/// Application-layer <c>StageCompletionResultDto</c> auto-advance outcome flows back unchanged).
/// </para>
/// <para>
/// <b>No workflow mutation here:</b> this adapter never calls <c>WorkflowEngine</c> or
/// <c>WorkflowTaskOrchestrator</c>. All workflow side effects stay inside the coordinator, which is the
/// only component allowed to invoke the workflow command port.
/// </para>
/// <para>
/// <b>Failures don't crash the UI:</b> ordinary validation/business failures already come back as a
/// non-successful <see cref="TaskCompletionResult"/>. As a defensive belt-and-braces measure, any
/// unexpected exception is also turned into a non-successful <see cref="LegacyTaskCompletionResultDto"/>
/// so the work surface can show a clear message instead of faulting.
/// </para>
/// </summary>
internal sealed class TaskCompletionLegacySource : ILegacyTaskCompletionSource
{
    private readonly ITaskCompletionCoordinator _coordinator;

    public TaskCompletionLegacySource(ITaskCompletionCoordinator coordinator)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    public async ValueTask<LegacyTaskCompletionResultDto> CompleteAsync(
        LegacyCompleteTaskCommandDto command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TaskCompletionResult result;
        try
        {
            result = await _coordinator
                .CompleteAsync(
                    taskId: command.TaskId,
                    completionEventCode: command.CompletionEventCode,
                    taskResultCode: command.TaskResultCode,
                    completedTaskLinkIds: command.CompletedTaskLinkIds,
                    payload: null,
                    userId: command.UserId,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not a business failure — let it propagate so callers can react.
            throw;
        }
        catch (Exception ex)
        {
            // Defensive: ordinary failures should already be non-success results, but never let an
            // unexpected exception bubble into the UI as a crash.
            return new LegacyTaskCompletionResultDto(
                Success: false,
                TaskClosed: false,
                WorkflowAdvanced: false,
                ErrorMessage: $"Task completion failed: {ex.Message}",
                NewProjectStatusId: null,
                NewProjectStatusCode: null,
                RecordedTaskResultCode: null,
                StageAdvanceResult: null);
        }

        var mapped = new LegacyTaskCompletionResultDto(
            Success: result.Success,
            TaskClosed: result.TaskClosed,
            WorkflowAdvanced: result.WorkflowAdvanced,
            ErrorMessage: result.ErrorMessage,
            NewProjectStatusId: result.NewProjectStatusId,
            NewProjectStatusCode: result.NewProjectStatusCode,
            RecordedTaskResultCode: result.RecordedTaskResultCode,
            StageAdvanceResult: result.StageAdvanceResult);

        if (result.Success)
            SiNetSQL.Services.ActiveProjectContext.Instance.NotifyTaskDataChanged();

        return mapped;
    }
}
