using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Identity;
using SiNet.Application.Workflow;
using SiNet.Domain.Workflow;

namespace SiNet.App.Wpf.Admin.WorkflowOps;

/// <summary>
/// Runtime control for a single workflow instance — see docs/WORKFLOW_OPS_DASHBOARD.md §5.
/// </summary>
public sealed class WorkflowInstanceDetailViewModel : ObservableObject
{
    private readonly IWorkflowQueryService _query;
    private readonly IWorkflowCommandService _commands;
    private readonly ICurrentUserContext? _currentUser;
    private readonly IAuthorizationQueryService? _authorization;
    private readonly int _instanceId;
    private readonly Action? _onChanged;

    private bool _isBusy;
    private bool _canAdvanceFeature;
    private bool _canCancelFeature;
    private string _headerText = "טוען…";
    private string _transitionsText = string.Empty;
    private string _progressText = string.Empty;
    private string _statusMessage = string.Empty;
    private string? _advanceNotes;
    private WorkflowStatus _status = WorkflowStatus.Draft;
    private WorkflowStageDefinitionDto? _selectedNextStage;

    public WorkflowInstanceDetailViewModel(
        int instanceId,
        IWorkflowQueryService query,
        IWorkflowCommandService commands,
        ICurrentUserContext? currentUser = null,
        IAuthorizationQueryService? authorization = null,
        Action? onChanged = null)
    {
        if (instanceId <= 0)
            throw new ArgumentOutOfRangeException(nameof(instanceId));
        _instanceId = instanceId;
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _currentUser = currentUser;
        _authorization = authorization;
        _onChanged = onChanged;

        NextStages = [];
        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        AdvanceCommand = new AsyncRelayCommand(AdvanceAsync, () => !IsBusy && CanAdvance);
        PauseCommand = new AsyncRelayCommand(PauseAsync, () => !IsBusy && CanPause);
        ResumeCommand = new AsyncRelayCommand(ResumeAsync, () => !IsBusy && CanResume);
        CompleteCommand = new AsyncRelayCommand(CompleteAsync, () => !IsBusy && CanComplete);
        CancelCommand = new AsyncRelayCommand(CancelAsync, () => !IsBusy && CanCancel);
    }

    public ObservableCollection<WorkflowStageDefinitionDto> NextStages { get; }

    public ICommand RefreshCommand { get; }
    public ICommand AdvanceCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand ResumeCommand { get; }
    public ICommand CompleteCommand { get; }
    public ICommand CancelCommand { get; }

    public string HeaderText
    {
        get => _headerText;
        private set => SetField(ref _headerText, value);
    }

    public string TransitionsText
    {
        get => _transitionsText;
        private set => SetField(ref _transitionsText, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetField(ref _progressText, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string? AdvanceNotes
    {
        get => _advanceNotes;
        set => SetField(ref _advanceNotes, value);
    }

    public WorkflowStageDefinitionDto? SelectedNextStage
    {
        get => _selectedNextStage;
        set
        {
            if (!SetField(ref _selectedNextStage, value))
                return;
            RaiseCanExecutes();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value))
                return;
            RaiseCanExecutes();
        }
    }

    public bool CanAdvance =>
        _canAdvanceFeature
        && _status == WorkflowStatus.Active
        && SelectedNextStage is not null
        && ResolveUserId() is not null;

    public bool CanPause =>
        _canAdvanceFeature
        && _status == WorkflowStatus.Active
        && ResolveUserId() is not null;

    public bool CanResume =>
        _canAdvanceFeature
        && _status == WorkflowStatus.Paused
        && ResolveUserId() is not null;

    public bool CanComplete =>
        _canCancelFeature
        && _status is WorkflowStatus.Active or WorkflowStatus.Paused
        && ResolveUserId() is not null;

    public bool CanCancel =>
        _canCancelFeature
        && _status is WorkflowStatus.Active or WorkflowStatus.Paused or WorkflowStatus.Draft
        && ResolveUserId() is not null;

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await RefreshPermissionsAsync().ConfigureAwait(true);

            var detail = await _query.GetInstanceDetailAsync(_instanceId, CancellationToken.None)
                .ConfigureAwait(true);
            if (detail is null)
            {
                HeaderText = $"מופע #{_instanceId} לא נמצא";
                TransitionsText = string.Empty;
                ProgressText = string.Empty;
                StatusMessage = "המופע אינו קיים.";
                return;
            }

            _status = detail.Status;
            var project = detail.Project is null
                ? "—"
                : $"{detail.Project.Number?.ToString("0.###") ?? "—"} · {detail.Project.Title ?? "—"}";
            HeaderText =
                $"#{detail.Id} · {detail.WorkflowDefinition?.Name ?? "תהליך"} · {project}"
                + $" · {StatusLabel(detail.Status)} · שלב {detail.CurrentStage?.Name ?? "—"}";

            TransitionsText = string.Join(
                Environment.NewLine,
                detail.StageTransitions
                    .OrderBy(t => t.TransitionedAtUtc)
                    .Select(t =>
                    {
                        var stage = t.ToStage?.Name ?? $"#{t.ToStageId}";
                        var when = t.TransitionedAtUtc.ToLocalTime().ToString("dd/MM HH:mm");
                        var by = t.TransitionedByUser?.PersonName ?? "—";
                        return $"{when} → {stage} ({by})";
                    }));

            try
            {
                var progress = await _query.GetStageTaskProgressAsync(_instanceId, CancellationToken.None)
                    .ConfigureAwait(true);
                ProgressText =
                    $"משימות: נדרשות {progress.TotalRequired} (הושלמו {progress.CompletedRequired}), "
                    + $"אופציונליות {progress.TotalOptional}, "
                    + $"נוצרו {progress.TotalCreated}, נסגרו {progress.TotalClosed}";
            }
            catch (Exception ex)
            {
                ProgressText = $"התקדמות משימות לא זמינה: {ex.Message}";
            }

            var next = await _query.GetAllowedNextStagesAsync(_instanceId, CancellationToken.None)
                .ConfigureAwait(true);
            NextStages.Clear();
            foreach (var stage in next)
                NextStages.Add(stage);
            SelectedNextStage = NextStages.FirstOrDefault();
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"שגיאה בטעינה: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanAdvance));
            OnPropertyChanged(nameof(CanPause));
            OnPropertyChanged(nameof(CanResume));
            OnPropertyChanged(nameof(CanComplete));
            OnPropertyChanged(nameof(CanCancel));
        }
    }

    private async Task RefreshPermissionsAsync()
    {
        if (_authorization is null)
        {
            _canAdvanceFeature = true;
            _canCancelFeature = true;
            return;
        }

        _canAdvanceFeature = await _authorization
            .CanCurrentUserAccessFeatureAsync(AppFeatureCodes.WorkflowOpsAdvance, CancellationToken.None)
            .ConfigureAwait(true);
        _canCancelFeature = await _authorization
            .CanCurrentUserAccessFeatureAsync(AppFeatureCodes.WorkflowOpsCancel, CancellationToken.None)
            .ConfigureAwait(true);
    }

    private async Task AdvanceAsync()
    {
        if (SelectedNextStage is null || ResolveUserId() is not { } userId)
            return;

        IsBusy = true;
        try
        {
            await _commands.AdvanceAsync(
                    new AdvanceWorkflowCommand(_instanceId, SelectedNextStage.Id, userId, AdvanceNotes),
                    CancellationToken.None)
                .ConfigureAwait(true);
            StatusMessage = "השלב קודם בהצלחה.";
            _onChanged?.Invoke();
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"קידום נכשל: {ex.Message}",
                "מופע תהליך",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusMessage = $"קידום נכשל: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PauseAsync()
    {
        if (ResolveUserId() is not { } userId)
            return;

        IsBusy = true;
        try
        {
            await _commands.PauseAsync(new PauseWorkflowCommand(_instanceId, userId, null), CancellationToken.None)
                .ConfigureAwait(true);
            _onChanged?.Invoke();
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"השהיה נכשלה: {ex.Message}", "מופע תהליך", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ResumeAsync()
    {
        if (ResolveUserId() is not { } userId)
            return;

        IsBusy = true;
        try
        {
            await _commands.ResumeAsync(new ResumeWorkflowCommand(_instanceId, userId, null), CancellationToken.None)
                .ConfigureAwait(true);
            _onChanged?.Invoke();
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"חידוש נכשל: {ex.Message}", "מופע תהליך", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CompleteAsync()
    {
        if (ResolveUserId() is not { } userId)
            return;

        var confirm = MessageBox.Show(
            $"לסיים את מופע #{_instanceId}?",
            "סיום תהליך",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        try
        {
            await _commands.CompleteInstanceAsync(
                    new CompleteWorkflowCommand(_instanceId, userId, null),
                    CancellationToken.None)
                .ConfigureAwait(true);
            _onChanged?.Invoke();
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"סיום נכשל: {ex.Message}", "מופע תהליך", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CancelAsync()
    {
        if (ResolveUserId() is not { } userId)
            return;

        var confirm = MessageBox.Show(
            $"לבטל את מופע #{_instanceId}? פעולה זו אינה ניתנת לביטול.",
            "ביטול תהליך",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        try
        {
            await _commands.CancelAsync(new CancelWorkflowCommand(_instanceId, userId, null), CancellationToken.None)
                .ConfigureAwait(true);
            _onChanged?.Invoke();
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"ביטול נכשל: {ex.Message}", "מופע תהליך", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private int? ResolveUserId() => _currentUser?.UserId is { } id && id > 0 ? id : null;

    private void RaiseCanExecutes()
    {
        (RefreshCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (AdvanceCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (PauseCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ResumeCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (CompleteCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (CancelCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanAdvance));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanComplete));
        OnPropertyChanged(nameof(CanCancel));
    }

    private static string StatusLabel(WorkflowStatus status) => status switch
    {
        WorkflowStatus.Active => "פעיל",
        WorkflowStatus.Paused => "מושהה",
        WorkflowStatus.Completed => "הושלם",
        WorkflowStatus.Cancelled => "בוטל",
        WorkflowStatus.Draft => "טיוטה",
        _ => status.ToString(),
    };
}
