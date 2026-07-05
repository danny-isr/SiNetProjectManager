using SiNet.Application.Tasks;

namespace SiNet.LegacyBridge.Tasks;

/// <summary>
/// <b>Temporary migration bridge — not target architecture.</b> Candidate for future removal once all
/// hosts consume native SqlTaskCompletionService. Do not use for new Work Surfaces unless explicitly approved.
/// Strangler adapter that implements the new <see cref="ITaskCompletionService"/> Application port by
/// delegating to the legacy-host <see cref="ILegacyTaskCompletionSource"/> seam. It maps the
/// Application <see cref="CompleteTaskCommand"/> onto the bridge-local
/// <see cref="LegacyCompleteTaskCommandDto"/> and projects the legacy result back into a
/// <see cref="TaskCompletionResultDto"/>.
/// <para>
/// The seam is optional: when no host binds it (the new <c>SiNet.App.Wpf</c> shell during early
/// migration), the adapter returns <see cref="TaskCompletionResultDto.Unavailable"/> so the work
/// surface can keep navigating/loading read-only while reporting that completion is not wired yet. The
/// legacy WPF host binds a real source backed by <c>TaskCompletionCoordinator</c>, which routes
/// workflow auto-advance through the official <c>IWorkflowCommandService.CheckAndAutoAdvanceAsync</c>.
/// Replace this with a native infrastructure implementation once task completion is fully migrated.
/// </para>
/// </summary>
internal sealed class LegacyTaskCompletionService : ITaskCompletionService
{
    private readonly ILegacyTaskCompletionSource? _source;

    public LegacyTaskCompletionService(ILegacyTaskCompletionSource? source = null)
    {
        _source = source;
    }

    public async ValueTask<TaskCompletionResultDto> CompleteAsync(
        CompleteTaskCommand command, CancellationToken ct)
    {
        if (_source is null)
        {
            return TaskCompletionResultDto.Unavailable(
                "Task completion is not available in this host yet. " +
                "The legacy task-completion seam is not bound, so completion was not recorded.");
        }

        var legacyResult = await _source.CompleteAsync(
            new LegacyCompleteTaskCommandDto(
                TaskId: command.TaskId,
                CompletionEventCode: command.CompletionEventCode,
                TaskResultCode: command.TaskResultCode,
                CompletedTaskLinkIds: command.CompletedTaskLinkIds,
                UserId: command.UserId),
            ct).ConfigureAwait(false);

        return new TaskCompletionResultDto(
            Success: legacyResult.Success,
            TaskClosed: legacyResult.TaskClosed,
            WorkflowAdvanced: legacyResult.WorkflowAdvanced,
            ErrorMessage: legacyResult.ErrorMessage,
            NewProjectStatusId: legacyResult.NewProjectStatusId,
            NewProjectStatusCode: legacyResult.NewProjectStatusCode,
            RecordedTaskResultCode: legacyResult.RecordedTaskResultCode,
            StageAdvanceResult: legacyResult.StageAdvanceResult);
    }
}
