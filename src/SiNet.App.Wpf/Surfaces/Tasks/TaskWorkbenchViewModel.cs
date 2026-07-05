using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Identity;
using SiNet.Application.Projects;
using SiNet.Application.Tasks;
using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.Surfaces.Tasks;

/// <summary>
/// Task Workbench: three personal work-queue buckets, diagnostics, basic create/delete.
/// Hosts the shared <see cref="ProjectSelectorViewModel"/> for project selection (see <c>docs/PROJECTS.md</c>).
/// </summary>
public class TaskWorkbenchViewModel : ObservableObject, IDisposable
{
    private readonly ITaskQueryService _taskQuery;
    private readonly ITaskNavigationService _taskNavigation;
    private readonly ITaskWorkbenchService? _workbench;
    private readonly ITaskQueueService? _taskQueue;
    private readonly ICurrentUserContext? _currentUser;
    private readonly ICurrentProjectContext? _currentProject;
    private readonly IAuthorizationQueryService? _authorization;
    private readonly IUserLookupService? _userLookup;

    private TaskSummaryDto? _selectedTask;
    private string _statusMessage = "טוען משימות...";
    private string _diagnosticsText = string.Empty;
    private string _resolvePreview = string.Empty;
    private string _newTitle = string.Empty;
    private string _newBody = string.Empty;
    private string _activeProjectDisplay = "לא נבחר פרויקט";
    private bool _isBusy;
    private bool _isAddPanelVisible;
    private TaskLookupItemDto? _selectedAssignee;
    private TaskLookupItemDto? _selectedTaskType;
    private TaskLookupItemDto? _selectedStatus;
    private TaskLookupItemDto? _selectedBucket;
    private DateTime? _newDueDate;
    private TaskWorkbenchScope _selectedScope = TaskWorkbenchScope.MyTasks;
    private int? _selectedUserId;
    private bool _canSelectTaskScope;
    private bool _scopeOptionsInitialized;
    private bool _suppressScopeReload;
    private bool _disposed;

    public TaskWorkbenchViewModel()
        : this(
            new DesignTaskQueryService(),
            new DesignTaskNavigationService(),
            null,
            null,
            new InMemoryCurrentProjectContext(),
            null,
            null,
            null,
            new FakeProjectQueryService(),
            new FakeProjectFilterOptionsService())
    {
    }

    public TaskWorkbenchViewModel(
        ITaskQueryService taskQuery,
        ITaskNavigationService taskNavigation,
        ITaskWorkbenchService? workbench = null,
        ICurrentUserContext? currentUser = null,
        ICurrentProjectContext? currentProject = null,
        IAuthorizationQueryService? authorization = null,
        IUserLookupService? userLookup = null,
        ITaskQueueService? taskQueue = null,
        IProjectQueryService? projectQuery = null,
        IProjectFilterOptionsService? projectFilterOptions = null)
    {
        _taskQuery = taskQuery ?? throw new ArgumentNullException(nameof(taskQuery));
        _taskNavigation = taskNavigation ?? throw new ArgumentNullException(nameof(taskNavigation));
        _workbench = workbench;
        _taskQueue = taskQueue;
        _currentUser = currentUser;
        _currentProject = currentProject;
        _authorization = authorization;
        _userLookup = userLookup;

        QuickTasks = [];
        MediumTasks = [];
        LongTasks = [];
        Users = [];
        TaskTypes = [];
        Statuses = [];
        Buckets = [];
        AvailableScopes = [];
        AvailableUsers = [];

        if (projectQuery is not null && projectFilterOptions is not null && _currentProject is not null)
        {
            ProjectSelector = new ProjectSelectorViewModel(projectQuery, projectFilterOptions, _currentProject);
            HasProjectSelector = true;
        }

        if (_currentProject is not null)
        {
            _currentProject.CurrentProjectChanged += OnCurrentProjectChanged;
            UpdateActiveProjectDisplay(_currentProject.CurrentProject);
        }

        RefreshCommand = new AsyncRelayCommand(() => LoadAsync(), () => !IsBusy);
        ResolveCommand = new AsyncRelayCommand(() => ResolveSelectedAsync(), () => !IsBusy && SelectedTask is not null);
        ShowAddPanelCommand = new RelayCommand(_ =>
        {
            if (!HasSelectedProject)
            {
                StatusMessage = "לא נבחר פרויקט";
                return;
            }

            IsAddPanelVisible = true;
            ApplyCreationDefaults();
        }, _ => !IsBusy && _workbench is not null);
        HideAddPanelCommand = new RelayCommand(_ => IsAddPanelVisible = false);
        CreateTaskCommand = new AsyncRelayCommand(CreateTaskAsync, CanCreateTask);
        DeleteTaskCommand = new AsyncRelayCommand(DeleteSelectedAsync, () => !IsBusy && SelectedTask is not null && _workbench is not null);
        RepairQueueCommand = new AsyncRelayCommand(RepairQueueAsync, () => !IsBusy && CanManageQueue);
        MoveUpCommand = new AsyncRelayCommand(MoveSelectedUpAsync, CanMoveSelectedUp);
        MoveDownCommand = new AsyncRelayCommand(MoveSelectedDownAsync, CanMoveSelectedDown);
    }

    /// <summary>Shared project selector hosted in the workbench header.</summary>
    public ProjectSelectorViewModel? ProjectSelector { get; }

    public bool HasProjectSelector { get; }

    public string ActiveProjectDisplay
    {
        get => _activeProjectDisplay;
        private set => SetField(ref _activeProjectDisplay, value);
    }

    public bool HasSelectedProject => SelectedProjectId is int;

    public virtual string Title => "משימות — Task Workbench";

    public string QueryServiceName => _taskQuery.GetType().Name;

    public string QueueServiceName => _taskQueue?.GetType().Name ?? "(none)";

    public bool CanManageQueue => _taskQueue is not null && _currentUser?.UserId is int;

    public string LoadMode { get; private set; } = "None";

    public string? CurrentUserIdDisplay { get; private set; }

    public string? CurrentProjectIdDisplay { get; private set; }

    public ObservableCollection<TaskSummaryDto> QuickTasks { get; }
    public ObservableCollection<TaskSummaryDto> MediumTasks { get; }
    public ObservableCollection<TaskSummaryDto> LongTasks { get; }

    public ObservableCollection<TaskLookupItemDto> Users { get; }
    public ObservableCollection<TaskLookupItemDto> TaskTypes { get; }
    public ObservableCollection<TaskLookupItemDto> Statuses { get; }
    public ObservableCollection<TaskLookupItemDto> Buckets { get; }

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
            if (IsAddPanelVisible)
                ApplyCreationDefaults();

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
            if (IsAddPanelVisible)
                ApplyCreationDefaults();

            if (!_suppressScopeReload && _scopeOptionsInitialized && SelectedScope == TaskWorkbenchScope.SpecificUser)
                _ = LoadAsync();
        }
    }

    public bool IsSpecificUserScope => SelectedScope == TaskWorkbenchScope.SpecificUser;

    public string ScopeDisplayText =>
        CanSelectTaskScope
            ? TaskWorkbenchScopeLabels.GetDisplayName(SelectedScope)
            : $"מציג: {TaskWorkbenchScopeLabels.MyTasks}";

    public bool CanEditAssigneeOnCreate => CanSelectTaskScope;

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

    public bool IsAddPanelVisible
    {
        get => _isAddPanelVisible;
        set => SetField(ref _isAddPanelVisible, value);
    }

    public string NewTitle
    {
        get => _newTitle;
        set
        {
            if (SetField(ref _newTitle, value))
                (CreateTaskCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public string NewBody
    {
        get => _newBody;
        set => SetField(ref _newBody, value);
    }

    public int? SelectedProjectId => _currentProject?.CurrentProject?.ProjectId;

    public TaskLookupItemDto? SelectedAssignee
    {
        get => _selectedAssignee;
        set
        {
            if (SetField(ref _selectedAssignee, value))
                (CreateTaskCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public TaskLookupItemDto? SelectedTaskType
    {
        get => _selectedTaskType;
        set
        {
            if (!SetField(ref _selectedTaskType, value))
                return;

            if (value?.DefaultWorkQueueBucket is int bucket
                && WorkQueueBucketCodes.IsValid(bucket))
            {
                SelectedBucket = Buckets.FirstOrDefault(b => b.Id == bucket) ?? SelectedBucket;
            }

            (CreateTaskCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public TaskLookupItemDto? SelectedStatus
    {
        get => _selectedStatus;
        set
        {
            if (SetField(ref _selectedStatus, value))
                (CreateTaskCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public TaskLookupItemDto? SelectedBucket
    {
        get => _selectedBucket;
        set
        {
            if (SetField(ref _selectedBucket, value))
                (CreateTaskCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public DateTime? NewDueDate
    {
        get => _newDueDate;
        set => SetField(ref _newDueDate, value);
    }

    public bool CanWrite => _workbench is not null;

    public ICommand RefreshCommand { get; }
    public ICommand ResolveCommand { get; }
    public ICommand ShowAddPanelCommand { get; }
    public ICommand HideAddPanelCommand { get; }
    public ICommand CreateTaskCommand { get; }
    public ICommand DeleteTaskCommand { get; }
    public ICommand RepairQueueCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (ProjectSelector is not null)
            await ProjectSelector.InitializeAsync(ct).ConfigureAwait(true);

        await InitializeScopeOptionsAsync(ct).ConfigureAwait(true);
        await LoadCreationOptionsAsync(ct).ConfigureAwait(true);
        await LoadAsync(ct).ConfigureAwait(true);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_currentProject is not null)
            _currentProject.CurrentProjectChanged -= OnCurrentProjectChanged;

        ProjectSelector?.Dispose();
    }

    private void OnCurrentProjectChanged(object? sender, ProjectChangedEventArgs e)
    {
        UpdateActiveProjectDisplay(e.Project);
        ApplyCreationDefaults();
        _ = LoadAsync();
    }

    private void UpdateActiveProjectDisplay(ProjectSummaryDto? project)
    {
        ActiveProjectDisplay = project is null
            ? "לא נבחר פרויקט"
            : $"{project.ProjectNumber} — {project.ProjectName}";
        OnPropertyChanged(nameof(SelectedProjectId));
        OnPropertyChanged(nameof(HasSelectedProject));
        (CreateTaskCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

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

        IsBusy = true;
        BucketCounts counts = default;
        try
        {
            QuickTasks.Clear();
            MediumTasks.Clear();
            LongTasks.Clear();
            SelectedTask = null;

            var userId = _currentUser?.UserId;
            var projectId = _currentProject?.CurrentProject?.ProjectId;

            CurrentUserIdDisplay = userId?.ToString() ?? "(none)";
            CurrentProjectIdDisplay = projectId?.ToString() ?? "(none)";
            OnPropertyChanged(nameof(CurrentUserIdDisplay));
            OnPropertyChanged(nameof(CurrentProjectIdDisplay));

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
                            ? "לא נמצאו משימות פתוחות לכל המשתמשים."
                            : $"מציג משימות של כל המשתמשים. נטענו {counts.Total}: קצר={counts.Quick}, בינוני={counts.Medium}, ארוך={counts.Long}";
                        break;

                    default:
                        LoadMode = TaskWorkbenchScope.MyTasks.ToString();
                        counts = await LoadUserBucketsAsync(uid, ct).ConfigureAwait(true);
                        StatusMessage = await BuildUserStatusMessageAsync(uid, counts, ct).ConfigureAwait(true);
                        break;
                }
            }
            else if (projectId is int pid and > 0)
            {
                LoadMode = "Project";
                counts = await LoadProjectBucketsAsync(pid, ct).ConfigureAwait(true);
                StatusMessage = FormatProjectStatusMessage(pid, counts);
            }
            else
            {
                LoadMode = "None";
                StatusMessage = "לא נבחר פרויקט";
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
        }
    }

    private async Task LoadCreationOptionsAsync(CancellationToken ct)
    {
        if (_workbench is null)
            return;

        var options = await _workbench.GetTaskCreationOptionsAsync(ct).ConfigureAwait(true);
        ReplaceLookup(Users, options.Users);
        ReplaceLookup(TaskTypes, options.TaskTypes);
        ReplaceLookup(Statuses, options.Statuses);
        ReplaceLookup(Buckets, options.Buckets);
        ApplyCreationDefaults();
    }

    private void ApplyCreationDefaults()
    {
        if (!CanSelectTaskScope && _currentUser?.UserId is int lockedUid)
            SelectedAssignee = Users.FirstOrDefault(u => u.Id == lockedUid);
        else if (SelectedScope == TaskWorkbenchScope.SpecificUser && SelectedUserId is int scopeUserId)
            SelectedAssignee = Users.FirstOrDefault(u => u.Id == scopeUserId);
        else if (SelectedScope == TaskWorkbenchScope.AllUsers)
            SelectedAssignee = null;
        else if (_currentUser?.UserId is int uid)
            SelectedAssignee = Users.FirstOrDefault(u => u.Id == uid) ?? SelectedAssignee;

        SelectedStatus ??= Statuses.FirstOrDefault();
        SelectedTaskType ??= TaskTypes.FirstOrDefault();
        if (SelectedTaskType?.DefaultWorkQueueBucket is int defaultBucket
            && WorkQueueBucketCodes.IsValid(defaultBucket))
        {
            SelectedBucket = Buckets.FirstOrDefault(b => b.Id == defaultBucket);
        }
        else
        {
            SelectedBucket ??= Buckets.FirstOrDefault(b => b.Id == WorkQueueBucketCodes.Medium);
        }

        (CreateTaskCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private async Task<BucketCounts> LoadUserBucketsAsync(int userId, CancellationToken ct)
    {
        var quick = await _taskQuery.GetOpenTasksForUserByBucketAsync(userId, WorkQueueBucketCodes.Quick, ct).ConfigureAwait(true);
        var medium = await _taskQuery.GetOpenTasksForUserByBucketAsync(userId, WorkQueueBucketCodes.Medium, ct).ConfigureAwait(true);
        var longBucket = await _taskQuery.GetOpenTasksForUserByBucketAsync(userId, WorkQueueBucketCodes.Long, ct).ConfigureAwait(true);
        ReplaceAll(quick, medium, longBucket, SelectedProjectId);
        return new BucketCounts(QuickTasks.Count, MediumTasks.Count, LongTasks.Count);
    }

    private async Task<BucketCounts> LoadAllUsersBucketsAsync(CancellationToken ct)
    {
        var quick = await _taskQuery.GetOpenTasksForAllUsersByBucketAsync(WorkQueueBucketCodes.Quick, ct).ConfigureAwait(true);
        var medium = await _taskQuery.GetOpenTasksForAllUsersByBucketAsync(WorkQueueBucketCodes.Medium, ct).ConfigureAwait(true);
        var longBucket = await _taskQuery.GetOpenTasksForAllUsersByBucketAsync(WorkQueueBucketCodes.Long, ct).ConfigureAwait(true);
        ReplaceAll(quick, medium, longBucket, SelectedProjectId);
        return new BucketCounts(QuickTasks.Count, MediumTasks.Count, LongTasks.Count);
    }

    private async Task<BucketCounts> LoadProjectBucketsAsync(int projectId, CancellationToken ct)
    {
        var quick = await _taskQuery.GetTasksForProjectAsync(projectId, includeClosed: false, WorkQueueBucketCodes.Quick, ct).ConfigureAwait(true);
        var medium = await _taskQuery.GetTasksForProjectAsync(projectId, includeClosed: false, WorkQueueBucketCodes.Medium, ct).ConfigureAwait(true);
        var longBucket = await _taskQuery.GetTasksForProjectAsync(projectId, includeClosed: false, WorkQueueBucketCodes.Long, ct).ConfigureAwait(true);
        ReplaceAll(quick, medium, longBucket, null);
        return new BucketCounts(quick.Count, medium.Count, longBucket.Count);
    }

    private async Task<string> BuildUserStatusMessageAsync(int userId, BucketCounts counts, CancellationToken ct)
    {
        if (counts.Total == 0)
        {
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
        sb.AppendLine($"CanSelectTaskScope: {CanSelectTaskScope}");
        sb.AppendLine($"ProjectId: {CurrentProjectIdDisplay}");
        sb.AppendLine($"ActiveProject: {ActiveProjectDisplay}");
        sb.AppendLine($"QueryService: {QueryServiceName}");
        sb.AppendLine($"QueueService: {QueueServiceName}");
        sb.AppendLine($"Counts: Quick={counts.Quick}, Medium={counts.Medium}, Long={counts.Long}");
        if (!string.IsNullOrEmpty(error))
            sb.AppendLine($"Error: {error}");
        return sb.ToString().TrimEnd();
    }

    private bool CanCreateTask() =>
        !IsBusy && _workbench is not null && HasSelectedProject && !string.IsNullOrWhiteSpace(NewTitle)
        && SelectedAssignee is not null && SelectedTaskType is not null
        && SelectedStatus is not null && SelectedBucket is not null && _currentUser?.UserId is int actorId
        && ValidateAssigneeForCreate(actorId);

    private bool ValidateAssigneeForCreate(int actorId)
    {
        if (SelectedAssignee is null)
            return false;

        if (!CanSelectTaskScope)
            return SelectedAssignee.Id == actorId;

        return true;
    }

    private async Task CreateTaskAsync()
    {
        if (_workbench is null || _currentUser?.UserId is not int actorId)
            return;

        if (_currentProject?.CurrentProject?.ProjectId is not int projectId)
        {
            StatusMessage = "לא נבחר פרויקט";
            MessageBox.Show(
                "לא נבחר פרויקט. בחר פרויקט ב-Project Selector לפני יצירת משימה.",
                "יצירת משימה",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!ValidateAssigneeForCreate(actorId))
        {
            MessageBox.Show(
                "אין הרשאה ליצור משימה למשתמש אחר.",
                "יצירת משימה",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        try
        {
            var request = new CreateTaskRequest(
                projectId,
                SelectedAssignee!.Id,
                SelectedTaskType!.Id,
                SelectedStatus!.Id,
                NewTitle.Trim(),
                SelectedBucket!.Id,
                Body: string.IsNullOrWhiteSpace(NewBody) ? null : NewBody.Trim(),
                DueDate: NewDueDate);

            var result = await _workbench.CreateTaskAsync(request, actorId).ConfigureAwait(true);
            if (!result.Succeeded)
            {
                StatusMessage = result.Message;
                MessageBox.Show(result.Message, "יצירת משימה", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            NewTitle = string.Empty;
            NewBody = string.Empty;
            IsAddPanelVisible = false;
            StatusMessage = $"נוצרה משימה בפרויקט {ActiveProjectDisplay}.";
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            MessageBox.Show(ex.Message, "יצירת משימה", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteSelectedAsync()
    {
        if (_workbench is null || SelectedTask is null || _currentUser?.UserId is not int actorId)
            return;

        if (MessageBox.Show("למחוק את המשימה שנבחרה?", "מחיקת משימה", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        try
        {
            var result = await _workbench.DeleteTaskAsync(SelectedTask.TaskId, actorId).ConfigureAwait(true);
            if (!result.Succeeded)
            {
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

        if (LoadMode == "Project")
            return CanSelectTaskScope || assigneeId == actorId;

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

    private static void ReplaceLookup(ObservableCollection<TaskLookupItemDto> target, IReadOnlyList<TaskLookupItemDto> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
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
        (ResolveCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (CreateTaskCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (DeleteTaskCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
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
        ICurrentProjectContext? currentProject = null,
        IProjectQueryService? projectQuery = null,
        IProjectFilterOptionsService? projectFilterOptions = null)
        : base(taskQuery, taskNavigation, null, currentUser, currentProject, null, null, null, projectQuery, projectFilterOptions)
    {
    }

    public override string Title => "משימות — קריאה בלבד";
}
