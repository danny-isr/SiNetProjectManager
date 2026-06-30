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
    private bool _isTaskMode;
    private bool _isBusy;
    private string? _taskStatusMessage;
    private WorkSurfaceContext? _context;

    public InspectionShellViewModel(
        InspectionTreeViewModel tree,
        InspectionNotesViewModel notes,
        InspectionDrawingsViewModel drawings,
        InspectionReviewedPlanViewModel reviewedPlan,
        InspectionReportViewModel report,
        ITaskNavigationService taskNavigation)
    {
        Tree = tree;
        Notes = notes;
        Drawings = drawings;
        ReviewedPlan = reviewedPlan;
        Report = report;
        _taskNavigation = taskNavigation;

        Tree.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName == nameof(InspectionTreeViewModel.SelectedReport))
            {
                await Notes.LoadNotesAsync(Tree.SelectedReport?.ReportId).ConfigureAwait(true);
            }
        };
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
        private set => SetField(ref _context, value);
    }

    /// <summary><see langword="true"/> when the shell was opened from a task (vs. free browsing).</summary>
    public bool IsTaskMode
    {
        get => _isTaskMode;
        private set => SetField(ref _isTaskMode, value);
    }

    /// <summary><see langword="true"/> while resolving/loading a task target.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
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
}
