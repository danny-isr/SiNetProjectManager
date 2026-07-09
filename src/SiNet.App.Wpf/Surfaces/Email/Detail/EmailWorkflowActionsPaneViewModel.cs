using System.Collections.ObjectModel;
using System.Windows.Input;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Inbox;
using SiNet.Application.Email.Detail;

namespace SiNet.App.Wpf.Surfaces.Email.Detail;

public sealed class EmailWorkflowActionsPaneViewModel : ObservableObject
{
    private string? _projectDisplay;
    private string? _workflowFamilyDisplay;
    private string? _confidenceDisplay;
    private int _activeWorkflowCount;
    private EmailSuggestedActionDto? _selectedAction;
    private bool _isLoading;
    private string _statusMessage = string.Empty;

    public EmailWorkflowActionsPaneViewModel(Func<Task> executeSelectedActionAsync)
    {
        SuggestedActions = [];
        ExecuteActionCommand = new AsyncRelayCommand(
            executeSelectedActionAsync,
            () => SelectedAction is not null && !IsLoading);
    }

    public ObservableCollection<EmailSuggestedActionDto> SuggestedActions { get; }

    public bool HasContext => !string.IsNullOrWhiteSpace(ProjectDisplay);

    public string? ProjectDisplay
    {
        get => _projectDisplay;
        set
        {
            if (SetField(ref _projectDisplay, value))
            {
                OnPropertyChanged(nameof(HasContext));
            }
        }
    }

    public string? WorkflowFamilyDisplay
    {
        get => _workflowFamilyDisplay;
        set => SetField(ref _workflowFamilyDisplay, value);
    }

    public string? ConfidenceDisplay
    {
        get => _confidenceDisplay;
        set => SetField(ref _confidenceDisplay, value);
    }

    public int ActiveWorkflowCount
    {
        get => _activeWorkflowCount;
        set => SetField(ref _activeWorkflowCount, value);
    }

    public EmailSuggestedActionDto? SelectedAction
    {
        get => _selectedAction;
        set
        {
            if (SetField(ref _selectedAction, value))
            {
                (ExecuteActionCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (SetField(ref _isLoading, value))
            {
                (ExecuteActionCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public ICommand ExecuteActionCommand { get; }

    public void ApplyContext(EmailWorkflowContextDto? context, IReadOnlyList<EmailSuggestedActionDto> actions)
    {
        if (context is null || !context.HasContext)
        {
            ProjectDisplay = null;
            WorkflowFamilyDisplay = null;
            ConfidenceDisplay = null;
            ActiveWorkflowCount = 0;
            SuggestedActions.Clear();
            SelectedAction = null;
            return;
        }

        ProjectDisplay = context.ProjectDisplay;
        WorkflowFamilyDisplay = context.WorkflowFamilyDisplay;
        ConfidenceDisplay = context.ConfidenceDisplay;
        ActiveWorkflowCount = context.ActiveWorkflowCount;
        SuggestedActions.Clear();
        foreach (var action in actions.OrderBy(a => a.SortOrder))
        {
            SuggestedActions.Add(action);
        }

        SelectedAction = SuggestedActions.FirstOrDefault();
    }

    public void Clear()
    {
        ApplyContext(null, Array.Empty<EmailSuggestedActionDto>());
        StatusMessage = string.Empty;
    }
}
