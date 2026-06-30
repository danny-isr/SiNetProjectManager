namespace SiNet.LegacyBridge.Tasks;

/// <summary>
/// Bridge-local input mirroring the legacy <c>TaskCompletionCoordinator.CompleteAsync</c> positional
/// parameters, kept free of any <c>SiNetSQL</c> types. The <see cref="LegacyTaskCompletionService"/>
/// adapter maps the Application <c>CompleteTaskCommand</c> onto this DTO before handing it to the
/// legacy seam.
/// </summary>
/// <param name="TaskId">The task being completed.</param>
/// <param name="CompletionEventCode">Stable completion-event code understood by the legacy coordinator.</param>
/// <param name="TaskResultCode">Optional task-result code to record; may be required by the event.</param>
/// <param name="CompletedTaskLinkIds">Optional ids of the work-target links that were completed.</param>
/// <param name="UserId">Acting user id.</param>
public sealed record LegacyCompleteTaskCommandDto(
    int TaskId,
    string CompletionEventCode,
    string? TaskResultCode,
    IReadOnlyCollection<int>? CompletedTaskLinkIds,
    int UserId);
