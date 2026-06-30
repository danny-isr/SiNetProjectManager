using SiNet.Application.Workflow;

namespace SiNet.LegacyBridge.Tasks;

/// <summary>
/// Bridge-local projection of the legacy <c>TaskCompletionCoordinator</c>'s result, expressed
/// without any dependency on <c>SiNetSQL</c>. It carries the workflow auto-advance outcome via the
/// Application-layer <see cref="StageCompletionResultDto"/> (which this assembly may reference) so the
/// official <c>IWorkflowCommandService</c> result flows back through the seam unchanged.
/// </summary>
/// <param name="Success">Whether the completion was accepted.</param>
/// <param name="TaskClosed">Whether the task was closed as a result.</param>
/// <param name="WorkflowAdvanced">Whether a workflow auto-advance was requested/performed.</param>
/// <param name="ErrorMessage">Failure reason when <paramref name="Success"/> is false.</param>
/// <param name="NewProjectStatusId">New broad project status id, if changed.</param>
/// <param name="NewProjectStatusCode">New broad project status code, if changed.</param>
/// <param name="RecordedTaskResultCode">The task-result code recorded, if any.</param>
/// <param name="StageAdvanceResult">The workflow auto-advance outcome, when an advance was attempted.</param>
public sealed record LegacyTaskCompletionResultDto(
    bool Success,
    bool TaskClosed,
    bool WorkflowAdvanced,
    string? ErrorMessage,
    int? NewProjectStatusId,
    string? NewProjectStatusCode,
    string? RecordedTaskResultCode,
    StageCompletionResultDto? StageAdvanceResult);
