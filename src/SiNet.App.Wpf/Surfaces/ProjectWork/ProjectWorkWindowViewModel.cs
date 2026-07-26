using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shared.Projects;
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
public sealed class ProjectWorkWindowViewModel : ObservableObject, IDisposable
{
    private readonly ITaskCompletionService? _taskCompletion;
    private readonly ITaskCompletionMetadataResolver? _completionMetadata;
    private readonly ICurrentUserContext? _currentUser;
    private readonly ICurrentProjectContext? _currentProject;
    private readonly IProjectQueryService? _projectQuery;
    private readonly ProjectWorkTreeViewModel? _tree;
    private readonly ProjectSelectorViewModel? _selector;

    private int _lastLoadedProjectId;
    private bool _disposed;

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
        RebuildResultOptions();
    }

    public ProjectWorkWindowViewModel(
        ITaskCompletionService? taskCompletion,
        ITaskCompletionMetadataResolver? completionMetadata = null,
        ICurrentUserContext? currentUser = null,
        ICurrentProjectContext? currentProject = null,
        IProjectQueryService? projectQuery = null,
        ProjectWorkTreeViewModel? tree = null,
        ProjectSelectorViewModel? selector = null)
    {
        _taskCompletion = taskCompletion;
        _completionMetadata = completionMetadata;
        _currentUser = currentUser;
        _currentProject = currentProject;
        _projectQuery = projectQuery;
        _tree = tree;
        _selector = selector;

        AllowedResultCodes = new ObservableCollection<string>();
        AllowedResultOptions = new ObservableCollection<TaskResultOption>();

        CompleteTaskCommand = new AsyncRelayCommand(
            async () => { _ = await CompleteFromTaskAsync().ConfigureAwait(true); },
            () => CanCompleteTask);

        // Browse mode: react to the shared Current Project (e.g. driven by the embedded selector) and
        // (re)load the file tree. Task mode also loads explicitly in ApplyContextAsync; the load is
        // de-duplicated by project id so it never runs twice for the same project.
        if (_currentProject is not null)
            _currentProject.CurrentProjectChanged += OnCurrentProjectChanged;
    }

    /// <summary>The unified file/folder tree (null in the design-time / task-only host).</summary>
    public ProjectWorkTreeViewModel? Tree => _tree;

    /// <summary>Embedded shared project selector for browse mode (null in design-time host).</summary>
    public ProjectSelectorViewModel? Selector => _selector;

    /// <summary>True when the file workspace (tree) is available in this host.</summary>
    public bool HasFileWorkspace => _tree is not null;

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

    /// <summary>Hebrew display rows for the completion ComboBox (Code stays English for completion).</summary>
    public ObservableCollection<TaskResultOption> AllowedResultOptions { get; }

    private TaskResultOption? _selectedResultOption;

    public TaskResultOption? SelectedResultOption
    {
        get => _selectedResultOption;
        set
        {
            if (SetField(ref _selectedResultOption, value))
                SelectedResultCode = value?.Code;
        }
    }

    public string? SelectedResultCode
    {
        get => _selectedResultCode;
        set
        {
            if (SetField(ref _selectedResultCode, value))
            {
                if (_selectedResultOption?.Code != value)
                {
                    _selectedResultOption = AllowedResultOptions.FirstOrDefault(o => o.Code == value);
                    OnPropertyChanged(nameof(SelectedResultOption));
                }

                (CompleteTaskCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
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

    /// <summary>
    /// Browse-mode entry (menu / ShowProjectWork): no task strip. Loads the tree from the current
    /// project context when available; otherwise leaves the selector ready for the user to pick one.
    /// </summary>
    public async Task OpenBrowseModeAsync(CancellationToken cancellationToken = default)
    {
        _taskContext = null;
        _loaded = true;
        AllowedResultCodes.Clear();
        SelectedResultCode = null;
        AllowedResultOptions.Clear();
        SelectedResultOption = null;
        TaskHeader = string.Empty;
        StatusMessage = "\u05DE\u05D5\u05DB\u05DF";
        OnPropertyChanged(nameof(IsTaskMode));
        OnPropertyChanged(nameof(TaskContext));
        OnPropertyChanged(nameof(CanCompleteTask));
        (CompleteTaskCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();

        var projectId = _currentProject?.CurrentProject?.ProjectId ?? 0;
        if (projectId > 0)
        {
            ActiveProjectDisplay = _currentProject?.CurrentProject is { } p
                ? FormatProjectDisplay(p)
                : $"\u05E4\u05E8\u05D5\u05D9\u05E7\u05D8 {projectId}";
            await LoadTreeAsync(projectId, cancellationToken).ConfigureAwait(true);
        }
        else
        {
            ActiveProjectDisplay =
                "\u05D1\u05D7\u05E8/\u05D9 \u05E4\u05E8\u05D5\u05D9\u05E7\u05D8 \u05DC\u05E6\u05E4\u05D9\u05D9\u05D4 \u05D1\u05E2\u05E5 \u05D4\u05E7\u05D1\u05E6\u05D9\u05DD";
        }
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
        RebuildResultOptions();
        SelectedResultCode = AllowedResultCodes.Count == 1 ? AllowedResultCodes[0] : null;

        // #region agent log
        SiNet.Application.Diagnostics.WorkflowDebugTrace.Step(
            "ProjectWork.Results",
            $"task=#{context.TaskId} comboShowsRawCodes=[{string.Join(",", AllowedResultCodes)}] display=[{string.Join(",", AllowedResultOptions.Select(o => o.DisplayName))}]");
        SiNet.Application.Diagnostics.WorkflowDebugTrace.Step(
            "ProjectWork.AccPopOut",
            "ProjectWork AccDock/PopOut control available on surface");
        // #endregion

        TaskHeader = string.IsNullOrWhiteSpace(context.TaskTypeCode)
            ? $"\u05DE\u05E9\u05D9\u05DE\u05D4 #{context.TaskId}"
            : $"\u05DE\u05E9\u05D9\u05DE\u05D4 #{context.TaskId} \u2014 {context.TaskTypeCode}";

        await BindProjectAsync(context.ProjectId, cancellationToken).ConfigureAwait(true);
        await LoadTreeAsync(context.ProjectId, cancellationToken, forceReload: true).ConfigureAwait(true);

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

    private async Task LoadTreeAsync(int projectId, CancellationToken cancellationToken, bool forceReload = false)
    {
        if (_tree is null || projectId <= 0)
        {
            // #region agent log
            SiNet.Application.Diagnostics.WorkflowDebugTrace.Step(
                "ProjectWork.LoadTree",
                $"SKIP tree=null={_tree is null} projectId={projectId} lastLoaded={_lastLoadedProjectId} force={forceReload}");
            // #endregion
            return;
        }

        if (!forceReload && projectId == _lastLoadedProjectId)
        {
            // #region agent log
            SiNet.Application.Diagnostics.WorkflowDebugTrace.Step(
                "ProjectWork.LoadTree",
                $"SKIP tree=null=False projectId={projectId} lastLoaded={_lastLoadedProjectId} force={forceReload}");
            // #endregion
            return;
        }

        _lastLoadedProjectId = projectId;
        // #region agent log
        SiNet.Application.Diagnostics.WorkflowDebugTrace.Step(
            "ProjectWork.LoadTree",
            $"LOAD projectId={projectId} force={forceReload}");
        // #endregion
        try
        {
            await _tree.LoadProjectAsync(projectId, cancellationToken).ConfigureAwait(true);
        }
        catch
        {
            // A failed tree load must not break the task shell; the status bar reflects scan state.
            _lastLoadedProjectId = 0;
        }
    }

    private void RebuildResultOptions()
    {
        AllowedResultOptions.Clear();
        foreach (var code in AllowedResultCodes)
            AllowedResultOptions.Add(new TaskResultOption(code, TaskResultDisplayNames.For(code)));
    }

    private void OnCurrentProjectChanged(object? sender, ProjectChangedEventArgs e)
    {
        var projectId = e.Project?.ProjectId ?? 0;
        if (_tree is null || projectId <= 0)
            return;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        void Load() => _ = LoadTreeAsync(projectId, CancellationToken.None);
        if (dispatcher is null || dispatcher.CheckAccess())
            Load();
        else
            dispatcher.BeginInvoke(Load);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_currentProject is not null)
            _currentProject.CurrentProjectChanged -= OnCurrentProjectChanged;
        _tree?.Dispose();
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

            var closed = result.TaskClosed ? " המשימה נסגרה." : string.Empty;
            if (string.Equals(resolvedResultCode, "MaterialMissing", StringComparison.Ordinal))
            {
                StatusMessage =
                    $"נרשם חסר חומר.{closed} נשארים בשלב בדיקת חומר (לולאה מכוונת) ונוצרת משימת בדיקה חדשה — רשימת המשימות אמורה להתעדכן.";
            }
            else if (result.WorkflowAdvanced)
            {
                StatusMessage = $"המשימה #{taskId} הושלמה.{closed} התהליך התקדם.";
            }
            else
            {
                StatusMessage = result.TaskClosed
                    ? $"המשימה #{taskId} הושלמה.{closed} התהליך לא התקדם."
                    : $"התוצאה נרשמה למשימה #{taskId}, אך המשימה לא נסגרה — התהליך לא התקדם.";
            }
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
