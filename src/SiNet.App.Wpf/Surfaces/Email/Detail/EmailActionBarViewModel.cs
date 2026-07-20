using System.Windows.Input;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Inbox;

namespace SiNet.App.Wpf.Surfaces.Email.Detail;

public sealed class EmailActionBarViewModel : ObservableObject
{
    private string _activeProjectDisplay = "לא נבחר פרויקט";
    private string? _moveBlockReason;
    private string? _assignedHint;
    private bool _canFileEmail;
    private bool _canMoveToProject;
    private bool _showUnassignedLayout;
    private bool _showAssignedLayout;

    public EmailActionBarViewModel(
        Func<Task> fileEmailAsync,
        Func<Task> moveToProjectAsync)
    {
        FileEmailCommand = new AsyncRelayCommand(fileEmailAsync, () => CanFileEmail);
        MoveToProjectCommand = new AsyncRelayCommand(moveToProjectAsync, () => CanMoveToProject);
    }

    public string ActiveProjectDisplay
    {
        get => _activeProjectDisplay;
        set => SetField(ref _activeProjectDisplay, value);
    }

    public string? MoveBlockReason
    {
        get => _moveBlockReason;
        set
        {
            if (SetField(ref _moveBlockReason, value))
            {
                OnPropertyChanged(nameof(ShowMoveBlockReason));
                OnPropertyChanged(nameof(MoveButtonToolTip));
            }
        }
    }

    public string MoveButtonToolTip =>
        string.IsNullOrWhiteSpace(MoveBlockReason)
            ? "העבר קבצים מתויקים לתיקיית הפרויקט ב-ACC"
            : MoveBlockReason;

    public string? AssignedHint
    {
        get => _assignedHint;
        set
        {
            if (SetField(ref _assignedHint, value))
            {
                OnPropertyChanged(nameof(ShowAssignedHint));
            }
        }
    }

    public bool ShowAssignedHint => !string.IsNullOrWhiteSpace(AssignedHint);

    public bool CanFileEmail
    {
        get => _canFileEmail;
        set
        {
            if (SetField(ref _canFileEmail, value))
            {
                (FileEmailCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanMoveToProject
    {
        get => _canMoveToProject;
        set
        {
            if (SetField(ref _canMoveToProject, value))
            {
                (MoveToProjectCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool ShowUnassignedLayout
    {
        get => _showUnassignedLayout;
        set
        {
            if (SetField(ref _showUnassignedLayout, value))
            {
                OnPropertyChanged(nameof(ShowMoveBlockReason));
            }
        }
    }

    public bool ShowAssignedLayout
    {
        get => _showAssignedLayout;
        set
        {
            if (SetField(ref _showAssignedLayout, value))
            {
                OnPropertyChanged(nameof(ShowMoveBlockReason));
            }
        }
    }

    /// <summary>
    /// Move-block text is only meaningful after Gmail project-label filing
    /// (<see cref="ShowAssignedLayout"/>); otherwise it falsely shows «המייל לא משויך»
    /// next to the File button before any label was applied.
    /// </summary>
    public bool ShowMoveBlockReason =>
        ShowAssignedLayout && !string.IsNullOrWhiteSpace(MoveBlockReason);

    public ICommand FileEmailCommand { get; }
    public ICommand MoveToProjectCommand { get; }

    public void RefreshCommandStates(bool canFile, bool canMove)
    {
        CanFileEmail = canFile;
        CanMoveToProject = canMove;
    }
}
