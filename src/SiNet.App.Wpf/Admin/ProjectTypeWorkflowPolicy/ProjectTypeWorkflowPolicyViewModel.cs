using System.Collections.ObjectModel;
using System.Windows;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Workflow;

namespace SiNet.App.Wpf.Admin.ProjectTypeWorkflowPolicy;

public sealed class ProjectTypeWorkflowPolicyViewModel : ObservableObject
{
    private readonly IProjectTypeWorkflowPolicyAdminService _admin;

    private ProjectTypeWorkflowJobTypeDto? _selectedJobType;
    private ProjectTypeWorkflowPolicyMappingRowVm? _selectedMapping;
    private WorkflowDefinitionOptionDto? _selectedDefinitionToAdd;
    private bool _addAsDefault = true;
    private int _addSortOrder = 1;
    private bool _isBusy;
    private string _statusMessage = string.Empty;
    private string _unmappedSummary = string.Empty;

    private IReadOnlyList<ProjectTypeWorkflowMappingDto> _allMappings = [];

    public ProjectTypeWorkflowPolicyViewModel(IProjectTypeWorkflowPolicyAdminService admin)
    {
        _admin = admin ?? throw new ArgumentNullException(nameof(admin));

        JobTypes = [];
        Mappings = [];
        ActiveWorkflowDefinitions = [];

        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        AddMappingCommand = new AsyncRelayCommand(AddMappingAsync, () => !IsBusy && CanAddMapping);
        SetDefaultCommand = new AsyncRelayCommand(SetDefaultAsync, () => !IsBusy && SelectedMapping is not null);
        ToggleEnabledCommand = new AsyncRelayCommand(ToggleEnabledAsync, () => !IsBusy && SelectedMapping is not null);
        DeleteMappingCommand = new AsyncRelayCommand(DeleteMappingAsync, () => !IsBusy && SelectedMapping is not null);
    }

    public ObservableCollection<ProjectTypeWorkflowJobTypeDto> JobTypes { get; }
    public ObservableCollection<ProjectTypeWorkflowPolicyMappingRowVm> Mappings { get; }
    public ObservableCollection<WorkflowDefinitionOptionDto> ActiveWorkflowDefinitions { get; }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AddMappingCommand { get; }
    public AsyncRelayCommand SetDefaultCommand { get; }
    public AsyncRelayCommand ToggleEnabledCommand { get; }
    public AsyncRelayCommand DeleteMappingCommand { get; }

    public ProjectTypeWorkflowJobTypeDto? SelectedJobType
    {
        get => _selectedJobType;
        set
        {
            if (!SetField(ref _selectedJobType, value))
                return;
            RebuildMappingsForSelection();
            RaiseCanExecutes();
        }
    }

    public ProjectTypeWorkflowPolicyMappingRowVm? SelectedMapping
    {
        get => _selectedMapping;
        set
        {
            if (!SetField(ref _selectedMapping, value))
                return;
            RaiseCanExecutes();
        }
    }

    public WorkflowDefinitionOptionDto? SelectedDefinitionToAdd
    {
        get => _selectedDefinitionToAdd;
        set
        {
            if (!SetField(ref _selectedDefinitionToAdd, value))
                return;
            RaiseCanExecutes();
        }
    }

    public bool AddAsDefault
    {
        get => _addAsDefault;
        set => SetField(ref _addAsDefault, value);
    }

    public int AddSortOrder
    {
        get => _addSortOrder;
        set => SetField(ref _addSortOrder, value);
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

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string UnmappedSummary
    {
        get => _unmappedSummary;
        private set => SetField(ref _unmappedSummary, value);
    }

    private bool CanAddMapping =>
        SelectedJobType is not null && SelectedDefinitionToAdd is { IsActive: true };

    public async Task LoadAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        StatusMessage = "טוען…";
        try
        {
            var snapshot = await _admin.GetSnapshotAsync().ConfigureAwait(true);
            var selectedId = SelectedJobType?.Id;

            JobTypes.Clear();
            foreach (var jt in snapshot.JobTypes)
                JobTypes.Add(jt);

            ActiveWorkflowDefinitions.Clear();
            foreach (var d in snapshot.WorkflowDefinitions.Where(x => x.IsActive))
                ActiveWorkflowDefinitions.Add(d);

            _allMappings = snapshot.Mappings;

            SelectedJobType = selectedId is int id
                ? JobTypes.FirstOrDefault(j => j.Id == id) ?? JobTypes.FirstOrDefault()
                : JobTypes.FirstOrDefault();

            RebuildMappingsForSelection();
            UpdateUnmappedSummary();
            StatusMessage = $"נטענו {snapshot.Mappings.Count} מיפויים · {JobTypes.Count} סוגי פרויקט";
        }
        catch (Exception ex)
        {
            StatusMessage = "שגיאה בטעינה: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RebuildMappingsForSelection()
    {
        Mappings.Clear();
        SelectedMapping = null;
        if (SelectedJobType is null)
            return;

        foreach (var m in _allMappings.Where(x => x.ProjectTypeId == SelectedJobType.Id))
            Mappings.Add(new ProjectTypeWorkflowPolicyMappingRowVm(m));

        SelectedMapping = Mappings.FirstOrDefault();
    }

    private void UpdateUnmappedSummary()
    {
        var mappedIds = _allMappings
            .Where(m => m.IsEnabled)
            .Select(m => m.ProjectTypeId)
            .ToHashSet();
        var missing = JobTypes.Where(j => !mappedIds.Contains(j.Id)).Select(j => j.Title).ToList();
        UnmappedSummary = missing.Count == 0
            ? "כל סוגי הפרויקט ממופים (מיפוי פעיל אחד לפחות)."
            : "חסר מיפוי פעיל: " + string.Join(", ", missing);
    }

    private async Task AddMappingAsync()
    {
        if (SelectedJobType is null || SelectedDefinitionToAdd is null)
            return;

        IsBusy = true;
        ProjectTypeWorkflowWriteResult result;
        try
        {
            result = await _admin.UpsertMappingAsync(
                    SelectedJobType.Id,
                    SelectedDefinitionToAdd.Id,
                    AddAsDefault,
                    isEnabled: true,
                    AddSortOrder)
                .ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }

        if (!result.Success)
        {
            StatusMessage = result.Error ?? "שמירה נכשלה.";
            return;
        }

        StatusMessage = "המיפוי נשמר.";
        await LoadAsync().ConfigureAwait(true);
    }

    private async Task SetDefaultAsync()
    {
        if (SelectedMapping is null)
            return;

        IsBusy = true;
        ProjectTypeWorkflowWriteResult result;
        try
        {
            result = await _admin.SetDefaultAsync(SelectedMapping.Id).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }

        if (!result.Success)
        {
            StatusMessage = result.Error ?? "הפעולה נכשלה.";
            return;
        }

        StatusMessage = "עודכן כברירת מחדל.";
        await LoadAsync().ConfigureAwait(true);
    }

    private async Task ToggleEnabledAsync()
    {
        if (SelectedMapping is null)
            return;

        var turningOff = SelectedMapping.IsEnabled;
        IsBusy = true;
        ProjectTypeWorkflowWriteResult result;
        try
        {
            result = await _admin
                .SetEnabledAsync(SelectedMapping.Id, !SelectedMapping.IsEnabled)
                .ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }

        if (!result.Success)
        {
            StatusMessage = result.Error ?? "הפעולה נכשלה.";
            return;
        }

        StatusMessage = turningOff ? "המיפוי כובה." : "המיפוי הופעל.";
        await LoadAsync().ConfigureAwait(true);
    }

    private async Task DeleteMappingAsync()
    {
        if (SelectedMapping is null)
            return;

        var confirm = MessageBox.Show(
            $"למחוק מיפוי «{SelectedMapping.DisplayWorkflow}» מסוג «{SelectedMapping.ProjectTypeTitle}»?",
            "מדיניות סוג↔תהליך",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        ProjectTypeWorkflowWriteResult result;
        try
        {
            result = await _admin.DeleteMappingAsync(SelectedMapping.Id).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }

        if (!result.Success)
        {
            StatusMessage = result.Error ?? "מחיקה נכשלה.";
            return;
        }

        StatusMessage = "המיפוי נמחק.";
        await LoadAsync().ConfigureAwait(true);
    }

    private void RaiseCanExecutes()
    {
        RefreshCommand.RaiseCanExecuteChanged();
        AddMappingCommand.RaiseCanExecuteChanged();
        SetDefaultCommand.RaiseCanExecuteChanged();
        ToggleEnabledCommand.RaiseCanExecuteChanged();
        DeleteMappingCommand.RaiseCanExecuteChanged();
    }
}
