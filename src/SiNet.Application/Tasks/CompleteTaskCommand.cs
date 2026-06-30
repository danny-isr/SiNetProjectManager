namespace SiNet.Application.Tasks;

/// <summary>
/// Input for <see cref="ITaskCompletionService.CompleteAsync"/>. Collapses the legacy coordinator's
/// positional parameters into a single Application-layer command so callers never depend on the
/// concrete infrastructure signature.
/// </summary>
/// <param name="TaskId">The task being completed.</param>
/// <param name="CompletionEventCode">Stable completion-event code understood by the completion coordinator (e.g. a review/material event).</param>
/// <param name="TaskResultCode">Optional task-result code to record; may be required by the event (must be one of the context's allowed codes).</param>
/// <param name="CompletedTaskLinkIds">Optional ids of the work-target links that were completed (for aggregated tasks).</param>
/// <param name="UserId">Acting user id.</param>
public sealed record CompleteTaskCommand(
    int TaskId,
    string CompletionEventCode,
    string? TaskResultCode,
    IReadOnlyCollection<int>? CompletedTaskLinkIds,
    int UserId);
