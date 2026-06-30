namespace SiNet.LegacyBridge.Tasks;

/// <summary>
/// Bridge-local projection of the legacy <c>TaskNavigationResolver</c>'s result, expressed without
/// any dependency on <c>SiNetSQL</c> so this assembly stays clean (Application + Domain only).
/// The legacy WPF host maps its <c>TaskNavigationRequest</c> onto this DTO; the
/// <see cref="LegacyTaskNavigationService"/> adapter maps it onto the Application
/// <see cref="SiNet.Application.WorkSurfaces.WorkSurfaceContext"/>.
/// </summary>
/// <param name="TaskId">The resolved task id.</param>
/// <param name="ProjectId">Owning project id; <see langword="null"/> for project-independent tasks.</param>
/// <param name="WorkflowInstanceId">The active workflow instance for the task, when known.</param>
/// <param name="ComponentKey">Stable component key identifying which screen should host the work.</param>
/// <param name="PrimaryWorkTargetEntityId">The exact work-target entity id (legacy ids are <see cref="long"/>); <see langword="null"/> when the task has no concrete target.</param>
/// <param name="AllowedTaskResultCodes">The task-result codes the surface may record on completion.</param>
/// <param name="IsSuccess">Whether the legacy resolver could open the task. When false, the adapter returns a <see langword="null"/> context.</param>
/// <param name="FailureMessage">Resolver failure detail when <paramref name="IsSuccess"/> is false.</param>
/// <param name="CompletionEventCode">
/// The stable completion-event code resolved from the task type, when it is <b>unambiguous</b>;
/// <see langword="null"/> when it cannot be safely derived. Projected by the host from the legacy
/// completion-behavior table; <b>runtime-only</b> (never persisted).
/// </param>
/// <param name="ActingUserId">
/// The authenticated host user id, when the host can supply one; <see langword="null"/> otherwise.
/// <b>Runtime-only</b> (never persisted).
/// </param>
/// <param name="TaskTypeCode">
/// The task type code, when the host can supply one; <see langword="null"/> otherwise. Lets the
/// surface resolve a branching task's completion event from the selected result via the
/// completion-metadata port. <b>Runtime-only</b> (never persisted).
/// </param>
public sealed record LegacyTaskNavigationRequestDto(
    int TaskId,
    int? ProjectId,
    int? WorkflowInstanceId,
    string ComponentKey,
    long? PrimaryWorkTargetEntityId,
    IReadOnlyList<string> AllowedTaskResultCodes,
    bool IsSuccess,
    string? FailureMessage,
    string? CompletionEventCode = null,
    int? ActingUserId = null,
    string? TaskTypeCode = null);
