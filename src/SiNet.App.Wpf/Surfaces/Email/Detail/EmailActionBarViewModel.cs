using System.Windows.Input;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Inbox;

namespace SiNet.App.Wpf.Surfaces.Email.Detail;

public sealed class EmailActionBarViewModel : ObservableObject
{
    private string _activeProjectDisplay = "לא נבחר פרויקט";
    private string? _moveBlockReason;
    private bool _canFileEmail;
    private bool _canMoveToProject;

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
            }
        }
    }

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

    public bool ShowMoveBlockReason => !string.IsNullOrWhiteSpace(MoveBlockReason);

    public ICommand FileEmailCommand { get; }
    public ICommand MoveToProjectCommand { get; }

    public void RefreshCommandStates(bool canFile, bool canMove)
    {
        CanFileEmail = canFile;
        CanMoveToProject = canMove;
    }
}
