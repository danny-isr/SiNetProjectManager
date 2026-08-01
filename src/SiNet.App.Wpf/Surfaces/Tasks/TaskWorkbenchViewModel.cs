using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.App.Wpf.Shell;
using SiNet.App.Wpf.WorkSurfaces;
using SiNet.Application.Diagnostics;
using SiNet.Application.Identity;
using SiNet.Application.Projects;
using SiNet.Application.Tasks;
using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.Surfaces.Tasks;

/// <summary>
/// Task Workbench: three personal work-queue buckets, scope filters, diagnostics, delete/queue ops.
/// Task creation opens <see cref="TaskCreateDialogWindow"/> — no inline create form, no global project context.
/// </summary>
public class TaskWorkbenchViewModel : ObservableObject, IDisposable
{
    private readonly ITaskQueryService _taskQuery;
    private readonly ITaskNavigationService _taskNavigation;
    private readonly ITaskWorkbenchService? _workbench;
    private readonly ITaskQueueService? _taskQueue;
    private readonly ICurrentUserContext? _currentUser;
    private readonly IAuthorizationQueryService? _authorization;
    private readonly IUserLookupService? _userLookup;
    private readonly ITaskCreateDialogFactory? _taskCreateDialogFactory;
    private readonly IWorkSurfaceLauncher? _workSurfaceLauncher;
    private readonly ITaskListChangeNotifier? _taskListChangeNotifier;
    private readonly InMemoryCurrentProjectContext _localProjectFilterContext = new();
    private readonly DispatcherTimer? _crossClientRefreshTimer;

    /// <summary>Set when a notify arrives while <see cref="IsBusy"/>; drained after LoadAsync.</summary>
    private bool _reloadPending;

    private TaskSummaryDto? _selectedTask;
    private string _statusMessage = "טוען משימות...";
    private string _diagnosticsText = string.Empty;
    private string _resolvePreview = string.Empty;
    private bool _isBusy;
    private TaskWorkbenchScope _selectedScope = TaskWorkbenchScope.MyTasks;
    private int? _selectedUserId;
    private bool _canSelectTaskScope;
    private bool _scopeOptionsInitialized;
    private bool _suppressScopeReload;
    private bool _disposed;

    /// <summary>Poll interval so lists pick up tasks created/closed by other users/clients.</summary>
    private static readonly TimeSpan CrossClientRefreshInterval = TimeSpan.FromSeconds(30);

    public TaskWorkbenchViewModel()
        : this(
            new DesignTaskQueryService(),
            new DesignTaskNavigationService(),
            null,
            null,
            null,
            null,
            null,
            new FakeProjectQueryService(),
            new FakeProjectFilterOptionsService(),
            null,
            null)
    {
    }

    public TaskWorkbenchViewModel(
        ITaskQueryService taskQuery,
        ITaskNavigationService taskNavigation,
        ITaskWorkbenchService? workbench = null,
        ICurrentUserContext? currentUser = null,
        IAuthorizationQueryService? authorization = null,
        IUserLookupService? userLookup = null,
        ITaskQueueService? taskQueue = null,
        IProjectQueryService? projectQuery = null,
        IProjectFilterOptionsService? projectFilterOptions = null,
        ITaskCreateDialogFactory? taskCreateDialogFactory = null,
        IWorkSurfaceLauncher? workSurfaceLauncher = null,
        ITaskListChangeNotifier? taskListChangeNotifier = null)
    {
        _taskQuery = taskQuery ?? throw new ArgumentNullException(nameof(taskQuery));
        _taskNavigation = taskNavigation ?? throw new ArgumentNullException(nameof(taskNavigation));
        _workbench = workbench;
        _taskQueue = taskQueue;
        _currentUser = currentUser;
        _authorization = authorization;
        _userLookup = userLookup;
        _taskCreateDialogFactory = taskCreateDialogFactory;
        _workSurfaceLauncher = workSurfaceLauncher;
        _taskListChangeNotifier = taskListChangeNotifier;

        QuickTasks = [];
        MediumTasks = [];
        LongTasks = [];
        AvailableScopes = [];
        AvailableUsers = [];

        if (projectQuery is not null && projectFilterOptions is not null)
        {
            LocalProjectFilterSelector = new ProjectSelectorViewModel(
                projectQuery, projectFilterOptions, _localProjectFilterContext);
            HasLocalProjectFilter = true;
            _localProjectFilterContext.CurrentProjectChanged += OnLocalProjectFilterChanged;
        }

        RefreshCommand = new AsyncRelayCommand(() => LoadAsync(), () => !IsBusy);
        OpenTaskCommand = new AsyncRelayCommand(() => OpenSelectedTaskAsync(), () => !IsBusy && SelectedTask is not null && _workSurfaceLauncher is not null);
        ResolveCommand = new AsyncRelayCommand(() => ResolveSelectedAsync(), () => !IsBusy && SelectedTask is not null);
        AddTaskCommand = new AsyncRelayCommand(AddTaskAsync, () => !IsBusy && _workbench is not null && _taskCreateDialogFactory is not null);
        DeleteTaskCommand = new AsyncRelayCommand(DeleteSelectedAsync, () => !IsBusy && SelectedTask is not null && _workbench is not null);
        DeactivateTaskCommand = new AsyncRelayCommand(DeactivateSelectedAsync, () => !IsBusy && SelectedTask is not null && _workbench is not null);
        ReactivateTaskCommand = new AsyncRelayCommand(ReactivateSelectedAsync, () => !IsBusy && SelectedTask is not null && _workbench is not null);
        RepairQueueCommand = new AsyncRelayCommand(RepairQueueAsync, () => !IsBusy && CanManageQueue);
        MoveUpCommand = new AsyncRelayCommand(MoveSelectedUpAsync, CanMoveSelectedUp);
        MoveDownCommand = new AsyncRelayCommand(MoveSelectedDownAsync, CanMoveSelectedDown);
        ClearSelectedProjectCommand = new RelayCommand(_ => ClearSelectedProject(), _ => CanClearSelectedProject && !IsBusy);

        if (_taskListChangeNotifier is not null)
            _taskListChangeNotifier.TaskListChanged += OnExternalTaskListChanged;

        // Cross-client safety net: another user may create/close tasks without a local notify.
        if (System.Windows.Application.Current?.Dispatcher is not null)
        {
            _crossClientRefreshTimer = new DispatcherTimer(
                CrossClientRefreshInterval,
                DispatcherPriority.Background,
                OnCrossClientRefreshTick,
                System.Windows.Application.Current.Dispatcher);
            _crossClientRefreshTimer.Start();
        }
    }

    private void OnExternalTaskListChanged()
    {
        // #region agent log
        WorkflowDebugTrace.Step(
            "Tasks.Workbench",
            "OnExternalTaskListChanged → LoadAsync (local notify)");
        // #endregion
        void Reload()
        {
            if (_disposed || !_scopeOptionsInitialized)
                return;
            if (IsBusy)
            {
                _reloadPending = true;
                WorkflowDebugTrace.Step("Tasks.Workbench", "notify while busy → reloadPending");
                return;
            }

            _ = LoadAsync();
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;
        if (dispatcher.CheckAccess())
            Reload();
        else
            dispatcher.BeginInvoke(Reload);
    }

    private void OnCrossClientRefreshTick(object? sender, EventArgs e)
    {
        if (_disposed || IsBusy || !_scopeOptionsInitialized)
            return;
        // #region agent log
        WorkflowDebugTrace.Step("Tasks.Workbench", "cross-client poll → LoadAsync");
        // #endregion
        _ = LoadAsync();
    }

    /// <summary>Local-only project selector for optional list filtering (does not touch app project context).</summary>
    public ProjectSelectorViewModel? LocalProjectFilterSelector { get; }

    public bool HasLocalProjectFilter { get; }

    /// <summary>True when a project is selected in the local filter selector (not the app-wide context).</summary>
    public bool FilterTasksByProjectEnabled => GetActiveProjectFilterId() is int;

    public int? SelectedProjectId => GetActiveProjectFilterId();

    public bool CanClearSelectedProject => FilterTasksByProjectEnabled && LocalProjectFilterSelector is not null;

    public string ProjectFilterDisplayText =>
        _localProjectFilterContext.CurrentProject is { } project
            ? $"סינון לפי פרויקט: כן — {project.ProjectNumber} — {project.ProjectName} (Id={project.ProjectId})"
            : "סינון לפי פרויקט: לא — כל הפרויקטים";

    internal const string EmptyProjectFilterStatusMessage =
        "לא נמצאו משימות עבור הסינון הנוכחי. נסה לבטל סינון לפי פרויקט או לבחור פרויקט אחר.";

    public virtual string Title => "משימות — Task Workbench";

    public string QueryServiceName => _taskQuery.GetType().Name;

    public string QueueServiceName => _taskQueue?.GetType().Name ?? "(none)";

    public bool CanManageQueue => _taskQueue is not null && _currentUser?.UserId is int;

    public string LoadMode { get; private set; } = "None";

    public string? CurrentUserIdDisplay { get; private set; }

    public ObservableCollection<TaskSummaryDto> QuickTasks { get; }
    public ObservableCollection<TaskSummaryDto> MediumTasks { get; }
    public ObservableCollection<TaskSummaryDto> LongTasks { get; }

    public ObservableCollection<TaskWorkbenchScopeOption> AvailableScopes { get; }
    public ObservableCollection<UserLookupDto> AvailableUsers { get; }

    public bool CanSelectTaskScope
    {
        get => _canSelectTaskScope;
        private set => SetField(ref _canSelectTaskScope, value);
    }

    public TaskWorkbenchScope SelectedScope
    {
        get => _selectedScope;
        set
        {
            if (_scopeOptionsInitialized && !CanSelectTaskScope && value != TaskWorkbenchScope.MyTasks)
            {
                StatusMessage = "אין הרשאה להצגת משימות של משתמשים אחרים.";
                NotifyScopeDerivedProperties();
                return;
            }

            if (!SetField(ref _selectedScope, value))
                return;

            NotifyScopeDerivedProperties();
            if (!_suppressScopeReload && _scopeOptionsInitialized)
                _ = LoadAsync();
        }
    }

    public int? SelectedUserId
    {
        get => _selectedUserId;
        set
        {
            if (!SetField(ref _selectedUserId, value))
                return;

            OnPropertyChanged(nameof(ScopeDisplayText));
            if (!_suppressScopeReload && _scopeOptionsInitialized && SelectedScope == TaskWorkbenchScope.SpecificUser)
                _ = LoadAsync();
        }
    }

    public bool IsSpecificUserScope => SelectedScope == TaskWorkbenchScope.SpecificUser;

    public string ScopeDisplayText =>
        CanSelectTaskScope
            ? TaskWorkbenchScopeLabels.GetDisplayName(SelectedScope)
            : $"מציג: {TaskWorkbenchScopeLabels.MyTasks}";

    public TaskSummaryDto? SelectedTask
    {
        get => _selectedTask;
        set
        {
            if (SetField(ref _selectedTask, value))
            {
                ResolvePreview = string.Empty;
                OnPropertyChanged(nameof(QueueMoveStatusHint));
                RaiseCommandStates();
            }
        }
    }

    /// <summary>Tooltip / status hint when queue move commands are disabled.</summary>
    public string QueueMoveStatusHint
    {
        get
        {
            if (_taskQueue is null || _currentUser?.UserId is not int)
                return string.Empty;

            if (SelectedTask is null)
                return "בחר משימה כדי לשנות את מיקומה בתור.";

            if (!CanMutateSelectedTaskQueue())
                return "אין הרשאה לשנות את תור המשימה הנבחרת.";

            if (SelectedTask.WorkPriority is not int priority || priority <= 0)
                return "המשימה אינה נמצאת בתור פעיל.";

            return string.Empty;
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string DiagnosticsText
    {
        get => _diagnosticsText;
        private set => SetField(ref _diagnosticsText, value);
    }

    public string ResolvePreview
    {
        get => _resolvePreview;
        private set => SetField(ref _resolvePreview, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
                RaiseCommandStates();
        }
    }

    public bool CanWrite => _workbench is not null;

    public ICommand RefreshCommand { get; }
    public ICommand OpenTaskCommand { get; }
    public ICommand ResolveCommand { get; }
    public ICommand AddTaskCommand { get; }
    public ICommand DeleteTaskCommand { get; }
    public ICommand DeactivateTaskCommand { get; }
    public ICommand ReactivateTaskCommand { get; }
    public ICommand RepairQueueCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand ClearSelectedProjectCommand { get; }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (LocalProjectFilterSelector is not null)
            await LocalProjectFilterSelector.InitializeAsync(ct).ConfigureAwait(true);

        await InitializeScopeOptionsAsync(ct).ConfigureAwait(true);
        await LoadAsync(ct).ConfigureAwait(true);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_crossClientRefreshTimer is not null)
        {
            _crossClientRefreshTimer.Stop();
            _crossClientRefreshTimer.Tick -= OnCrossClientRefreshTick;
        }

        if (_taskListChangeNotifier is not null)
            _taskListChangeNotifier.TaskListChanged -= OnExternalTaskListChanged;

        _localProjectFilterContext.CurrentProjectChanged -= OnLocalProjectFilterChanged;
        LocalProjectFilterSelector?.Dispose();
    }

    private void OnLocalProjectFilterChanged(object? sender, ProjectChangedEventArgs e)
    {
        OnPropertyChanged(nameof(FilterTasksByProjectEnabled));
        OnPropertyChanged(nameof(SelectedProjectId));
        OnPropertyChanged(nameof(CanClearSelectedProject));
        OnPropertyChanged(nameof(ProjectFilterDisplayText));
        (ClearSelectedProjectCommand as RelayCommand)?.RaiseCanExecuteChanged();
        if (_scopeOptionsInitialized)
            _ = LoadAsync();
    }

    private void ClearSelectedProject()
    {
        LocalProjectFilterSelector?.ClearSelection();
    }

    private int? GetActiveProjectFilterId() =>
        _localProjectFilterContext.CurrentProject?.ProjectId is int id ? id : null;

    private async Task InitializeScopeOptionsAsync(CancellationToken ct)
    {
        CanSelectTaskScope = _authorization is not null
            && await _authorization.CanCurrentUserAccessFeatureAsync(
                AppFeatureCodes.TaskWorkbenchViewOtherUsersTasks, ct).ConfigureAwait(true);

        _suppressScopeReload = true;
        try
        {
            AvailableScopes.Clear();
            AvailableScopes.Add(new TaskWorkbenchScopeOption(
                TaskWorkbenchScope.MyTasks, TaskWorkbenchScopeLabels.MyTasks));

            if (CanSelectTaskScope)
            {
                AvailableScopes.Add(new TaskWorkbenchScopeOption(
                    TaskWorkbenchScope.SpecificUser, TaskWorkbenchScopeLabels.SpecificUser));
                AvailableScopes.Add(new TaskWorkbenchScopeOption(
                    TaskWorkbenchScope.AllUsers, TaskWorkbenchScopeLabels.AllUsers));

                if (_userLookup is not null)
                {
                    AvailableUsers.Clear();
                    var users = await _userLookup.GetActiveUsersAsync(ct).ConfigureAwait(true);
                    foreach (var user in users)
                        AvailableUsers.Add(user);
                }
            }

            SelectedScope = TaskWorkbenchScope.MyTasks;
            SelectedUserId = null;
        }
        finally
        {
            _suppressScopeReload = false;
            _scopeOptionsInitialized = true;
            NotifyScopeDerivedProperties();
        }
    }

    private void NotifyScopeDerivedProperties()
    {
        OnPropertyChanged(nameof(IsSpecificUserScope));
        OnPropertyChanged(nameof(ScopeDisplayText));
    }

    internal async Task LoadAsync(CancellationToken ct = default)
    {
        if (!_scopeOptionsInitialized)
            await InitializeScopeOptionsAsync(ct).ConfigureAwait(true);

        // Serialize reloads: a notify (or second caller) while busy coalesces into one follow-up load.
        if (IsBusy)
        {
            _reloadPending = true;
            return;
        }

        IsBusy = true;
        BucketCounts counts = default;
        try
        {
            QuickTasks.Clear();
            MediumTasks.Clear();
            LongTasks.Clear();
            SelectedTask = null;

            var userId = _currentUser?.UserId;

            CurrentUserIdDisplay = userId?.ToString() ?? "(none)";
            OnPropertyChanged(nameof(CurrentUserIdDisplay));

            if (userId is int uid)
            {
                if (!CanSelectTaskScope && SelectedScope != TaskWorkbenchScope.MyTasks)
                {
                    _suppressScopeReload = true;
                    try
                    {
                        SelectedScope = TaskWorkbenchScope.MyTasks;
                        SelectedUserId = null;
                    }
                    finally
                    {
                        _suppressScopeReload = false;
                    }

                    StatusMessage = "אין הרשאה להצגת משימות של משתמשים אחרים.";
                    LoadMode = TaskWorkbenchScope.MyTasks.ToString();
                    UpdateDiagnostics(counts);
                    return;
                }

                switch (SelectedScope)
                {
                    case TaskWorkbenchScope.MyTasks:
                        LoadMode = TaskWorkbenchScope.MyTasks.ToString();
                        counts = await LoadUserBucketsAsync(uid, ct).ConfigureAwait(true);
                        StatusMessage = await BuildUserStatusMessageAsync(uid, counts, ct).ConfigureAwait(true);
                        break;

                    case TaskWorkbenchScope.SpecificUser:
                        if (SelectedUserId is not int targetUserId)
                        {
                            LoadMode = TaskWorkbenchScope.SpecificUser.ToString();
                            StatusMessage = "בחר משתמש כדי להציג את המשימות שלו.";
                            break;
                        }

                        LoadMode = TaskWorkbenchScope.SpecificUser.ToString();
                        counts = await LoadUserBucketsAsync(targetUserId, ct).ConfigureAwait(true);
                        StatusMessage = await BuildUserStatusMessageAsync(targetUserId, counts, ct).ConfigureAwait(true);
                        break;

                    case TaskWorkbenchScope.AllUsers:
                        LoadMode = TaskWorkbenchScope.AllUsers.ToString();
                        counts = await LoadAllUsersBucketsAsync(ct).ConfigureAwait(true);
                        StatusMessage = counts.Total == 0
                            ? FormatEmptyScopeMessage("כל המשתמשים", counts)
                            : $"מציג משימות של כל המשתמשים. נטענו {counts.Total}: קצר={counts.Quick}, בינוני={counts.Medium}, ארוך={counts.Long}";
                        break;

                    default:
                        LoadMode = TaskWorkbenchScope.MyTasks.ToString();
                        counts = await LoadUserBucketsAsync(uid, ct).ConfigureAwait(true);
                        StatusMessage = await BuildUserStatusMessageAsync(uid, counts, ct).ConfigureAwait(true);
                        break;
                }
            }
            else
            {
                LoadMode = "None";
                StatusMessage = "התחבר כמשתמש כדי לראות משימות.";
            }

            OnPropertyChanged(nameof(LoadMode));
            UpdateDiagnostics(counts);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            StatusMessage = $"שגיאה בטעינת משימות: {ex.Message}";
            DiagnosticsText = BuildDiagnosticsText(counts, ex.Message);
        }
        finally
        {
            IsBusy = false;
            if (_reloadPending && !_disposed)
            {
                _reloadPending = false;
                WorkflowDebugTrace.Step("Tasks.Workbench", "draining reloadPending → LoadAsync");
                _ = LoadAsync();
            }
        }
    }

    private async Task AddTaskAsync()
    {
        if (_taskCreateDialogFactory is null)
            return;

        var owner = System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        var result = _taskCreateDialogFactory.ShowDialog(owner);
        if (!result.Succeeded)
            return;

        await LoadAsync().ConfigureAwait(true);
        if (result.CreatedTaskId is int taskId)
            RestoreSelection(taskId);
    }

    private async Task<BucketCounts> LoadUserBucketsAsync(int userId, CancellationToken ct)
    {
        var quick = await _taskQuery.GetOpenTasksForUserByBucketAsync(userId, WorkQueueBucketCodes.Quick, ct).ConfigureAwait(true);
        var medium = await _taskQuery.GetOpenTasksForUserByBucketAsync(userId, WorkQueueBucketCodes.Medium, ct).ConfigureAwait(true);
        var longBucket = await _taskQuery.GetOpenTasksForUserByBucketAsync(userId, WorkQueueBucketCodes.Long, ct).ConfigureAwait(true);
        ReplaceAll(quick, medium, longBucket, GetActiveProjectFilterId());
        return new BucketCounts(QuickTasks.Count, MediumTasks.Count, LongTasks.Count);
    }

    private async Task<BucketCounts> LoadAllUsersBucketsAsync(CancellationToken ct)
    {
        var quick = await _taskQuery.GetOpenTasksForAllUsersByBucketAsync(WorkQueueBucketCodes.Quick, ct).ConfigureAwait(true);
        var medium = await _taskQuery.GetOpenTasksForAllUsersByBucketAsync(WorkQueueBucketCodes.Medium, ct).ConfigureAwait(true);
        var longBucket = await _taskQuery.GetOpenTasksForAllUsersByBucketAsync(WorkQueueBucketCodes.Long, ct).ConfigureAwait(true);
        ReplaceAll(quick, medium, longBucket, GetActiveProjectFilterId());
        return new BucketCounts(QuickTasks.Count, MediumTasks.Count, LongTasks.Count);
    }

    private async Task<string> BuildUserStatusMessageAsync(int userId, BucketCounts counts, CancellationToken ct)
    {
        if (counts.Total == 0)
        {
            if (FilterTasksByProjectEnabled)
                return EmptyProjectFilterStatusMessage;

            var hint = string.Empty;
            if (_workbench is not null)
            {
                var demoUsers = await _workbench.GetDemoTaskAssigneeUserIdsAsync(ct).ConfigureAwait(true);
                if (demoUsers.Count > 0 && !demoUsers.Contains(userId))
                    hint = $" קיימות משימות דemo למשתמשים: {string.Join(", ", demoUsers)}.";
            }

            return $"לא נמצאו משימות עבור UserId={userId}. ייתכן שמשימות הדemo נוצרו למשתמש אחר.{hint}";
        }

        return $"מציג משימות עבור משתמש {userId}. נטענו {counts.Total}: קצר={counts.Quick}, בינוני={counts.Medium}, ארוך={counts.Long}";
    }

    private string FormatEmptyScopeMessage(string scopeLabel, BucketCounts counts)
    {
        if (counts.Total == 0 && FilterTasksByProjectEnabled)
            return EmptyProjectFilterStatusMessage;

        return scopeLabel switch
        {
            "כל המשתמשים" => "לא נמצאו משימות פתוחות לכל המשתמשים.",
            _ => $"לא נמצאו משימות עבור {scopeLabel}.",
        };
    }

    internal static string FormatProjectStatusMessage(int projectId, BucketCounts counts) =>
        counts.Total == 0
            ? $"לא נמצאו משימות לפרויקט {projectId}."
            : $"מציג משימות לפרויקט {projectId}. נטענו {counts.Total}: קצר={counts.Quick}, בינוני={counts.Medium}, ארוך={counts.Long}";

    internal static string FormatUserStatusMessage(int userId, BucketCounts counts) =>
        counts.Total == 0
            ? $"לא נמצאו משימות למשתמש {userId}. ייתכן שמשימות הדemo נוצרו למשתמש אחר."
            : $"נטענו {counts.Total} משימות למשתמש {userId}: קצר={counts.Quick}, בינוני={counts.Medium}, ארוך={counts.Long}";

    internal readonly record struct BucketCounts(int Quick, int Medium, int Long)
    {
        public int Total => Quick + Medium + Long;
    }

    private void UpdateDiagnostics(BucketCounts counts) =>
        DiagnosticsText = BuildDiagnosticsText(counts, null);

    internal string BuildDiagnosticsText(BucketCounts counts, string? error)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Mode: {LoadMode}");
        sb.AppendLine($"CurrentUserId: {CurrentUserIdDisplay}");
        sb.AppendLine($"SelectedUserId: {SelectedUserId?.ToString() ?? "(none)"}");
        sb.AppendLine($"Project filter: {(FilterTasksByProjectEnabled ? "on" : "off")}");
        if (FilterTasksByProjectEnabled && GetActiveProjectFilterId() is int filterId)
            sb.AppendLine($"FilteredProjectId: {filterId}");
        sb.AppendLine($"Counts: Quick={counts.Quick}, Medium={counts.Medium}, Long={counts.Long}");
        if (!string.IsNullOrEmpty(error))
            sb.AppendLine($"Error: {error}");
        return sb.ToString().TrimEnd();
    }

    private async Task DeleteSelectedAsync()
    {
        if (_workbench is null || SelectedTask is null || _currentUser?.UserId is not int actorId)
            return;

        if (MessageBox.Show("למחוק את המשימה שנבחרה?", "מחיקת משימה", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var taskId = SelectedTask.TaskId;
        IsBusy = true;
        try
        {
            var result = await _workbench.DeleteTaskAsync(taskId, actorId).ConfigureAwait(true);
            if (!result.Succeeded)
            {
                // A workflow-linked task cannot be hard-deleted (it would orphan the workflow). Offer to
                // deactivate it instead — this pauses the workflow and preserves the task for reactivation.
                if (result.BlockedByWorkflow)
                {
                    var offer = MessageBox.Show(
                        result.Message + "\n\nלהשבית את המשימה במקום זאת? ה-Workflow יושהה וניתן יהיה להפעיל אותה מחדש בהמשך.",
                        "מחיקת משימה",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (offer == MessageBoxResult.Yes)
                        await RunDeactivateAsync(taskId, actorId).ConfigureAwait(true);

                    return;
                }

                MessageBox.Show(result.Message, "מחיקת משימה", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            MessageBox.Show(ex.Message, "מחיקת משימה", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeactivateSelectedAsync()
    {
        if (_workbench is null || SelectedTask is null || _currentUser?.UserId is not int actorId)
            return;

        if (MessageBox.Show(
                "להשבית את המשימה שנבחרה? אם היא מפעילה Workflow, ה-Workflow יושהה עד להפעלה מחדש.",
                "השבתת משימה",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var taskId = SelectedTask.TaskId;
        IsBusy = true;
        try
        {
            await RunDeactivateAsync(taskId, actorId).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            MessageBox.Show(ex.Message, "השבתת משימה", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunDeactivateAsync(int taskId, int actorId)
    {
        var result = await _workbench!.DeactivateTaskAsync(taskId, actorId).ConfigureAwait(true);
        if (!result.Succeeded)
        {
            MessageBox.Show(result.Message, "השבתת משימה", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StatusMessage = $"המשימה #{taskId} הושבתה וה-Workflow (אם קיים) הושהה.";
        await LoadAsync().ConfigureAwait(true);
    }

    private async Task ReactivateSelectedAsync()
    {
        if (_workbench is null || SelectedTask is null || _currentUser?.UserId is not int actorId)
            return;

        var taskId = SelectedTask.TaskId;
        IsBusy = true;
        try
        {
            var result = await _workbench.ReactivateTaskAsync(taskId, actorId).ConfigureAwait(true);
            if (!result.Succeeded)
            {
                MessageBox.Show(result.Message, "הפעלת משימה מחדש", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StatusMessage = $"המשימה #{taskId} הופעלה מחדש וה-Workflow (אם היה מושהה) חודש.";
            await LoadAsync().ConfigureAwait(true);
            RestoreSelection(taskId);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            MessageBox.Show(ex.Message, "הפעלת משימה מחדש", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RepairQueueAsync()
    {
        if (_taskQueue is null || _currentUser?.UserId is not int actorId)
            return;

        IsBusy = true;
        try
        {
            TaskQueueRepairResult result = SelectedScope switch
            {
                TaskWorkbenchScope.SpecificUser when SelectedUserId is int uid =>
                    await RepairUserBucketsAsync(uid).ConfigureAwait(true),
                TaskWorkbenchScope.AllUsers when CanSelectTaskScope =>
                    await _taskQueue.RepairAllQueuesAsync().ConfigureAwait(true),
                _ => await RepairUserBucketsAsync(actorId).ConfigureAwait(true),
            };

            StatusMessage =
                $"תיקון תור: {result.BucketsProcessed} תורים, {result.TasksAssignedPriority} null, " +
                $"{result.DuplicatePrioritiesFixed} כפילויות, {result.GapsClosed} חורים.";
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            StatusMessage = $"שגיאה בתיקון תור: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<TaskQueueRepairResult> RepairUserBucketsAsync(int userId)
    {
        if (_taskQueue is null)
            return TaskQueueRepairResult.Empty;

        var aggregate = TaskQueueRepairResult.Empty;
        foreach (var bucket in new[] { WorkQueueBucketCodes.Quick, WorkQueueBucketCodes.Medium, WorkQueueBucketCodes.Long })
        {
            var result = await _taskQueue.RepairQueueAsync(userId, bucket).ConfigureAwait(true);
            aggregate = aggregate.Merge(result);
        }

        return aggregate with { UsersProcessed = 1 };
    }

    private bool CanMoveSelectedUp() =>
        !IsBusy && TryGetSelectedQueuePosition(out var position, out _) && position > 1;

    private bool CanMoveSelectedDown() =>
        !IsBusy && TryGetSelectedQueuePosition(out var position, out var queueSize) && position < queueSize;

    private bool TryGetSelectedQueuePosition(out int position, out int queueSize)
    {
        position = 0;
        queueSize = 0;

        if (_taskQueue is null || _currentUser?.UserId is not int)
            return false;

        if (SelectedTask?.WorkPriority is not int priority || priority <= 0)
            return false;

        if (!CanMutateSelectedTaskQueue())
            return false;

        var queueTasks = GetActiveQueueTasksForSelected();
        queueSize = queueTasks.Count;
        if (queueSize == 0)
            return false;

        position = priority;
        return true;
    }

    private bool CanMutateSelectedTaskQueue()
    {
        if (SelectedTask is null || _currentUser?.UserId is not int actorId)
            return false;

        if (SelectedTask.AssignedToUserId is not int assigneeId)
            return false;

        if (!CanSelectTaskScope)
            return assigneeId == actorId;

        return SelectedScope switch
        {
            TaskWorkbenchScope.MyTasks => assigneeId == actorId,
            TaskWorkbenchScope.SpecificUser => SelectedUserId is int uid && assigneeId == uid,
            TaskWorkbenchScope.AllUsers => true,
            _ => false,
        };
    }

    private async Task MoveSelectedUpAsync()
    {
        if (_taskQueue is null || SelectedTask is null || _currentUser?.UserId is not int actorId)
            return;

        var taskId = SelectedTask.TaskId;
        IsBusy = true;
        try
        {
            var result = await _taskQueue.MoveUpAsync(taskId, actorId).ConfigureAwait(true);
            if (!result.Succeeded)
            {
                StatusMessage = result.Message;
                return;
            }

            StatusMessage = $"הועבר למעלה: {FormatQueueMoveResult(result)}";
            await LoadAsync().ConfigureAwait(true);
            RestoreSelection(taskId);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            StatusMessage = $"שגיאה בהעברה למעלה: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task MoveSelectedDownAsync()
    {
        if (_taskQueue is null || SelectedTask is null || _currentUser?.UserId is not int actorId)
            return;

        var taskId = SelectedTask.TaskId;
        IsBusy = true;
        try
        {
            var result = await _taskQueue.MoveDownAsync(taskId, actorId).ConfigureAwait(true);
            if (!result.Succeeded)
            {
                StatusMessage = result.Message;
                return;
            }

            StatusMessage = $"הועבר למטה: {FormatQueueMoveResult(result)}";
            await LoadAsync().ConfigureAwait(true);
            RestoreSelection(taskId);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            StatusMessage = $"שגיאה בהעברה למטה: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string FormatQueueMoveResult(TaskQueueOperationResult result)
    {
        if (result.OldPriority is int oldPriority && result.NewPriority is int newPriority)
            return $"מיקום {oldPriority} → {newPriority}";

        return result.Message;
    }

    private void RestoreSelection(int taskId)
    {
        var match = FindTaskById(taskId);
        if (match is not null)
            SelectedTask = match;
    }

    private TaskSummaryDto? FindTaskById(int taskId) =>
        QuickTasks.FirstOrDefault(t => t.TaskId == taskId)
        ?? MediumTasks.FirstOrDefault(t => t.TaskId == taskId)
        ?? LongTasks.FirstOrDefault(t => t.TaskId == taskId);

    private IReadOnlyList<TaskSummaryDto> GetActiveQueueTasksForSelected()
    {
        var bucket = GetBucketCollectionForSelected();
        if (bucket is null || SelectedTask?.AssignedToUserId is not int assigneeId)
            return [];

        return bucket
            .Where(t => t.AssignedToUserId == assigneeId && t.WorkPriority is > 0)
            .OrderBy(t => t.WorkPriority)
            .ToList();
    }

    private ObservableCollection<TaskSummaryDto>? GetBucketCollectionForSelected() =>
        SelectedTask?.WorkQueueBucket switch
        {
            WorkQueueBucketCodes.Quick => QuickTasks,
            WorkQueueBucketCodes.Medium => MediumTasks,
            WorkQueueBucketCodes.Long => LongTasks,
            _ => null,
        };

    private void ReplaceAll(
        IReadOnlyList<TaskSummaryDto> quick,
        IReadOnlyList<TaskSummaryDto> medium,
        IReadOnlyList<TaskSummaryDto> longBucket,
        int? projectFilterId)
    {
        QuickTasks.Clear();
        MediumTasks.Clear();
        LongTasks.Clear();
        foreach (var task in FilterByProject(quick, projectFilterId)) QuickTasks.Add(task);
        foreach (var task in FilterByProject(medium, projectFilterId)) MediumTasks.Add(task);
        foreach (var task in FilterByProject(longBucket, projectFilterId)) LongTasks.Add(task);
    }

    private static IReadOnlyList<TaskSummaryDto> FilterByProject(
        IReadOnlyList<TaskSummaryDto> tasks,
        int? projectFilterId) =>
        projectFilterId is int projectId
            ? tasks.Where(t => t.ProjectId == projectId).ToList()
            : tasks;

    internal async Task OpenSelectedTaskAsync(CancellationToken ct = default)
    {
        if (SelectedTask is null || _workSurfaceLauncher is null)
            return;

        // Capture before LoadAsync — reload clears SelectedTask.
        var taskId = SelectedTask.TaskId;

        IsBusy = true;
        try
        {
            var opened = await _workSurfaceLauncher
                .TryOpenFromTaskAsync(taskId, ct)
                .ConfigureAwait(true);

            // Surfaces (e.g. OpenQuoteProject) may close/advance the workflow — reload the board.
            await LoadAsync(ct).ConfigureAwait(true);

            StatusMessage = opened
                ? $"נפתחה משימה #{taskId}."
                : $"לא ניתן לפתוח את משימה #{taskId}. אין fallback.";
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            StatusMessage = $"שגיאה בפתיחת משימה: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal async Task ResolveSelectedAsync(CancellationToken ct = default)
    {
        if (SelectedTask is null) return;
        IsBusy = true;
        try
        {
            var context = await _taskNavigation.ResolveAsync(SelectedTask.TaskId, ct).ConfigureAwait(true);
            ResolvePreview = context is null
                ? "לא ניתן לפתוח את המשימה דרך WorkSurfaceContext. אין fallback."
                : FormatContext(context);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            ResolvePreview = $"שגיאה ב-Resolve: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal static string FormatContext(WorkSurfaceContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"TaskId: {context.TaskId}");
        sb.AppendLine($"ProjectId: {context.ProjectId}");
        sb.AppendLine($"WorkflowInstanceId: {context.WorkflowInstanceId}");
        sb.AppendLine($"ComponentKey: {context.ComponentKey}");
        sb.AppendLine($"PrimaryWorkTargetEntityId: {context.PrimaryWorkTargetEntityId}");
        sb.AppendLine($"TaskTypeCode: {context.TaskTypeCode}");
        sb.AppendLine($"CompletionEventCode: {context.CompletionEventCode}");
        sb.AppendLine($"ActingUserId: {context.ActingUserId}");
        sb.Append("AllowedResultCodes: ");
        sb.Append(context.AllowedResultCodes.Count == 0 ? "(none)" : string.Join(", ", context.AllowedResultCodes));
        return sb.ToString();
    }

    private void RaiseCommandStates()
    {
        (RefreshCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (OpenTaskCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ResolveCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (AddTaskCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (DeleteTaskCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (DeactivateTaskCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ReactivateTaskCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ClearSelectedProjectCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RepairQueueCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (MoveUpCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (MoveDownCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private sealed class DesignTaskQueueService : ITaskQueueService
    {
        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetUserQueueAsync(int userId, int workQueueBucket, CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>([]);
        public ValueTask MoveWithinBucketAsync(int taskId, int newPosition, int changedByUserId, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask ChangeBucketAsync(int taskId, int newBucket, int changedByUserId, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask<int> ValidateAndRepairQueueAsync(int userId, int workQueueBucket, CancellationToken ct = default) => ValueTask.FromResult(0);
        public ValueTask<TaskQueueRepairResult> RepairQueueAsync(int userId, int workQueueBucket, CancellationToken ct = default) =>
            ValueTask.FromResult(TaskQueueRepairResult.Empty);
        public ValueTask<TaskQueueRepairResult> RepairAllQueuesAsync(CancellationToken ct = default) =>
            ValueTask.FromResult(TaskQueueRepairResult.Empty);
        public ValueTask<TaskQueueOperationResult> MoveUpAsync(int taskId, int changedByUserId, CancellationToken ct = default) =>
            ValueTask.FromResult(new TaskQueueOperationResult(true, "ok", taskId));
        public ValueTask<TaskQueueOperationResult> MoveDownAsync(int taskId, int changedByUserId, CancellationToken ct = default) =>
            ValueTask.FromResult(new TaskQueueOperationResult(true, "ok", taskId));
        public ValueTask<TaskQueueOperationResult> ReassignAsync(int taskId, int newUserId, int changedByUserId, CancellationToken ct = default) =>
            ValueTask.FromResult(new TaskQueueOperationResult(true, "ok", taskId));
    }

    private sealed class DesignTaskQueryService : ITaskQueryService
    {
        public ValueTask<TaskSummaryDto?> GetByIdAsync(int taskId, CancellationToken ct) => ValueTask.FromResult<TaskSummaryDto?>(null);
        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetTasksForProjectAsync(int projectId, bool includeClosed = false, int? workQueueBucket = null, CancellationToken ct = default) => ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>([]);
        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForUserAsync(int userId, int? workQueueBucket = null, CancellationToken ct = default) => ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>([]);
        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForUserByBucketAsync(int userId, int workQueueBucket, CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>([]);
        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForAllUsersByBucketAsync(int workQueueBucket, CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>([]);
    }

    private sealed class DesignTaskNavigationService : ITaskNavigationService
    {
        public ValueTask<WorkSurfaceContext?> ResolveAsync(int taskId, CancellationToken ct) => ValueTask.FromResult<WorkSurfaceContext?>(null);
    }
}

/// <summary>Backward-compatible alias — use <see cref="TaskWorkbenchViewModel"/>.</summary>
public sealed class TaskPanelReadOnlyViewModel : TaskWorkbenchViewModel
{
    public TaskPanelReadOnlyViewModel()
    {
    }

    public TaskPanelReadOnlyViewModel(
        ITaskQueryService taskQuery,
        ITaskNavigationService taskNavigation,
        ICurrentUserContext? currentUser = null,
        IProjectQueryService? projectQuery = null,
        IProjectFilterOptionsService? projectFilterOptions = null)
        : base(taskQuery, taskNavigation, null, currentUser, null, null, null, projectQuery, projectFilterOptions, null, null)
    {
    }

    public override string Title => "משימות — קריאה בלבד";
}
