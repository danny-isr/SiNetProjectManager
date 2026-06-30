using SiNet.Application.Workflow;

namespace SiNet.Application.Tasks;

/// <summary>
/// Outcome of <see cref="ITaskCompletionService.CompleteAsync"/>. Pure data the UI consumes to
/// refresh views. Mirrors the legacy coordinator's result while staying in the Application layer,
/// and carries the workflow auto-advance outcome via the existing
/// <see cref="StageCompletionResultDto"/> so completion keeps routing through
/// <see cref="IWorkflowCommandService"/>.
/// </summary>
/// <param name="Success">Whether the completion was accepted.</param>
/// <param name="TaskClosed">Whether the task was closed as a result.</param>
/// <param name="WorkflowAdvanced">Whether a workflow auto-advance was requested/performed.</param>
/// <param name="ErrorMessage">Human-readable failure reason when <paramref name="Success"/> is false.</param>
/// <param name="NewProjectStatusId">New broad project status id, if the completion changed it.</param>
/// <param name="NewProjectStatusCode">New broad project status code, if the completion changed it.</param>
/// <param name="RecordedTaskResultCode">The task-result code that was recorded, if any.</param>
/// <param name="StageAdvanceResult">The workflow auto-advance outcome, when an advance was attempted; otherwise <see langword="null"/>.</param>
public sealed record TaskCompletionResultDto(
    bool Success,
    bool TaskClosed,
    bool WorkflowAdvanced,
    string? ErrorMessage = null,
    int? NewProjectStatusId = null,
    string? NewProjectStatusCode = null,
    string? RecordedTaskResultCode = null,
    StageCompletionResultDto? StageAdvanceResult = null)
{
    /// <summary>A failed completion (validation/business rule) carrying an error message.</summary>
    public static TaskCompletionResultDto Failure(string message) =>
        new(Success: false, TaskClosed: false, WorkflowAdvanced: false, ErrorMessage: message);

    /// <summary>
    /// Completion could not run because no completion backend is bound in the current host
    /// (e.g. the new app before the legacy task-completion seam is wired). Distinct from a business
    /// failure so callers can message it differently.
    /// </summary>
    public static TaskCompletionResultDto Unavailable(string message) =>
        new(Success: false, TaskClosed: false, WorkflowAdvanced: false, ErrorMessage: message);
}
