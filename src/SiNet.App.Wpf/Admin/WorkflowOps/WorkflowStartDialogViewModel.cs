using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Identity;
using SiNet.Application.Projects;
using SiNet.Application.Workflow;

namespace SiNet.App.Wpf.Admin.WorkflowOps;

/// <summary>
/// Manual workflow start — see docs/WORKFLOW_OPS_DASHBOARD.md §6.
/// </summary>
public sealed class WorkflowStartDialogViewModel : ObservableObject
{
    private readonly IProjectQueryService _projects;
    private readonly IProjectWorkflowPolicyService _policy;
    private readonly IWorkflowCommandService _commands;
    private readonly IWorkflowQueryService _query;
    private readonly ICurrentUserContext? _currentUser;
    private readonly IAuthorizationQueryService? _authorization;

    private bool _isBusy;
    private bool _canStartFeature;
    private string _projectSearchText = string.Empty;
    private string _statusMessage = string.Empty;
    private ProjectSummaryDto? _selectedProject;
    private WorkflowDefinitionDto? _selectedDefinition;
    private bool _dialogResult;

    public WorkflowStartDialogViewModel(
        IProjectQueryService projects,
        IProjectWorkflowPolicyService policy,
        IWorkflowCommandService commands,
        IWorkflowQueryService query,
        ICurrentUserContext? currentUser = null,
        IAuthorizationQueryService? authorization = null)
    {
        _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _currentUser = currentUser;
        _authorization = authorization;

        Projects = [];
        Definitions = [];
        SearchProjectsCommand = new AsyncRelayCommand(SearchProjectsAsync, () => !IsBusy);
        StartCommand = new AsyncRelayCommand(StartAsync, () => !IsBusy && CanStart);
        CancelDialogCommand = new RelayCommand(
            _ =>
            {
                DialogResult = false;
                RequestClose?.Invoke(this, EventArgs.Empty);
            },
            _ => true);
    }

    public ObservableCollection<ProjectSummaryDto> Projects { get; }
    public ObservableCollection<WorkflowDefinitionDto> Definitions { get; }

    public ICommand SearchProjectsCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand CancelDialogCommand { get; }

    public event EventHandler? RequestClose;

    public string ProjectSearchText
    {
        get => _projectSearchText;
        set => SetField(ref _projectSearchText, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public ProjectSummaryDto? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (!SetField(ref _selectedProject, value))
                return;
            _ = LoadDefinitionsAsync();
            RaiseCanExecutes();
        }
    }

    public WorkflowDefinitionDto? SelectedDefinition
    {
        get => _selectedDefinition;
        set
        {
            if (!SetField(ref _selectedDefinition, value))
                return;
            RaiseCanExecutes();
        }
    }

    public bool DialogResult
    {
        get => _dialogResult;
        private set => SetField(ref _dialogResult, value);
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

    public bool CanStart =>
        _canStartFeature
        && SelectedProject is not null
        && SelectedDefinition is not null
        && ResolveUserId() is not null;

    public int? StartedInstanceId { get; private set; }

    public async Task LoadAsync()
    {
        if (_authorization is not null)
        {
            _canStartFeature = await _authorization
                .CanCurrentUserAccessFeatureAsync(AppFeatureCodes.WorkflowOpsStart, CancellationToken.None)
                .ConfigureAwait(true);
        }
        else
        {
            _canStartFeature = true;
        }

        await SearchProjectsAsync().ConfigureAwait(true);
        RaiseCanExecutes();
    }

    private async Task SearchProjectsAsync()
    {
        IsBusy = true;
        try
        {
            var results = await _projects.SearchProjectsAsync(
                    new ProjectSearchQuery(SearchText: ProjectSearchText, MaxResults: 80),
                    CancellationToken.None)
                .ConfigureAwait(true);
            Projects.Clear();
            foreach (var project in results)
                Projects.Add(project);
            if (SelectedProject is not null
                && Projects.All(p => p.ProjectId != SelectedProject.ProjectId))
            {
                SelectedProject = null;
            }

            StatusMessage = Projects.Count == 0 ? "לא נמצאו פרויקטים." : string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"חיפוש נכשל: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadDefinitionsAsync()
    {
        Definitions.Clear();
        SelectedDefinition = null;
        if (SelectedProject is null)
            return;

        try
        {
            var allowed = await _policy.GetAllowedWorkflowsAsync(
                    SelectedProject.ProjectId,
                    CancellationToken.None)
                .ConfigureAwait(true);

            var usedPolicyFallback = false;
            IReadOnlyList<WorkflowDefinitionDto> list = allowed;
            if (list.Count == 0)
            {
                // Outsourcing and other unmapped definitions stay startable from ops
                // until JobType policy is configured (see docs/WORKFLOW_OUTSOURCING.md).
                list = await _query.GetActiveDefinitionsAsync(CancellationToken.None)
                    .ConfigureAwait(true);
                usedPolicyFallback = list.Count > 0;
            }

            foreach (var def in list)
                Definitions.Add(def);
            SelectedDefinition = Definitions.FirstOrDefault();
            StatusMessage = Definitions.Count == 0
                ? "אין תהליכים פעילים במערכת."
                : usedPolicyFallback
                    ? "אין מיפוי סוג↔תהליך לפרויקט — מוצגות כל התבניות הפעילות."
                    : string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"טעינת תבניות נכשלה: {ex.Message}";
        }
        finally
        {
            RaiseCanExecutes();
        }
    }

    private async Task StartAsync()
    {
        if (SelectedProject is null || SelectedDefinition is null || ResolveUserId() is not { } userId)
            return;

        var confirm = MessageBox.Show(
            $"להפעיל את «{SelectedDefinition.Name}» עבור פרויקט {SelectedProject.ProjectNumber}?",
            "הפעלת תהליך",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        try
        {
            var result = await _commands.StartAsync(
                    new StartWorkflowCommand(
                        SelectedDefinition.Id,
                        SelectedProject.ProjectId,
                        WorkflowTriggerTypeDto.Manual,
                        TriggerEntityId: null,
                        UserId: userId,
                        Notes: null,
                        IsProjectBound: true),
                    CancellationToken.None)
                .ConfigureAwait(true);
            StartedInstanceId = result.Instance.Id;
            StatusMessage = $"הופעל מופע #{result.Instance.Id}.";
            _dialogResult = true;
            OnPropertyChanged(nameof(DialogResult));
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"הפעלה נכשלה: {ex.Message}",
                "הפעלת תהליך",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusMessage = $"הפעלה נכשלה: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private int? ResolveUserId() => _currentUser?.UserId is { } id && id > 0 ? id : null;

    private void RaiseCanExecutes()
    {
        (SearchProjectsCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (StartCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanStart));
    }
}
