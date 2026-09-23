using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Identity;
using SiNet.Application.Workflow;

namespace SiNet.App.Wpf.Admin.WorkflowOps;

public sealed class AdoptExistingWorkflowViewModel : ObservableObject
{
    private readonly IWorkflowAdoptionService _adoption;
    private readonly ICurrentUserContext? _currentUser;

    private int _projectId;
    private bool _isBusy;
    private string _statusMessage = string.Empty;
    private string _previewText = string.Empty;
    private bool _canCommit;
    private WorkflowAdoptionWorkflowOption? _selectedWorkflow;
    private WorkflowAdoptionJobTypeOption? _selectedJobType;
    private WorkflowAdoptionStageOption? _selectedStage;
    private WorkflowAdoptionUserOption? _selectedResponsible;

    public AdoptExistingWorkflowViewModel(
        IWorkflowAdoptionService adoption,
        ICurrentUserContext? currentUser = null)
    {
        _adoption = adoption ?? throw new ArgumentNullException(nameof(adoption));
        _currentUser = currentUser;
        Workflows = [];
        JobTypes = [];
        Stages = [];
        Reports = [];
        ResponsibleUsers = [];
        PreviewCommand = new AsyncRelayCommand(PreviewAsync, () => !IsBusy && SelectedStage is not null);
        CommitCommand = new AsyncRelayCommand(CommitAsync, () => !IsBusy && _canCommit);
        CloseCommand = new RelayCommand(_ => RequestClose?.Invoke(this, false));
    }

    public event EventHandler<bool>? RequestClose;

    public ObservableCollection<WorkflowAdoptionWorkflowOption> Workflows { get; }
    public ObservableCollection<WorkflowAdoptionJobTypeOption> JobTypes { get; }
    public ObservableCollection<WorkflowAdoptionStageOption> Stages { get; }
    public ObservableCollection<AdoptionReportRowVm> Reports { get; }
    public ObservableCollection<WorkflowAdoptionUserOption> ResponsibleUsers { get; }

    public ICommand PreviewCommand { get; }
    public ICommand CommitCommand { get; }
    public ICommand CloseCommand { get; }

    public int ProjectId
    {
        get => _projectId;
        set => SetField(ref _projectId, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value))
                return;
            (PreviewCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (CommitCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string PreviewText
    {
        get => _previewText;
        private set => SetField(ref _previewText, value);
    }

    public WorkflowAdoptionWorkflowOption? SelectedWorkflow
    {
        get => _selectedWorkflow;
        set
        {
            if (!SetField(ref _selectedWorkflow, value))
                return;
            ReloadJobTypes();
        }
    }

    public WorkflowAdoptionJobTypeOption? SelectedJobType
    {
        get => _selectedJobType;
        set
        {
            if (!SetField(ref _selectedJobType, value))
                return;
            ReloadStages();
        }
    }

    public WorkflowAdoptionStageOption? SelectedStage
    {
        get => _selectedStage;
        set
        {
            if (!SetField(ref _selectedStage, value))
                return;
            ReloadResponsibleUsers();
            _canCommit = false;
            (PreviewCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (CommitCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public WorkflowAdoptionUserOption? SelectedResponsible
    {
        get => _selectedResponsible;
        set => SetField(ref _selectedResponsible, value);
    }

    public async Task LoadAsync()
    {
        if (ProjectId <= 0)
        {
            StatusMessage = "לא נבחר פרויקט.";
            return;
        }

        IsBusy = true;
        try
        {
            var options = await _adoption.GetOptionsAsync(ProjectId, CancellationToken.None).ConfigureAwait(true);
            Workflows.Clear();
            foreach (var workflow in options.Workflows)
                Workflows.Add(workflow);
            Reports.Clear();
            foreach (var report in options.ExistingReports)
                Reports.Add(new AdoptionReportRowVm(report));
            SelectedWorkflow = Workflows.FirstOrDefault();
            StatusMessage = options.Message ?? $"פרויקט {options.ProjectTitle}";
            PreviewText = string.Empty;
            _canCommit = false;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PreviewAsync()
    {
        IsBusy = true;
        try
        {
            var preview = await _adoption.PreviewAsync(BuildRequest(), CancellationToken.None).ConfigureAwait(true);
            PreviewText = FormatPreview(preview);
            StatusMessage = preview.Message;
            _canCommit = preview.CanCommit;
        }
        catch (Exception ex)
        {
            PreviewText = string.Empty;
            StatusMessage = ex.Message;
            _canCommit = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CommitAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _adoption.CommitAsync(BuildRequest(), CancellationToken.None).ConfigureAwait(true);
            StatusMessage = result.Message;
            _canCommit = false;
            if (result.Disposition == WorkflowAdoptionDisposition.Committed)
            {
                MessageBox.Show(result.Message, "הטמעת תהליך קיים", MessageBoxButton.OK, MessageBoxImage.Information);
                RequestClose?.Invoke(this, true);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private WorkflowAdoptionRequest BuildRequest()
    {
        var reports = new List<WorkflowAdoptionReportIntent>();
        foreach (var row in Reports)
        {
            if (row.Mode is WorkflowAdoptionReportMode mode)
                reports.Add(new WorkflowAdoptionReportIntent(row.ReportId, mode));
        }
        return new WorkflowAdoptionRequest(
            ProjectId,
            SelectedWorkflow?.DefinitionId ?? 0,
            SelectedJobType?.JobTypeId ?? 0,
            SelectedStage?.Code ?? string.Empty,
            _currentUser?.UserId ?? 0,
            Notes: null,
            ResponsibleUserId: SelectedResponsible is { UserId: > 0 } user ? user.UserId : null,
            Reports: reports);
    }

    private void ReloadJobTypes()
    {
        JobTypes.Clear();
        if (SelectedWorkflow is not null)
        {
            foreach (var jobType in SelectedWorkflow.JobTypes)
                JobTypes.Add(jobType);
        }

        SelectedJobType = JobTypes.FirstOrDefault();
    }

    private void ReloadStages()
    {
        Stages.Clear();
        if (SelectedJobType is not null)
        {
            foreach (var stage in SelectedJobType.Stages)
                Stages.Add(stage);
        }

        SelectedStage = Stages.FirstOrDefault();
    }

    private void ReloadResponsibleUsers()
    {
        ResponsibleUsers.Clear();
        var fallback = new WorkflowAdoptionUserOption(0, "ברירת מחדל של הקבוצה");
        ResponsibleUsers.Add(fallback);
        if (SelectedStage is not null)
        {
            foreach (var user in SelectedStage.ResponsibleCandidates)
                ResponsibleUsers.Add(user);
        }

        SelectedResponsible = fallback;
    }

    private static string FormatPreview(WorkflowAdoptionPreview preview)
    {
        var text = new StringBuilder();
        text.AppendLine($"Project: {preview.ProjectNumber:0.###} — {preview.ProjectTitle}");
        text.AppendLine($"Workflow: {preview.WorkflowName}");
        text.AppendLine($"JobType: {preview.JobTypeTitle}");
        text.AppendLine();
        text.AppendLine("כבר קרה לפני SiNet");
        foreach (var stage in preview.HistoricalStages)
            text.AppendLine($"✓ {stage.Name ?? stage.Code}");
        foreach (var report in preview.Reports.Where(r => r.RequestedMode is not null))
            text.AppendLine($"✓ Report {report.ReportNumber} — {report.Note}");
        text.AppendLine();
        text.AppendLine($"SiNet מתחיל כאן: {preview.CurrentStageName} ({preview.CurrentStageCode})");
        text.AppendLine("ייווצר:");
        foreach (var task in preview.WillCreateTasks)
            text.AppendLine($"✓ {task.TaskTypeName ?? task.TaskTypeCode}");
        text.AppendLine("לא ירוץ:");
        foreach (var skipped in preview.WillNotCreate)
            text.AppendLine($"✗ {skipped}");
        foreach (var action in preview.WillNotRunActions)
            text.AppendLine($"✗ {action.ActionType} ({action.FromStageCode} → {action.ToStageCode})");
        if (preview.Warnings.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("אזהרות:");
            foreach (var warning in preview.Warnings)
                text.AppendLine("! " + warning);
        }

        return text.ToString();
    }
}

public sealed class AdoptionReportRowVm : ObservableObject
{
    private WorkflowAdoptionReportMode? _mode;

    public AdoptionReportRowVm(WorkflowAdoptionReportPreview report)
    {
        ReportId = report.ReportId;
        ReportNumber = report.ReportNumber;
        Summary = $"Report {report.ReportNumber}"
                  + (report.IsLockedAfterSend ? " · נעול" : " · פתוח");
    }

    public int ReportId { get; }
    public int ReportNumber { get; }
    public string Summary { get; }
    public static IReadOnlyList<string> Choices { get; } = ["לא נבחר", "היסטורי", "פעיל"];

    public WorkflowAdoptionReportMode? Mode => _mode;

    public string Choice
    {
        get => _mode switch
        {
            WorkflowAdoptionReportMode.Historical => "היסטורי",
            WorkflowAdoptionReportMode.Active => "פעיל",
            _ => "לא נבחר",
        };
        set
        {
            var next = value switch
            {
                "היסטורי" => WorkflowAdoptionReportMode.Historical,
                "פעיל" => (WorkflowAdoptionReportMode?)WorkflowAdoptionReportMode.Active,
                _ => null,
            };
            if (_mode == next)
                return;
            _mode = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Mode));
        }
    }
}
