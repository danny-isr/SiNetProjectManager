using System.Collections.ObjectModel;
using System.Diagnostics;
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
/// </summary>
public class TaskWorkbenchViewModel : ObservableObject
{
    private readonly ITaskQueryService _taskQuery;
    private readonly ITaskNavigationService _taskNavigation;
    private readonly ITaskWorkbenchService? _workbench;
    private readonly ICurrentUserContext? _currentUser;
    private readonly ICurrentProjectContext? _currentProject;
    private readonly IAuthorizationQueryService? _authorization;
    private readonly IUserLookupService? _userLookup;

    private TaskSummaryDto? _selectedTask;
    private string _statusMessage = "טוען משימות...";
    private string _diagnosticsText = string.Empty;
    private string _resolvePreview = string.Empty;
    private string _newTitle = string.Empty;
    private bool _isBusy;
    private bool _isAddPanelVisible;
    private TaskLookupItemDto? _selectedProject;
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

    public TaskWorkbenchViewModel()
        : this(new DesignTaskQueryService(), new DesignTaskNavigationService(), null, null, null, null, null)
    {
    }

    public TaskWorkbenchViewModel(
        ITaskQueryService taskQuery,
        ITaskNavigationService taskNavigation,
        ITaskWorkbenchService? workbench = null,
        ICurrentUserContext? currentUser = null,
        ICurrentProjectContext? currentProject = null,
        IAuthorizationQueryService? authorization = null,
        IUserLookupService? userLookup = null)
    {
        _taskQuery = taskQuery ?? throw new ArgumentNullException(nameof(taskQuery));
        _taskNavigation = taskNavigation ?? throw new ArgumentNullException(nameof(taskNavigation));
        _workbench = workbench;
        _currentUser = currentUser;
        _currentProject = currentProject;
        _authorization = authorization;
        _userLookup = userLookup;

        QuickTasks = [];
        MediumTasks = [];
        LongTasks = [];
        Projects = [];
        Users = [];
        TaskTypes = [];
        Statuses = [];
        Buckets = [];
        AvailableScopes = [];
        AvailableUsers = [];

        RefreshCommand = new AsyncRelayCommand(() => LoadAsync(), () => !IsBusy);
        ResolveCommand = new AsyncRelayCommand(() => ResolveSelectedAsync(), () => !IsBusy && SelectedTask is not null);
        ShowAddPanelCommand = new RelayCommand(_ =>
        {
            IsAddPanelVisible = true;
            ApplyCreationDefaults();
        }, _ => !IsBusy && _workbench is not null);
        HideAddPanelCommand = new RelayCommand(_ => IsAddPanelVisible = false);
        CreateTaskCommand = new AsyncRelayCommand(CreateTaskAsync, CanCreateTask);
        DeleteTaskCommand = new AsyncRelayCommand(DeleteSelectedAsync, () => !IsBusy && SelectedTask is not null && _workbench is not null);
    }

    public virtual string Title => "משימות — Task Workbench";

    public string QueryServiceName => _taskQuery.GetType().Name;

    public string LoadMode { get; private set; } = "None";

    public string? CurrentUserIdDisplay { get; private set; }

    public string? CurrentProjectIdDisplay { get; private set; }

    public ObservableCollection<TaskSummaryDto> QuickTasks { get; }
    public ObservableCollection<TaskSummaryDto> MediumTasks { get; }
    public ObservableCollection<TaskSummaryDto> LongTasks { get; }

    public ObservableCollection<TaskLookupItemDto> Projects { get; }
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
                RaiseCommandStates();
            }
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

    public TaskLookupItemDto? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (SetField(ref _selectedProject, value))
                (CreateTaskCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

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
            if (SetField(ref _selectedTaskType, value))
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

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await InitializeScopeOptionsAsync(ct).ConfigureAwait(true);
        await LoadCreationOptionsAsync(ct).ConfigureAwait(true);
        await LoadAsync(ct).ConfigureAwait(true);
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
                StatusMessage = "בחר פרויקט או התחבר כמשתמש כדי לראות משימות.";
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
        ReplaceLookup(Projects, options.Projects);
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

        if (_currentProject?.CurrentProject?.ProjectId is int pid)
            SelectedProject = Projects.FirstOrDefault(p => p.Id == pid) ?? SelectedProject;

        SelectedBucket ??= Buckets.FirstOrDefault(b => b.Id == WorkQueueBucketCodes.Medium);
        SelectedStatus ??= Statuses.FirstOrDefault();
        SelectedTaskType ??= TaskTypes.FirstOrDefault();

        (CreateTaskCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private async Task<BucketCounts> LoadUserBucketsAsync(int userId, CancellationToken ct)
    {
        var quick = await _taskQuery.GetOpenTasksForUserByBucketAsync(userId, WorkQueueBucketCodes.Quick, ct).ConfigureAwait(true);
        var medium = await _taskQuery.GetOpenTasksForUserByBucketAsync(userId, WorkQueueBucketCodes.Medium, ct).ConfigureAwait(true);
        var longBucket = await _taskQuery.GetOpenTasksForUserByBucketAsync(userId, WorkQueueBucketCodes.Long, ct).ConfigureAwait(true);
        ReplaceAll(quick, medium, longBucket);
        return new BucketCounts(quick.Count, medium.Count, longBucket.Count);
    }

    private async Task<BucketCounts> LoadAllUsersBucketsAsync(CancellationToken ct)
    {
        var quick = await _taskQuery.GetOpenTasksForAllUsersByBucketAsync(WorkQueueBucketCodes.Quick, ct).ConfigureAwait(true);
        var medium = await _taskQuery.GetOpenTasksForAllUsersByBucketAsync(WorkQueueBucketCodes.Medium, ct).ConfigureAwait(true);
        var longBucket = await _taskQuery.GetOpenTasksForAllUsersByBucketAsync(WorkQueueBucketCodes.Long, ct).ConfigureAwait(true);
        ReplaceAll(quick, medium, longBucket);
        return new BucketCounts(quick.Count, medium.Count, longBucket.Count);
    }

    private async Task<BucketCounts> LoadProjectBucketsAsync(int projectId, CancellationToken ct)
    {
        var quick = await _taskQuery.GetTasksForProjectAsync(projectId, includeClosed: false, WorkQueueBucketCodes.Quick, ct).ConfigureAwait(true);
        var medium = await _taskQuery.GetTasksForProjectAsync(projectId, includeClosed: false, WorkQueueBucketCodes.Medium, ct).ConfigureAwait(true);
        var longBucket = await _taskQuery.GetTasksForProjectAsync(projectId, includeClosed: false, WorkQueueBucketCodes.Long, ct).ConfigureAwait(true);
        ReplaceAll(quick, medium, longBucket);
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
        sb.AppendLine($"QueryService: {QueryServiceName}");
        sb.AppendLine($"Counts: Quick={counts.Quick}, Medium={counts.Medium}, Long={counts.Long}");
        if (!string.IsNullOrEmpty(error))
            sb.AppendLine($"Error: {error}");
        return sb.ToString().TrimEnd();
    }

    private bool CanCreateTask() =>
        !IsBusy && _workbench is not null && !string.IsNullOrWhiteSpace(NewTitle)
        && SelectedProject is not null && SelectedAssignee is not null && SelectedTaskType is not null
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
                SelectedProject!.Id, SelectedAssignee!.Id, SelectedTaskType!.Id, SelectedStatus!.Id,
                NewTitle.Trim(), SelectedBucket!.Id, NewDueDate);

            var result = await _workbench.CreateTaskAsync(request, actorId).ConfigureAwait(true);
            if (!result.Succeeded)
            {
                MessageBox.Show(result.Message, "יצירת משימה", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            NewTitle = string.Empty;
            IsAddPanelVisible = false;
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

    private void ReplaceAll(IReadOnlyList<TaskSummaryDto> quick, IReadOnlyList<TaskSummaryDto> medium, IReadOnlyList<TaskSummaryDto> longBucket)
    {
        QuickTasks.Clear();
        MediumTasks.Clear();
        LongTasks.Clear();
        foreach (var task in quick) QuickTasks.Add(task);
        foreach (var task in medium) MediumTasks.Add(task);
        foreach (var task in longBucket) LongTasks.Add(task);
    }

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
        ICurrentProjectContext? currentProject = null)
        : base(taskQuery, taskNavigation, null, currentUser, currentProject, null, null)
    {
    }

    public override string Title => "משימות — קריאה בלבד";
}
