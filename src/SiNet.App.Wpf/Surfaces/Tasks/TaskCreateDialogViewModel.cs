using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Identity;
using SiNet.Application.Projects;
using SiNet.Application.Tasks;

namespace SiNet.App.Wpf.Surfaces.Tasks;

/// <summary>View model for the Add Task dialog. Uses a local project context — never the app singleton.</summary>
public sealed class TaskCreateDialogViewModel : ObservableObject, IDisposable
{
    private readonly ITaskWorkbenchService _workbench;
    private readonly ICurrentUserContext _currentUser;
    private readonly IAuthorizationQueryService? _authorization;
    private readonly InMemoryCurrentProjectContext _dialogProjectContext = new();

    private string _title = string.Empty;
    private string _body = string.Empty;
    private DateTime? _dueDate;
    private TaskLookupItemDto? _selectedAssignee;
    private TaskLookupItemDto? _selectedTaskType;
    private TaskLookupItemDto? _selectedStatus;
    private TaskLookupItemDto? _selectedBucket;
    private string _validationMessage = string.Empty;
    private bool _isSaving;
    private bool _canSelectAssignee;
    private bool _disposed;

    public TaskCreateDialogViewModel(
        ITaskWorkbenchService workbench,
        ICurrentUserContext currentUser,
        IProjectQueryService projectQuery,
        IProjectFilterOptionsService projectFilterOptions,
        IAuthorizationQueryService? authorization = null)
    {
        _workbench = workbench ?? throw new ArgumentNullException(nameof(workbench));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _authorization = authorization;

        ProjectSelector = new ProjectSelectorViewModel(projectQuery, projectFilterOptions, _dialogProjectContext);
        Users = [];
        TaskTypes = [];
        Statuses = [];
        Buckets = [];

        CreateCommand = new AsyncRelayCommand(CreateAsync, CanCreate);
        CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false));
    }

    public ProjectSelectorViewModel ProjectSelector { get; }

    public ObservableCollection<TaskLookupItemDto> Users { get; }
    public ObservableCollection<TaskLookupItemDto> TaskTypes { get; }
    public ObservableCollection<TaskLookupItemDto> Statuses { get; }
    public ObservableCollection<TaskLookupItemDto> Buckets { get; }

    public int? SelectedProjectId => _dialogProjectContext.CurrentProject?.ProjectId;

    public string Title
    {
        get => _title;
        set
        {
            if (SetField(ref _title, value))
                (CreateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public string Body
    {
        get => _body;
        set => SetField(ref _body, value);
    }

    public DateTime? DueDate
    {
        get => _dueDate;
        set => SetField(ref _dueDate, value);
    }

    public TaskLookupItemDto? SelectedAssignee
    {
        get => _selectedAssignee;
        set
        {
            if (SetField(ref _selectedAssignee, value))
                (CreateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
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

            (CreateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public TaskLookupItemDto? SelectedStatus
    {
        get => _selectedStatus;
        set
        {
            if (SetField(ref _selectedStatus, value))
                (CreateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public TaskLookupItemDto? SelectedBucket
    {
        get => _selectedBucket;
        set
        {
            if (SetField(ref _selectedBucket, value))
                (CreateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetField(ref _validationMessage, value);
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (SetField(ref _isSaving, value))
                (CreateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public bool CanEditAssignee => _canSelectAssignee;

    public int? CreatedTaskId { get; private set; }

    public ICommand CreateCommand { get; }
    public ICommand CancelCommand { get; }

    public event Action<bool>? RequestClose;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _canSelectAssignee = _authorization is not null
            && await _authorization.CanCurrentUserAccessFeatureAsync(
                AppFeatureCodes.TaskWorkbenchViewOtherUsersTasks, ct).ConfigureAwait(true);
        OnPropertyChanged(nameof(CanEditAssignee));

        await ProjectSelector.InitializeAsync(ct).ConfigureAwait(true);

        var options = await _workbench.GetTaskCreationOptionsAsync(ct).ConfigureAwait(true);
        ReplaceLookup(Users, options.Users);
        ReplaceLookup(TaskTypes, options.TaskTypes);
        ReplaceLookup(Statuses, options.Statuses);
        ReplaceLookup(Buckets, options.Buckets);
        ApplyDefaults();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ProjectSelector.Dispose();
    }

    private void ApplyDefaults()
    {
        if (!_canSelectAssignee && _currentUser.UserId is int lockedUid)
            SelectedAssignee = Users.FirstOrDefault(u => u.Id == lockedUid);
        else if (_currentUser.UserId is int uid)
            SelectedAssignee = Users.FirstOrDefault(u => u.Id == uid);

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

        (CreateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private bool CanCreate() =>
        !IsSaving
        && SelectedProjectId is int
        && !string.IsNullOrWhiteSpace(Title)
        && SelectedAssignee is not null
        && SelectedTaskType is not null
        && SelectedStatus is not null
        && SelectedBucket is not null
        && _currentUser.UserId is int actorId
        && ValidateAssignee(actorId);

    private bool ValidateAssignee(int actorId)
    {
        if (SelectedAssignee is null)
            return false;

        if (!_canSelectAssignee)
            return SelectedAssignee.Id == actorId;

        return true;
    }

    private async Task CreateAsync()
    {
        if (_currentUser.UserId is not int actorId)
            return;

        if (_dialogProjectContext.CurrentProject?.ProjectId is not int projectId)
        {
            ValidationMessage = "יש לבחור פרויקט לפני יצירת משימה.";
            MessageBox.Show(
                ValidationMessage,
                "יצירת משימה",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!ValidateAssignee(actorId))
        {
            ValidationMessage = "אין הרשאה ליצור משימה למשתמש אחר.";
            MessageBox.Show(
                ValidationMessage,
                "יצירת משימה",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        IsSaving = true;
        ValidationMessage = string.Empty;
        try
        {
            var request = new CreateTaskRequest(
                projectId,
                SelectedAssignee!.Id,
                SelectedTaskType!.Id,
                SelectedStatus!.Id,
                Title.Trim(),
                SelectedBucket!.Id,
                Body: string.IsNullOrWhiteSpace(Body) ? null : Body.Trim(),
                DueDate: DueDate);

            var result = await _workbench.CreateTaskAsync(request, actorId).ConfigureAwait(true);
            if (!result.Succeeded)
            {
                ValidationMessage = result.Message;
                MessageBox.Show(result.Message, "יצירת משימה", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            CreatedTaskId = result.TaskId;
            RequestClose?.Invoke(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            ValidationMessage = ex.Message;
            MessageBox.Show(ex.Message, "יצירת משימה", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private static void ReplaceLookup(ObservableCollection<TaskLookupItemDto> target, IReadOnlyList<TaskLookupItemDto> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }
}
