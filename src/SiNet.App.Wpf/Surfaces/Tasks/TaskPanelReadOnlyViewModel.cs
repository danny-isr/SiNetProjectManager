using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.Application.Identity;
using SiNet.Application.Projects;
using SiNet.Application.Tasks;
using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.Surfaces.Tasks;

/// <summary>
/// Read-only Task Panel: three personal work-queue buckets via <see cref="ITaskQueryService"/>.
/// Resolve preview uses <see cref="ITaskNavigationService"/> only — no writes, no legacy panel, no fallback.
/// </summary>
public sealed class TaskPanelReadOnlyViewModel : ObservableObject
{
    private readonly ITaskQueryService _taskQuery;
    private readonly ITaskNavigationService _taskNavigation;
    private readonly ICurrentUserContext? _currentUser;
    private readonly ICurrentProjectContext? _currentProject;

    private TaskSummaryDto? _selectedTask;
    private string _statusMessage = "טוען משימות...";
    private string _resolvePreview = string.Empty;
    private bool _isBusy;

    public TaskPanelReadOnlyViewModel()
        : this(
            new DesignTaskQueryService(),
            new DesignTaskNavigationService(),
            null,
            new InMemoryCurrentProjectContext())
    {
    }

    public TaskPanelReadOnlyViewModel(
        ITaskQueryService taskQuery,
        ITaskNavigationService taskNavigation,
        ICurrentUserContext? currentUser = null,
        ICurrentProjectContext? currentProject = null)
    {
        _taskQuery = taskQuery ?? throw new ArgumentNullException(nameof(taskQuery));
        _taskNavigation = taskNavigation ?? throw new ArgumentNullException(nameof(taskNavigation));
        _currentUser = currentUser;
        _currentProject = currentProject;

        QuickTasks = [];
        MediumTasks = [];
        LongTasks = [];

        RefreshCommand = new AsyncRelayCommand(() => LoadAsync(), () => !IsBusy);
        ResolveCommand = new AsyncRelayCommand(() => ResolveSelectedAsync(), () => !IsBusy && SelectedTask is not null);
    }

    public string Title => "משימות — קריאה בלבד";

    public ObservableCollection<TaskSummaryDto> QuickTasks { get; }
    public ObservableCollection<TaskSummaryDto> MediumTasks { get; }
    public ObservableCollection<TaskSummaryDto> LongTasks { get; }

    public TaskSummaryDto? SelectedTask
    {
        get => _selectedTask;
        set
        {
            if (SetField(ref _selectedTask, value))
            {
                ResolvePreview = string.Empty;
                (ResolveCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
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
            {
                (RefreshCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
                (ResolveCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public ICommand RefreshCommand { get; }
    public ICommand ResolveCommand { get; }

    public async Task InitializeAsync(CancellationToken ct = default)
        => await LoadAsync(ct).ConfigureAwait(true);

    internal async Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            QuickTasks.Clear();
            MediumTasks.Clear();
            LongTasks.Clear();
            SelectedTask = null;

            var userId = _currentUser?.UserId;
            var projectId = _currentProject?.CurrentProject?.ProjectId;

            if (userId is int uid)
            {
                await LoadUserBucketsAsync(uid, ct).ConfigureAwait(true);
                StatusMessage = $"נטענו משימות פתוחות למשתמש {uid} (שלושה תורים אישיים).";
                return;
            }

            if (projectId is int pid and > 0)
            {
                await LoadProjectBucketsAsync(pid, ct).ConfigureAwait(true);
                StatusMessage = $"נטענו משימות פתוחות לפרויקט {pid} (לפי bucket).";
                return;
            }

            StatusMessage = "בחר פרויקט או התחבר כמשתמש כדי לראות משימות.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"שגיאה בטעינת משימות: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadUserBucketsAsync(int userId, CancellationToken ct)
    {
        var quick = await _taskQuery.GetOpenTasksForUserByBucketAsync(userId, WorkQueueBucketCodes.Quick, ct)
            .ConfigureAwait(true);
        var medium = await _taskQuery.GetOpenTasksForUserByBucketAsync(userId, WorkQueueBucketCodes.Medium, ct)
            .ConfigureAwait(true);
        var longBucket = await _taskQuery.GetOpenTasksForUserByBucketAsync(userId, WorkQueueBucketCodes.Long, ct)
            .ConfigureAwait(true);

        ReplaceAll(quick, medium, longBucket);
    }

    private async Task LoadProjectBucketsAsync(int projectId, CancellationToken ct)
    {
        var quick = await _taskQuery.GetTasksForProjectAsync(projectId, includeClosed: false, WorkQueueBucketCodes.Quick, ct)
            .ConfigureAwait(true);
        var medium = await _taskQuery.GetTasksForProjectAsync(projectId, includeClosed: false, WorkQueueBucketCodes.Medium, ct)
            .ConfigureAwait(true);
        var longBucket = await _taskQuery.GetTasksForProjectAsync(projectId, includeClosed: false, WorkQueueBucketCodes.Long, ct)
            .ConfigureAwait(true);

        ReplaceAll(quick, medium, longBucket);
    }

    private void ReplaceAll(
        IReadOnlyList<TaskSummaryDto> quick,
        IReadOnlyList<TaskSummaryDto> medium,
        IReadOnlyList<TaskSummaryDto> longBucket)
    {
        QuickTasks.Clear();
        MediumTasks.Clear();
        LongTasks.Clear();

        foreach (var task in quick)
            QuickTasks.Add(task);
        foreach (var task in medium)
            MediumTasks.Add(task);
        foreach (var task in longBucket)
            LongTasks.Add(task);
    }

    internal async Task ResolveSelectedAsync(CancellationToken ct = default)
    {
        if (SelectedTask is null)
            return;

        IsBusy = true;
        try
        {
            var context = await _taskNavigation.ResolveAsync(SelectedTask.TaskId, ct).ConfigureAwait(true);
            if (context is null)
            {
                ResolvePreview = "לא ניתן לפתוח את המשימה דרך WorkSurfaceContext. אין fallback.";
                return;
            }

            ResolvePreview = FormatContext(context);
        }
        catch (Exception ex)
        {
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
        sb.Append(context.AllowedResultCodes.Count == 0
            ? "(none)"
            : string.Join(", ", context.AllowedResultCodes));
        return sb.ToString();
    }

    private sealed class DesignTaskQueryService : ITaskQueryService
    {
        public ValueTask<TaskSummaryDto?> GetByIdAsync(int taskId, CancellationToken ct) => ValueTask.FromResult<TaskSummaryDto?>(null);

        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetTasksForProjectAsync(
            int projectId, bool includeClosed = false, int? workQueueBucket = null, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>([]);

        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForUserAsync(
            int userId, int? workQueueBucket = null, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>([]);

        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForUserByBucketAsync(
            int userId, int workQueueBucket, CancellationToken ct)
            => ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>([]);
    }

    private sealed class DesignTaskNavigationService : ITaskNavigationService
    {
        public ValueTask<WorkSurfaceContext?> ResolveAsync(int taskId, CancellationToken ct) =>
            ValueTask.FromResult<WorkSurfaceContext?>(null);
    }
}
