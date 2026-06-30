using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.Application.Identity;
using SiNet.Application.Tasks;
using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.Inspection;

/// <summary>
/// Root view model for the rebuilt Inspection screen. It composes the five sub-area view models
/// (tree, notes, drawings, reviewed plan, report) so the screen can be developed and migrated one
/// area at a time. This is the new target UI foundation — it does NOT replace the legacy
/// <c>FloatingInspectionViewModel</c> window yet. It coordinates the read-only flow: when the tree's
/// selected report changes, the notes area reloads. Sub-areas are injected so each can evolve
/// independently and be unit-tested in isolation.
/// <para>
/// Task mode (workflow-first): <see cref="OpenFromTaskAsync"/> resolves a
/// <see cref="WorkSurfaceContext"/> through <see cref="ITaskNavigationService"/> and opens the
/// <em>exact</em> report from <see cref="WorkSurfaceContext.PrimaryWorkTargetEntityId"/> — never a
/// first/last fallback. The shell does not start, advance, or auto-advance workflow; completion will
/// later route through <see cref="ITaskCompletionService"/> which bridges to
/// <c>IWorkflowCommandService</c>.
/// </para>
/// </summary>
public sealed class InspectionShellViewModel : ObservableObject
{
    /// <summary>Component key this surface honours when opened from a task.</summary>
    public const string InspectionComponentKey = "Inspection";

    private readonly ITaskNavigationService _taskNavigation;
    private readonly ITaskCompletionService _taskCompletion;
    private readonly ICurrentUserContext? _currentUser;
    private readonly ITaskCompletionMetadataResolver? _completionMetadata;
    private bool _isTaskMode;
    private bool _isBusy;
    private string? _taskStatusMessage;
    private WorkSurfaceContext? _context;

    // Admin/dev completion inputs. These remain ONLY as a fallback: when the work context (or host
    // user context) can supply a value safely, it is auto-filled and the corresponding input is
    // hidden (see NeedsManualCompletionEventCode / NeedsManualActingUserId). They are never guessed:
    //   - CompletionEventCode is auto-filled from WorkSurfaceContext.CompletionEventCode, which the
    //     host resolves only when the task type maps to exactly one completion event.
    //   - ActingUserId is auto-filled from the authenticated host user (WorkSurfaceContext.ActingUserId
    //     or ICurrentUserContext.UserId).
    // If neither source yields a value, the dev input stays visible so the user can supply it.
    private string? _completionEventCode;
    private int _actingUserId;
    private string? _selectedResultCode;
    private bool _hasResolvedCompletionEventCode;
    private bool _hasResolvedActingUserId;

    public InspectionShellViewModel(
        InspectionTreeViewModel tree,
        InspectionNotesViewModel notes,
        InspectionDrawingsViewModel drawings,
        InspectionReviewedPlanViewModel reviewedPlan,
        InspectionReportViewModel report,
        ITaskNavigationService taskNavigation,
        ITaskCompletionService taskCompletion,
        ICurrentUserContext? currentUser = null,
        ITaskCompletionMetadataResolver? completionMetadata = null)
    {
        Tree = tree;
        Notes = notes;
        Drawings = drawings;
        ReviewedPlan = reviewedPlan;
        Report = report;
        _taskNavigation = taskNavigation;
        _taskCompletion = taskCompletion;
        _currentUser = currentUser;
        _completionMetadata = completionMetadata;

        Tree.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName == nameof(InspectionTreeViewModel.SelectedReport))
            {
                await Notes.LoadNotesAsync(Tree.SelectedReport?.ReportId).ConfigureAwait(true);
            }
        };

        CompleteTaskCommand = new AsyncRelayCommand(
            CompleteFromCommandAsync,
            () => CanCompleteInTaskMode);
    }

    public string Title => "Inspection (new screen foundation)";

    public InspectionTreeViewModel Tree { get; }

    public InspectionNotesViewModel Notes { get; }

    public InspectionDrawingsViewModel Drawings { get; }

    public InspectionReviewedPlanViewModel ReviewedPlan { get; }

    public InspectionReportViewModel Report { get; }

    /// <summary>The work-surface context this shell was opened with, when launched from a task.</summary>
    public WorkSurfaceContext? Context
    {
        get => _context;
        private set
        {
            if (SetField(ref _context, value))
            {
                ResolveCompletionInputsFromContext(value);
                OnPropertyChanged(nameof(AllowedResultCodes));
                OnPropertyChanged(nameof(HasMultipleAllowedResultCodes));
                OnPropertyChanged(nameof(CanCompleteInTaskMode));
                CompleteTaskCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary><see langword="true"/> when the shell was opened from a task (vs. free browsing).</summary>
    public bool IsTaskMode
    {
        get => _isTaskMode;
        private set
        {
            if (SetField(ref _isTaskMode, value))
            {
                OnPropertyChanged(nameof(CanCompleteInTaskMode));
                CompleteTaskCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary><see langword="true"/> while resolving/loading a task target.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanCompleteInTaskMode));
                CompleteTaskCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Human-readable task-mode status/error (e.g. "Opened report #N" or a clear failure such as a
    /// missing target). Bound by the view so the user sees why a task could/couldn't open.
    /// </summary>
    public string? TaskStatusMessage
    {
        get => _taskStatusMessage;
        private set => SetField(ref _taskStatusMessage, value);
    }

    /// <summary>
    /// The stable completion-event code understood by the legacy coordinator. Auto-filled from
    /// <see cref="WorkSurfaceContext.CompletionEventCode"/> when the host could resolve it
    /// unambiguously (see <see cref="NeedsManualCompletionEventCode"/>); otherwise it remains an
    /// explicit admin/dev input because this slice must not guess one.
    /// </summary>
    public string? CompletionEventCode
    {
        get => _completionEventCode;
        set
        {
            if (SetField(ref _completionEventCode, value))
            {
                OnPropertyChanged(nameof(CanCompleteInTaskMode));
                CompleteTaskCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// The acting user id recorded on completion. Auto-filled from the authenticated host user
    /// (<see cref="WorkSurfaceContext.ActingUserId"/> or <see cref="ICurrentUserContext.UserId"/>)
    /// when available (see <see cref="NeedsManualActingUserId"/>); otherwise it remains an explicit
    /// admin/dev input because this slice must not invent a user id.
    /// </summary>
    public int ActingUserId
    {
        get => _actingUserId;
        set => SetField(ref _actingUserId, value);
    }

    /// <summary>
    /// <see langword="true"/> when the completion-event code could <b>not</b> be resolved safely from
    /// the task context, so the view must keep the explicit event-code input. <see langword="false"/>
    /// once it has been auto-filled from <see cref="WorkSurfaceContext.CompletionEventCode"/> (the
    /// unambiguous-by-task-type case) <b>or</b> can be resolved from the currently selected result via
    /// the completion-metadata port (the branching case, e.g. <c>RecheckPlan</c>). Never guesses: if no
    /// source can supply an event code, the manual input stays.
    /// </summary>
    public bool NeedsManualCompletionEventCode
        => !_hasResolvedCompletionEventCode && TryResolveEventFromSelectedResult() is null;

    /// <summary>
    /// <see langword="true"/> when the acting user id could <b>not</b> be resolved from the host user
    /// context, so the view must keep the explicit user-id input. <see langword="false"/> once it has
    /// been auto-filled from the authenticated host user.
    /// </summary>
    public bool NeedsManualActingUserId => !_hasResolvedActingUserId;

    /// <summary>
    /// The user-chosen task-result code (used only when the context allows more than one). Left
    /// <see langword="null"/> otherwise; the resolution/validation stays in
    /// <see cref="CompleteFromTaskAsync"/> so the command never bypasses the guardrails.
    /// </summary>
    public string? SelectedResultCode
    {
        get => _selectedResultCode;
        set
        {
            if (SetField(ref _selectedResultCode, value))
            {
                // For a branching task the chosen result is what selects the completion event, so the
                // "needs a manual event code" state can change as soon as the selection changes.
                OnPropertyChanged(nameof(NeedsManualCompletionEventCode));
            }
        }
    }

    /// <summary>
    /// The result codes the current context permits, surfaced for an optional picker. Empty when
    /// there is no task context.
    /// </summary>
    public IReadOnlyList<string> AllowedResultCodes
        => Context?.AllowedResultCodes ?? Array.Empty<string>();

    /// <summary>
    /// <see langword="true"/> when the context allows more than one result code, so the view shows
    /// a picker. With zero or one allowed code the picker is hidden (the single/none case is
    /// resolved automatically and never guessed).
    /// </summary>
    public bool HasMultipleAllowedResultCodes => AllowedResultCodes.Count > 1;

    /// <summary>
    /// <see langword="true"/> only when completion is meaningful: the shell is in task mode, was
    /// opened from a real task (context with a <see cref="WorkSurfaceContext.TaskId"/>), and is not
    /// busy. The view binds this to show/enable the minimal completion trigger. All deeper
    /// validation (result-code resolution, event code) remains in <see cref="CompleteFromTaskAsync"/>.
    /// </summary>
    public bool CanCompleteInTaskMode
        => IsTaskMode && !IsBusy && Context is { TaskId: not null };

    /// <summary>
    /// The single minimal UI trigger for this slice. Bound to one "Complete Task" button; it reads
    /// the admin/dev inputs and delegates to <see cref="CompleteFromTaskAsync"/>, which is the only
    /// completion boundary. The command never touches workflow directly.
    /// </summary>
    public AsyncRelayCommand CompleteTaskCommand { get; }

    private Task CompleteFromCommandAsync()
        => CompleteFromTaskAsync(
            CompletionEventCode ?? string.Empty,
            ActingUserId,
            string.IsNullOrWhiteSpace(SelectedResultCode) ? null : SelectedResultCode);

    /// <summary>
    /// Auto-fills the completion inputs from the resolved <paramref name="context"/> (and the host
    /// user context) so the view can hide whichever value was obtained safely. Nothing here is
    /// guessed: an input is only marked resolved when a concrete value was supplied, and a blank/zero
    /// value leaves the explicit admin/dev input in place.
    /// </summary>
    private void ResolveCompletionInputsFromContext(WorkSurfaceContext? context)
    {
        // Completion event code: trust only an unambiguous code projected by the host. Anything else
        // keeps the manual input.
        if (!string.IsNullOrWhiteSpace(context?.CompletionEventCode))
        {
            CompletionEventCode = context.CompletionEventCode;
            _hasResolvedCompletionEventCode = true;
        }
        else
        {
            _hasResolvedCompletionEventCode = false;
        }

        // Acting user id: prefer the value the host put on the context; otherwise ask the injected
        // host user context. A positive id is required — null/zero is treated as "unknown".
        var resolvedUserId = context?.ActingUserId ?? _currentUser?.UserId;
        if (resolvedUserId is > 0)
        {
            ActingUserId = resolvedUserId.Value;
            _hasResolvedActingUserId = true;
        }
        else
        {
            _hasResolvedActingUserId = false;
        }

        OnPropertyChanged(nameof(NeedsManualCompletionEventCode));
        OnPropertyChanged(nameof(NeedsManualActingUserId));
    }

    /// <summary>
    /// Resolves the completion-event code from the currently selected result via the
    /// <see cref="ITaskCompletionMetadataResolver"/> port, reusing the host's existing declarative
    /// mapping. Returns <see langword="null"/> when no resolver/context is available, the task type is
    /// unknown, or the <c>(task type, result)</c> pair is unsupported/ambiguous — never an invented
    /// code. Pure: it only reads current state.
    /// </summary>
    private string? TryResolveEventFromSelectedResult()
    {
        if (_completionMetadata is null)
            return null;

        if (Context is not { TaskTypeCode: { } taskTypeCode } || string.IsNullOrWhiteSpace(taskTypeCode))
            return null;

        var resultCode = string.IsNullOrWhiteSpace(SelectedResultCode) ? null : SelectedResultCode;

        // For a branching task the result is what selects the event; without a selection the resolver
        // can only answer for the unambiguous-by-task-type case, which the context already projected.
        if (resultCode is null && HasMultipleAllowedResultCodes)
            return null;

        return _completionMetadata.ResolveCompletionEventCode(taskTypeCode, resultCode);
    }

    /// <summary>
    /// Opens this Inspection surface for a workflow-created task. Resolves the
    /// <see cref="WorkSurfaceContext"/> via <see cref="ITaskNavigationService"/>, validates it targets
    /// the Inspection component, then selects the <em>exact</em> report from
    /// <see cref="WorkSurfaceContext.PrimaryWorkTargetEntityId"/>. There is no fallback to the first or
    /// last report: if the target is missing the surface shows a clear error and selects nothing. The
    /// shell never mutates workflow here.
    /// </summary>
    /// <param name="taskId">The task to open from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if the exact target opened; otherwise <see langword="false"/>.</returns>
    public async Task<bool> OpenFromTaskAsync(int taskId, CancellationToken cancellationToken = default)
    {
        IsTaskMode = true;
        IsBusy = true;
        TaskStatusMessage = null;
        Context = null;
        SelectedResultCode = null;
        try
        {
            var context = await _taskNavigation.ResolveAsync(taskId, cancellationToken).ConfigureAwait(true);
            if (context is null)
            {
                TaskStatusMessage =
                    $"Could not open task #{taskId}. Task navigation is unavailable in this host yet, " +
                    "or the task could not be resolved to a work target.";
                return false;
            }

            Context = context;

            if (!string.Equals(context.ComponentKey, InspectionComponentKey, StringComparison.OrdinalIgnoreCase))
            {
                TaskStatusMessage =
                    $"Task #{taskId} targets '{context.ComponentKey}', which is not the Inspection surface.";
                return false;
            }

            if (context.PrimaryWorkTargetEntityId is not { } reportId)
            {
                // No concrete report target and this task type does not allow creating one here.
                TaskStatusMessage =
                    $"Task #{taskId} has no inspection report target, and creating one is not supported here.";
                return false;
            }

            var opened = await Tree
                .SelectReportByIdAsync(context.ProjectId, reportId, cancellationToken)
                .ConfigureAwait(true);

            TaskStatusMessage = opened
                ? $"Opened inspection report #{reportId} for task #{taskId} (read-only)."
                : $"Inspection report #{reportId} for task #{taskId} was not found in project {context.ProjectId}.";

            return opened;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Completes the task this surface was opened from, through the official path
    /// <see cref="ITaskCompletionService"/> → (host) legacy completion seam → the legacy task
    /// completion coordinator → <c>IWorkflowCommandService</c>. The shell <b>never</b> mutates
    /// workflow itself: it only records the completion intent and surfaces the outcome.
    /// <para>
    /// This is the single, minimal completion path for the Inspection slice. It refuses to act
    /// (and shows a clear message) when there is no <see cref="Context"/>, no
    /// <see cref="WorkSurfaceContext.TaskId"/>, or when a task-result code is required but cannot be
    /// determined without guessing:
    /// </para>
    /// <list type="bullet">
    ///   <item>If the context exposes exactly one allowed result code, that code is used.</item>
    ///   <item>If it exposes several, an explicit <paramref name="taskResultCode"/> must be supplied
    ///   and must be one of <see cref="WorkSurfaceContext.AllowedResultCodes"/> — never invented.</item>
    ///   <item>If it exposes none, completion proceeds with no result code (the event may not need one).</item>
    /// </list>
    /// </summary>
    /// <param name="completionEventCode">Stable completion-event code understood by the coordinator (required).</param>
    /// <param name="actingUserId">The acting user id recorded on the completion.</param>
    /// <param name="taskResultCode">
    /// The task-result code to record. Required only when the context allows more than one; otherwise
    /// optional. When supplied it must be one of <see cref="WorkSurfaceContext.AllowedResultCodes"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> if the completion was accepted; otherwise <see langword="false"/>.</returns>
    public async Task<bool> CompleteFromTaskAsync(
        string completionEventCode,
        int actingUserId,
        string? taskResultCode = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(completionEventCode))
        {
            TaskStatusMessage = "Cannot complete: no completion event was specified.";
            return false;
        }

        if (Context is not { } context)
        {
            TaskStatusMessage = "Cannot complete: this surface was not opened from a task.";
            return false;
        }

        if (context.TaskId is not { } taskId)
        {
            TaskStatusMessage = "Cannot complete: the work context has no task to complete.";
            return false;
        }

        if (!TryResolveResultCode(context, taskResultCode, out var resolvedResultCode, out var resultMessage))
        {
            TaskStatusMessage = resultMessage;
            return false;
        }

        IsBusy = true;
        try
        {
            var result = await _taskCompletion
                .CompleteAsync(
                    new CompleteTaskCommand(
                        TaskId: taskId,
                        CompletionEventCode: completionEventCode,
                        TaskResultCode: resolvedResultCode,
                        CompletedTaskLinkIds: null,
                        UserId: actingUserId),
                    cancellationToken)
                .ConfigureAwait(true);

            if (!result.Success)
            {
                TaskStatusMessage =
                    $"Could not complete task #{taskId}: {result.ErrorMessage ?? "the completion was rejected."}";
                return false;
            }

            var closed = result.TaskClosed ? " Task closed." : string.Empty;
            var advanced = result.WorkflowAdvanced ? " Workflow advanced." : string.Empty;
            TaskStatusMessage = $"Completed task #{taskId}.{closed}{advanced}";
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Determines the task-result code to record without ever inventing one. Returns
    /// <see langword="false"/> with a clear <paramref name="message"/> when a result code is required
    /// (multiple allowed) but cannot be resolved from <see cref="WorkSurfaceContext.AllowedResultCodes"/>.
    /// </summary>
    private static bool TryResolveResultCode(
        WorkSurfaceContext context,
        string? requestedResultCode,
        out string? resolvedResultCode,
        out string? message)
    {
        var allowed = context.AllowedResultCodes;
        message = null;

        if (!string.IsNullOrWhiteSpace(requestedResultCode))
        {
            // An explicit choice must be one of the allowed codes — never accept an invented value.
            if (allowed.Contains(requestedResultCode, StringComparer.Ordinal))
            {
                resolvedResultCode = requestedResultCode;
                return true;
            }

            resolvedResultCode = null;
            message =
                $"Cannot complete: result '{requestedResultCode}' is not one of the allowed results " +
                $"({string.Join(", ", allowed)}).";
            return false;
        }

        switch (allowed.Count)
        {
            case 0:
                // The event may not require a result code; proceed without one.
                resolvedResultCode = null;
                return true;
            case 1:
                resolvedResultCode = allowed[0];
                return true;
            default:
                // Multiple allowed codes and the caller didn't pick one — do not guess silently.
                resolvedResultCode = null;
                message =
                    "Cannot complete: this task allows multiple results " +
                    $"({string.Join(", ", allowed)}); choose one before completing.";
                return false;
        }
    }
}
