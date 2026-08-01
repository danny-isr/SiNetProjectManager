namespace SiNet.Application.WorkSurfaces;

/// <summary>
/// Runtime-only context handed to a <b>Work Surface</b> (a UI screen/window) when it is opened from
/// a task or workflow. It tells the surface exactly what project / task / workflow / work target it
/// operates on, so the screen never has to <i>guess</i> how it was opened.
/// <para>
/// Authoritative rules (see <c>docs/ARCHITECTURE_TARGET.md</c> §4 and <c>docs/AI_DEVELOPMENT_GUIDE.md</c> §3):
/// </para>
/// <list type="bullet">
///   <item>A screen opened from a task receives an <b>explicit</b> context — no first/last fallback.</item>
///   <item>Inspection receives a report target; Email receives email/task/project; ProjectWork receives project/file.</item>
///   <item>This is <b>runtime-only</b>: it requires no database table and is never persisted unless a
///   real persistence requirement appears later.</item>
/// </list>
/// </summary>
/// <param name="TaskId">The task that opened the surface, when launched from a task; <see langword="null"/> for ad-hoc opens.</param>
/// <param name="ProjectId">The owning project id; <c>0</c> when the work is project-independent.</param>
/// <param name="WorkflowInstanceId">The active workflow instance for the project/task, when known.</param>
/// <param name="ComponentKey">Stable key identifying which screen/component should host the work (resolved by task navigation).</param>
/// <param name="PrimaryWorkTargetEntityId">The exact work-target entity to open (e.g. inspection report id); <see langword="null"/> when the task has no concrete target.</param>
/// <param name="AllowedResultCodes">The task-result codes the surface may record on completion (drives the available completion actions).</param>
/// <param name="CompletionEventCode">
/// The stable completion-event code the coordinator should receive for this task, when it can be
/// resolved <b>unambiguously</b> from the task type; <see langword="null"/> when it cannot be safely
/// derived (e.g. a task type whose result is chosen at completion time), in which case the surface
/// must obtain it some other way rather than guess. <b>Runtime-only</b> — never persisted.
/// </param>
/// <param name="ActingUserId">
/// The authenticated host user id to record on completion, when the host can provide one;
/// <see langword="null"/> when no authenticated user is available. <b>Runtime-only</b> — never persisted.
/// </param>
/// <param name="TaskTypeCode">
/// The task type code that opened the surface, when known. It lets the surface resolve the completion
/// event for a <b>branching</b> task (whose chosen result selects between several events) via the
/// completion-metadata port at completion time, without owning a mapping table or guessing;
/// <see langword="null"/> when the host did not supply it. <b>Runtime-only</b> — never persisted.
/// </param>
/// <param name="EmailHints">
/// Optional Email-first open hints (FollowQuoteApproval thread/counterpart filter). Runtime-only.
/// </param>
/// <param name="ProcessDisplayName">Workflow definition name of the Trigger-linked instance (B2 UI).</param>
/// <param name="JobTypeDisplayName">JobType track title of the Trigger-linked instance (B2 UI).</param>
/// <param name="CurrentStageDisplayName">Current stage name of the Trigger-linked instance (B2 UI).</param>
public sealed record WorkSurfaceContext(
    int? TaskId,
    int ProjectId,
    int? WorkflowInstanceId,
    string ComponentKey,
    int? PrimaryWorkTargetEntityId,
    IReadOnlyList<string> AllowedResultCodes,
    string? CompletionEventCode = null,
    int? ActingUserId = null,
    string? TaskTypeCode = null,
    EmailOpenHints? EmailHints = null,
    string? ProcessDisplayName = null,
    string? JobTypeDisplayName = null,
    string? CurrentStageDisplayName = null);

/// <summary>Runtime-only Email open filter hints (e.g. FollowQuoteApproval).</summary>
public sealed record EmailOpenHints(
    string? GmailThreadId,
    string? AfterGmailMessageId,
    string? CounterpartAddress,
    bool OfferProjectWorkFallback);
