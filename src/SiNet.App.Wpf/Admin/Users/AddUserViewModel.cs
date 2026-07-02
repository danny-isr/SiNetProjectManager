using System.Collections.ObjectModel;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Identity;

namespace SiNet.App.Wpf.Admin.Users;

/// <summary>
/// Native New System add-user dialog view model. Validates duplicate login names and calls
/// <see cref="IUserManagementService.AddUserAsync"/> with <see cref="CreateUserCommand"/> (including Notes).
/// </summary>
public sealed class AddUserViewModel : ObservableObject
{
    private readonly IUserManagementService _userManagementService;
    private readonly IUserAdminChangesNotifier? _changesNotifier;
    private string _loginName = string.Empty;
    private string _displayName = string.Empty;
    private string _email = string.Empty;
    private string _notes = string.Empty;
    private AppRole _selectedRole = AppRole.Employee;
    private bool _isActive = true;
    private bool _isSaving;
    private string _validationMessage = string.Empty;

    public AddUserViewModel(
        IUserManagementService userManagementService,
        IUserAdminChangesNotifier? changesNotifier = null)
    {
        _userManagementService = userManagementService ?? throw new ArgumentNullException(nameof(userManagementService));
        _changesNotifier = changesNotifier;
        AvailableRoles = new ObservableCollection<AppRole>(
            [AppRole.Employee, AppRole.Management, AppRole.Administrator]);
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanSave);
        CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false));
    }

    public ObservableCollection<AppRole> AvailableRoles { get; }

    public string LoginName
    {
        get => _loginName;
        set
        {
            if (SetField(ref _loginName, value))
            {
                ValidateInput();
            }
        }
    }

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (SetField(ref _displayName, value))
            {
                ValidateInput();
            }
        }
    }

    public string Email
    {
        get => _email;
        set => SetField(ref _email, value);
    }

    public string Notes
    {
        get => _notes;
        set => SetField(ref _notes, value);
    }

    public AppRole SelectedRole
    {
        get => _selectedRole;
        set => SetField(ref _selectedRole, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetField(ref _isActive, value);
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (SetField(ref _isSaving, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetField(ref _validationMessage, value);
    }

    public AsyncRelayCommand SaveCommand { get; }

    public ICommand CancelCommand { get; }

    public event Action<bool>? RequestClose;

    private void ValidateInput()
    {
        ValidationMessage = string.IsNullOrWhiteSpace(LoginName?.Trim())
            || string.IsNullOrWhiteSpace(DisplayName?.Trim())
            ? "LoginName ו-DisplayName נדרשים."
            : string.Empty;
        SaveCommand.RaiseCanExecuteChanged();
    }

    private bool CanSave() =>
        !IsSaving
        && string.IsNullOrWhiteSpace(ValidationMessage)
        && !string.IsNullOrWhiteSpace(LoginName?.Trim())
        && !string.IsNullOrWhiteSpace(DisplayName?.Trim());

    public async Task SaveAsync()
    {
        if (!CanSave())
        {
            return;
        }

        IsSaving = true;
        ValidationMessage = string.Empty;

        try
        {
            var login = LoginName.Trim();
            if (await _userManagementService.CheckDuplicateLoginNameAsync(login).ConfigureAwait(true))
            {
                ValidationMessage = $"LoginName '{login}' כבר קיים.";
                return;
            }

            var command = new CreateUserCommand(
                LoginName: login,
                DisplayName: DisplayName.Trim(),
                Email: string.IsNullOrWhiteSpace(Email) ? null : Email.Trim(),
                Role: SelectedRole,
                IsActive: IsActive,
                Notes: string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim());

            await _userManagementService.AddUserAsync(command).ConfigureAwait(true);
            _changesNotifier?.NotifyUsersChanged();
            RequestClose?.Invoke(true);
        }
        catch (Exception ex)
        {
            ValidationMessage = ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }
}
