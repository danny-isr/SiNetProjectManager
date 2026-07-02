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

/// Supports optional Active Directory lookup to pre-fill the form (Save still required).

/// </summary>

public sealed class AddUserViewModel : ObservableObject

{

    private readonly IUserManagementService _userManagementService;

    private readonly IMasterPlanEmployeeLookupService _masterPlanEmployeeLookup;

    private readonly IDirectoryUserLookupService _directoryUserLookup;

    private readonly IUserAdminChangesNotifier? _changesNotifier;

    private string _loginName = string.Empty;

    private string _displayName = string.Empty;

    private string _email = string.Empty;

    private string _notes = string.Empty;

    private string _directorySearchText = string.Empty;

    private string _directoryStatusMessage = string.Empty;

    private DirectoryUserDto? _selectedDirectoryUser;

    private AppRole _selectedRole = AppRole.Employee;

    private AppAccUserType _selectedAccUserType = AppAccUserType.NoAccUser;

    private int? _masterPlanEmployeeId;

    private bool _isActive = true;

    private bool _isSaving;

    private bool _isSearchingDirectory;

    private string _validationMessage = string.Empty;



    public AddUserViewModel(

        IUserManagementService userManagementService,

        IMasterPlanEmployeeLookupService masterPlanEmployeeLookup,

        IDirectoryUserLookupService directoryUserLookup,

        IUserAdminChangesNotifier? changesNotifier = null)

    {

        _userManagementService = userManagementService ?? throw new ArgumentNullException(nameof(userManagementService));

        _masterPlanEmployeeLookup = masterPlanEmployeeLookup ?? throw new ArgumentNullException(nameof(masterPlanEmployeeLookup));

        _directoryUserLookup = directoryUserLookup ?? throw new ArgumentNullException(nameof(directoryUserLookup));

        _changesNotifier = changesNotifier;

        AvailableRoles = new ObservableCollection<AppRole>(

            [AppRole.Employee, AppRole.Management, AppRole.Administrator]);

        AvailableAccUserTypes = new ObservableCollection<AppAccUserType>(AppAccUserTypeDisplay.AllValues);

        MasterPlanEmployees = [];

        DirectorySearchResults = [];

        SaveCommand = new AsyncRelayCommand(SaveAsync, CanSave);

        CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false));

        SearchDirectoryCommand = new AsyncRelayCommand(SearchDirectoryAsync, CanSearchDirectory);

    }



    public ObservableCollection<AppRole> AvailableRoles { get; }



    public ObservableCollection<AppAccUserType> AvailableAccUserTypes { get; }



    public ObservableCollection<MasterPlanEmployeeDto> MasterPlanEmployees { get; }



    public ObservableCollection<DirectoryUserDto> DirectorySearchResults { get; }



    public string DirectorySearchText

    {

        get => _directorySearchText;

        set

        {

            if (SetField(ref _directorySearchText, value))

            {

                SearchDirectoryCommand.RaiseCanExecuteChanged();

            }

        }

    }



    public string DirectoryStatusMessage

    {

        get => _directoryStatusMessage;

        private set => SetField(ref _directoryStatusMessage, value);

    }



    public DirectoryUserDto? SelectedDirectoryUser

    {

        get => _selectedDirectoryUser;

        set

        {

            if (SetField(ref _selectedDirectoryUser, value) && value is not null)

            {

                ApplyDirectoryUser(value);

            }

        }

    }



    public bool IsSearchingDirectory

    {

        get => _isSearchingDirectory;

        private set

        {

            if (SetField(ref _isSearchingDirectory, value))

            {

                SearchDirectoryCommand.RaiseCanExecuteChanged();

            }

        }

    }



    public AsyncRelayCommand SearchDirectoryCommand { get; }



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



    public AppAccUserType SelectedAccUserType

    {

        get => _selectedAccUserType;

        set => SetField(ref _selectedAccUserType, value);

    }



    public int? MasterPlanEmployeeId

    {

        get => _masterPlanEmployeeId;

        set => SetField(ref _masterPlanEmployeeId, value);

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



    public async Task InitializeAsync()

    {

        MasterPlanEmployees.Clear();

        var employees = await _masterPlanEmployeeLookup.GetEmployeesAsync().ConfigureAwait(true);

        foreach (var employee in employees)

        {

            MasterPlanEmployees.Add(employee);

        }



        DirectoryStatusMessage = _directoryUserLookup.IsConfigured

            ? "חפש משתמש ב-Active Directory כדי למלא את הטופס."

            : "Active Directory לא מוגדר — הזן פרטים ידנית או הגדר דומיין ופרטי התחברות.";

    }



    internal void ApplyDirectoryUser(DirectoryUserDto user)

    {

        LoginName = user.LoginName;

        DisplayName = user.DisplayName;

        Email = user.Email ?? string.Empty;

        DirectoryStatusMessage = $"נבחר: {user.DisplayName}";

    }



    private bool CanSearchDirectory() =>

        !IsSearchingDirectory && !string.IsNullOrWhiteSpace(DirectorySearchText?.Trim());



    private async Task SearchDirectoryAsync()

    {

        if (!CanSearchDirectory())

        {

            return;

        }



        if (!_directoryUserLookup.IsConfigured)

        {

            DirectoryStatusMessage =

                "Active Directory לא מוגדר. הגדר ActiveDirectory:DomainName ופרטי התחברות בהגדרות המערכת.";

            DirectorySearchResults.Clear();

            return;

        }



        IsSearchingDirectory = true;

        DirectoryStatusMessage = "מחפש...";



        try

        {

            var results = await _directoryUserLookup

                .SearchUsersAsync(DirectorySearchText.Trim())

                .ConfigureAwait(true);



            DirectorySearchResults.Clear();

            foreach (var user in results)

            {

                DirectorySearchResults.Add(user);

            }



            DirectoryStatusMessage = results.Count == 0

                ? "לא נמצאו משתמשים תואמים."

                : $"{results.Count} משתמשים נמצאו — בחר משתמש למילוי הטופס.";

        }

        catch (Exception ex)

        {

            DirectorySearchResults.Clear();

            DirectoryStatusMessage = ex.Message;

        }

        finally

        {

            IsSearchingDirectory = false;

        }

    }



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

                AccUserType: SelectedAccUserType,

                IsActive: IsActive,

                MasterPlanEmployeeId: MasterPlanEmployeeId,

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


