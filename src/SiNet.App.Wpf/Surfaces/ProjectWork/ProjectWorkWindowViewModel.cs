using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.Application.Identity;
using SiNet.Application.Projects;
using SiNet.Application.Tasks;
using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.Surfaces.ProjectWork;

/// <summary>
/// View model for <see cref="ProjectWorkWindowView"/> — the native task-execution shell for the
/// project-scoped component keys (<c>ProjectWork</c> / <c>MaterialChecklist</c> /
/// <c>PoliceSubmission</c>) that legacy routed to <c>ProjectWorkView</c>.
/// <para>
/// Scope for Phase 5a is intentionally the <b>task-execution shell</b>: it binds the task's project
/// as current context, presents the task header + allowed results, and completes the task through
/// <see cref="ITaskCompletionService"/> (mirroring the Inspection completion contract). The full
/// project file tree / ACC viewer / drag-drop from the legacy screen is a separate, gated phase and
/// is represented here by a read-only work-area placeholder.
/// </para>
/// </summary>
public sealed class ProjectWorkWindowViewModel : ObservableObject
{
    private readonly ITaskCompletionService? _taskCompletion;
    private readonly ITaskCompletionMetadataResolver? _completionMetadata;
    private readonly ICurrentUserContext? _currentUser;
    private readonly ICurrentProjectContext? _currentProject;
    private readonly IProjectQueryService? _projectQuery;

    private WorkSurfaceContext? _taskContext;
    private bool _loaded;
    private bool _isBusy;
    private string _activeProjectDisplay = "\u05E4\u05E8\u05D5\u05D9\u05E7\u05D8 \u05DC\u05D3\u05D5\u05D2\u05DE\u05D4 \u2014 \u05E1\u05D1\u05D9\u05D1\u05EA \u05E2\u05D1\u05D5\u05D3\u05D4";
    private string _taskHeader = string.Empty;
    private string _statusMessage = "\u05DE\u05D5\u05DB\u05DF";
    private string? _selectedResultCode;

    /// <summary>Design-time / standalone constructor.</summary>
    public ProjectWorkWindowViewModel()
        : this(null)
    {
        _taskHeader = "\u05DE\u05E9\u05D9\u05DE\u05EA \u05E2\u05D1\u05D5\u05D3\u05D4 (\u05EA\u05E6\u05D5\u05D2\u05EA \u05E2\u05D9\u05E6\u05D5\u05D1)";
        AllowedResultCodes.Add("MaterialComplete");
        AllowedResultCodes.Add("MaterialMissing");
    }

    public ProjectWorkWindowViewModel(
        ITaskCompletionService? taskCompletion,
        ITaskCompletionMetadataResolver? completionMetadata = null,
        ICurrentUserContext? currentUser = null,
        ICurrentProjectContext? currentProject = null,
        IProjectQueryService? projectQuery = null)
    {
        _taskCompletion = taskCompletion;
        _completionMetadata = completionMetadata;
        _currentUser = currentUser;
        _currentProject = currentProject;
        _projectQuery = projectQuery;

        AllowedResultCodes = new ObservableCollection<string>();

        CompleteTaskCommand = new AsyncRelayCommand(
            async () => { _ = await CompleteFromTaskAsync().ConfigureAwait(true); },
            () => CanCompleteTask);
    }

    public string Title => "\u05E1\u05D1\u05D9\u05D1\u05EA \u05E2\u05D1\u05D5\u05D3\u05D4";

    /// <summary>
    /// Read-only placeholder for the deferred project file workspace (tree / ACC viewer). Phase 5a is
    /// the task shell only; the file management surface is a later gated phase.
    /// </summary>
    public string WorkAreaMessage =>
        "\u05e1\u05d1\u05d9\u05d1\u05ea \u05e7\u05d1\u05e6\u05d9 \u05d4\u05e4\u05e8\u05d5\u05d9\u05e7\u05d8 (\u05e2\u05e5 \u05ea\u05d9\u05e7\u05d9\u05d5\u05ea / \u05e6\u05d5\u05e4\u05d4 ACC) \u05ea\u05d9\u05e4\u05ea\u05d7 \u05db\u05d0\u05df \u05d1\u05e9\u05dc\u05d1 \u05e2\u05d5\u05e7\u05d1. " +
        "\u05db\u05e8\u05d2\u05e2 \u05e0\u05d9\u05ea\u05df \u05dc\u05e1\u05d2\u05d5\u05e8 \u05d0\u05ea \u05d4\u05de\u05e9\u05d9\u05de\u05d4 \u05de\u05db\u05d0\u05df.";

    public string ActiveProjectDisplay
    {
        get => _activeProjectDisplay;
        private set => SetField(ref _activeProjectDisplay, value);
    }

    public string TaskHeader
    {
        get => _taskHeader;
        private set => SetField(ref _taskHeader, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                (CompleteTaskCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CanCompleteTask));
            }
        }
    }

    public ObservableCollection<string> AllowedResultCodes { get; }

    public string? SelectedResultCode
    {
        get => _selectedResultCode;
        set
        {
            if (SetField(ref _selectedResultCode, value))
                (CompleteTaskCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public bool IsTaskMode => _taskContext is not null;

    public WorkSurfaceContext? TaskContext => _taskContext;

    public bool CanCompleteTask =>
        IsTaskMode
        && !IsBusy
        && _loaded
        && _taskCompletion is not null
        && _taskContext?.TaskId is > 0;

    public ICommand CompleteTaskCommand { get; }

    /// <summary>
    /// Task-mode entry. Validates the component key + project, binds the project as current context,
    /// and prepares the completion strip. Returns <see langword="false"/> (so the launcher does not
    /// <c>Show()</c>) when the context is not a ProjectWork surface or has no project.
    /// </summary>
    public void ApplyContext(WorkSurfaceContext? context)
    {
        _ = ApplyContextAsync(context);
    }

    public async Task<bool> ApplyContextAsync(WorkSurfaceContext? context, CancellationToken cancellationToken = default)
    {
        _taskContext = context;
        _loaded = false;
        OnPropertyChanged(nameof(IsTaskMode));
        OnPropertyChanged(nameof(TaskContext));
        OnPropertyChanged(nameof(CanCompleteTask));
        (CompleteTaskCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();

        if (context is null)
            return false;

        if (!WorkSurfaceComponentKeys.IsProjectWorkSurface(context.ComponentKey))
        {
            StatusMessage =
                $"Task #{context.TaskId} targets '{context.ComponentKey}', which is not the ProjectWork surface.";
            return false;
        }

        if (context.ProjectId <= 0)
        {
            StatusMessage = $"Task #{context.TaskId} has no project — ProjectWork requires a project.";
            return false;
        }

        AllowedResultCodes.Clear();
        foreach (var code in context.AllowedResultCodes)
            AllowedResultCodes.Add(code);
        SelectedResultCode = AllowedResultCodes.Count == 1 ? AllowedResultCodes[0] : null;

        TaskHeader = string.IsNullOrWhiteSpace(context.TaskTypeCode)
            ? $"\u05DE\u05E9\u05D9\u05DE\u05D4 #{context.TaskId}"
            : $"\u05DE\u05E9\u05D9\u05DE\u05D4 #{context.TaskId} \u2014 {context.TaskTypeCode}";

        await BindProjectAsync(context.ProjectId, cancellationToken).ConfigureAwait(true);

        _loaded = true;
        StatusMessage = $"\u05E0\u05E4\u05EA\u05D7\u05D4 \u05DE\u05E9\u05D9\u05DE\u05D4 #{context.TaskId} \u05E2\u05D1\u05D5\u05E8 \u05E4\u05E8\u05D5\u05D9\u05E7\u05D8 {context.ProjectId}.";
        OnPropertyChanged(nameof(CanCompleteTask));
        (CompleteTaskCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        return true;
    }

    private async Task BindProjectAsync(int projectId, CancellationToken cancellationToken)
    {
        ProjectSummaryDto? project = null;
        if (_projectQuery is not null)
        {
            try
            {
                project = await _projectQuery.GetProjectAsync(projectId, cancellationToken).ConfigureAwait(true);
            }
            catch
            {
                project = null;
            }
        }

        if (project is not null && _currentProject is not null)
        {
            // Bind the task's project as shell context. Does not mutate workflow/task state.
            await _currentProject.SetCurrentProjectAsync(project, cancellationToken).ConfigureAwait(true);
        }

        ActiveProjectDisplay = project is not null
            ? FormatProjectDisplay(project)
            : $"\u05E4\u05E8\u05D5\u05D9\u05E7\u05D8 {projectId}";
    }

    public async Task<bool> CompleteFromTaskAsync(
        string? completionEventCode = null,
        string? taskResultCode = null,
        CancellationToken cancellationToken = default)
    {
        if (_taskContext is not { } context)
        {
            StatusMessage = "Cannot complete: this surface was not opened from a task.";
            return false;
        }

        if (context.TaskId is not { } taskId || taskId <= 0)
        {
            StatusMessage = "Cannot complete: the work context has no task to complete.";
            return false;
        }

        if (_taskCompletion is null)
        {
            StatusMessage = "Cannot complete: ITaskCompletionService is not bound in this host.";
            return false;
        }

        if (!_loaded)
        {
            StatusMessage = "Cannot complete: the task surface was not loaded.";
            return false;
        }

        var actingUserId = context.ActingUserId ?? _currentUser?.UserId;
        if (actingUserId is not > 0)
        {
            StatusMessage = "Cannot complete: acting user is unknown.";
            return false;
        }

        if (!TryResolveResultCode(context, taskResultCode ?? SelectedResultCode, out var resolvedResultCode, out var resultMessage))
        {
            StatusMessage = resultMessage ?? "Cannot complete: result code could not be resolved.";
            return false;
        }

        if (!TryResolveEffectiveCompletionEventCode(
                context,
                completionEventCode ?? context.CompletionEventCode,
                resolvedResultCode,
                out var effectiveEventCode,
                out var eventMessage))
        {
            StatusMessage = eventMessage ?? "Cannot complete: completion event could not be resolved.";
            return false;
        }

        IsBusy = true;
        try
        {
            var result = await _taskCompletion
                .CompleteAsync(
                    new CompleteTaskCommand(
                        TaskId: taskId,
                        CompletionEventCode: effectiveEventCode,
                        TaskResultCode: resolvedResultCode,
                        CompletedTaskLinkIds: null,
                        UserId: actingUserId.Value),
                    cancellationToken)
                .ConfigureAwait(true);

            if (!result.Success)
            {
                StatusMessage =
                    $"Could not complete task #{taskId}: {result.ErrorMessage ?? "the completion was rejected."}";
                return false;
            }

            var closed = result.TaskClosed ? " Task closed." : string.Empty;
            var advanced = result.WorkflowAdvanced ? " Workflow advanced." : string.Empty;
            StatusMessage = $"Completed task #{taskId}.{closed}{advanced}";
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool TryResolveEffectiveCompletionEventCode(
        WorkSurfaceContext context,
        string? explicitCompletionEventCode,
        string? resolvedResultCode,
        [NotNullWhen(true)] out string? completionEventCode,
        out string? message)
    {
        message = null;

        if (!string.IsNullOrWhiteSpace(explicitCompletionEventCode))
        {
            completionEventCode = explicitCompletionEventCode.Trim();
            return true;
        }

        if (_completionMetadata is not null
            && context.TaskTypeCode is { } taskTypeCode
            && !string.IsNullOrWhiteSpace(taskTypeCode))
        {
            var resolved = _completionMetadata.ResolveCompletionEventCode(taskTypeCode, resolvedResultCode);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                completionEventCode = resolved;
                return true;
            }
        }

        completionEventCode = null;
        message = string.IsNullOrWhiteSpace(resolvedResultCode)
            ? "Cannot complete: no completion event could be resolved for this task. Select a result first."
            : $"Cannot complete: no single completion event maps to result '{resolvedResultCode}' for this task.";
        return false;
    }

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
                resolvedResultCode = null;
                return true;
            case 1:
                resolvedResultCode = allowed[0];
                return true;
            default:
                resolvedResultCode = null;
                message =
                    "Cannot complete: this task allows multiple results. Select one before completing.";
                return false;
        }
    }

    private static string FormatProjectDisplay(ProjectSummaryDto project)
    {
        var number = string.IsNullOrWhiteSpace(project.ProjectNumber) ? null : project.ProjectNumber.Trim();
        var name = string.IsNullOrWhiteSpace(project.ProjectName) ? null : project.ProjectName.Trim();
        if (number is not null && name is not null)
            return $"{number} \u2014 {name}";
        return name ?? number ?? $"\u05E4\u05E8\u05D5\u05D9\u05E7\u05D8 {project.ProjectId}";
    }
}
