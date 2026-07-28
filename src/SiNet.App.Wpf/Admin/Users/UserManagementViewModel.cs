using System.Collections.ObjectModel;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Identity;

namespace SiNet.App.Wpf.Admin.Users;

/// <summary>
/// Native New System user-management list with inline editing. Uses <see cref="IUserManagementService"/>
/// only — no legacy MVVM (see <c>docs/NEW_SYSTEM_BOUNDARY.md</c>).
/// </summary>
public sealed class UserManagementViewModel : ObservableObject
{
    private readonly IUserManagementService _userManagementService;
    private readonly IMasterPlanEmployeeLookupService _masterPlanEmployeeLookup;
    private readonly IUserAdminChangesNotifier? _changesNotifier;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private bool _isLoading;
    private bool _isSaving;
    private string _statusMessage = string.Empty;
    private string _searchText = string.Empty;
    private UserEditRow? _selectedUser;

    public UserManagementViewModel(
        IUserManagementService userManagementService,
        IMasterPlanEmployeeLookupService masterPlanEmployeeLookup,
        IUserAdminChangesNotifier? changesNotifier = null)
    {
        _userManagementService = userManagementService ?? throw new ArgumentNullException(nameof(userManagementService));
        _masterPlanEmployeeLookup = masterPlanEmployeeLookup ?? throw new ArgumentNullException(nameof(masterPlanEmployeeLookup));
        _changesNotifier = changesNotifier;
        Users = [];
        FilteredUsers = [];
        MasterPlanEmployees = [];
        AvailableRoles =
        [
            AppRole.Employee,
            AppRole.Management,
            AppRole.Administrator,
        ];
        AvailableAccUserTypes = new ObservableCollection<AppAccUserType>(AppAccUserTypeDisplay.AllValues);

        RefreshCommand = new AsyncRelayCommand(() => LoadUsersAsync(force: false), () => !IsLoading && !IsSaving);
        SaveCommand = new AsyncRelayCommand(SaveChangesAsync, CanSave);
        CancelCommand = new RelayCommand(_ => CancelChanges(), _ => HasUnsavedChanges && !IsSaving);

        if (_changesNotifier is not null)
        {
            _changesNotifier.UsersChanged += (_, _) => _ = LoadUsersAsync(force: true);
        }
    }

    public ObservableCollection<UserEditRow> Users { get; }

    public ObservableCollection<UserEditRow> FilteredUsers { get; }

    public ObservableCollection<MasterPlanEmployeeDto> MasterPlanEmployees { get; }

    public ObservableCollection<AppRole> AvailableRoles { get; }

    public ObservableCollection<AppAccUserType> AvailableAccUserTypes { get; }

    public UserEditRow? SelectedUser
    {
        get => _selectedUser;
        set => SetField(ref _selectedUser, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
            {
                ApplySearchFilter();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetField(ref _isLoading, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (SetField(ref _isSaving, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
                SaveCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasUnsavedChanges => Users.Any(u => u.IsDirty);

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand SaveCommand { get; }

    public RelayCommand CancelCommand { get; }

    /// <summary>
    /// Loads users from the Application port.
    /// When <paramref name="force"/> is true (e.g. after <see cref="IUserAdminChangesNotifier.UsersChanged"/>),
    /// discards local dirty edits and reloads so newly added users appear immediately.
    /// </summary>
    public async Task LoadUsersAsync(bool force = false)
    {
        await _loadGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (!force && HasUnsavedChanges)
            {
                StatusMessage = "יש שינויים שלא נשמרו — שמור או בטל לפני רענון.";
                return;
            }

            if (force && HasUnsavedChanges)
            {
                DiscardDirtyRowsWithoutStatus();
            }

            IsLoading = true;
            StatusMessage = "טוען משתמשים...";

            try
            {
                var usersTask = _userManagementService.GetUsersAsync();
                var employeesTask = _masterPlanEmployeeLookup.GetEmployeesAsync();
                await Task.WhenAll(usersTask, employeesTask).ConfigureAwait(true);

                var users = await usersTask.ConfigureAwait(true);
                var employees = await employeesTask.ConfigureAwait(true);

                MasterPlanEmployees.Clear();
                foreach (var employee in employees)
                {
                    MasterPlanEmployees.Add(employee);
                }

                foreach (var existing in Users)
                {
                    existing.PropertyChanged -= OnUserRowPropertyChanged;
                }

                Users.Clear();
                foreach (var user in users)
                {
                    var row = new UserEditRow(user);
                    row.PropertyChanged += OnUserRowPropertyChanged;
                    Users.Add(row);
                }

                UpdateMasterPlanEmployeeNames();
                ApplySearchFilter();
                SelectedUser = FilteredUsers.FirstOrDefault() ?? Users.FirstOrDefault();

                OnPropertyChanged(nameof(HasUnsavedChanges));
                SaveCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();

                if (MasterPlanEmployees.Count <= 1)
                {
                    StatusMessage = $"נטענו {Users.Count} משתמשים. MasterPlan: אין חיבור DB מוגדר (ReplicaDatabase / MasterPlanDatabase).";
                }
                else
                {
                    StatusMessage = $"נטענו {Users.Count} משתמשים, {MasterPlanEmployees.Count - 1} עובדי MasterPlan.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"שגיאה בטעינה: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }
        finally
        {
            _loadGate.Release();
        }
    }

    public void UpdateMasterPlanEmployeeNames()
    {
        var nameById = MasterPlanEmployees
            .Where(e => e.Id.HasValue)
            .ToDictionary(e => e.Id!.Value, e => e.Name);

        foreach (var row in Users)
        {
            if (row.MasterPlanEmployeeId is int id && nameById.TryGetValue(id, out var name))
            {
                row.MasterPlanEmployeeName = name;
            }
            else
            {
                row.MasterPlanEmployeeName = null;
            }
        }
    }

    private void OnUserRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is UserEditRow row && e.PropertyName == nameof(UserEditRow.MasterPlanEmployeeId))
        {
            var match = MasterPlanEmployees.FirstOrDefault(m => m.Id == row.MasterPlanEmployeeId);
            row.MasterPlanEmployeeName = match?.Name;
        }

        NotifyRowChanged();
    }

    private void ApplySearchFilter()
    {
        FilteredUsers.Clear();
        var search = SearchText.Trim();

        foreach (var user in Users)
        {
            if (string.IsNullOrEmpty(search)
                || user.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || user.Email.Contains(search, StringComparison.OrdinalIgnoreCase)
                || user.LoginName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (user.MasterPlanEmployeeName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                FilteredUsers.Add(user);
            }
        }
    }

    private bool CanSave() => !IsLoading && !IsSaving && HasUnsavedChanges;

    public async Task SaveChangesAsync()
    {
        if (!CanSave())
        {
            return;
        }

        IsSaving = true;
        StatusMessage = "שומר...";

        try
        {
            var updates = Users.Where(u => u.IsDirty).Select(u => u.ToUpdateCommand()).ToList();
            await _userManagementService.UpdateUsersAsync(updates).ConfigureAwait(true);

            foreach (var row in Users.Where(u => u.IsDirty))
            {
                row.MarkClean();
            }

            OnPropertyChanged(nameof(HasUnsavedChanges));
            SaveCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
            StatusMessage = $"נשמרו {updates.Count} משתמשים.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"שגיאה בשמירה: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void CancelChanges()
    {
        DiscardDirtyRowsWithoutStatus();
        UpdateMasterPlanEmployeeNames();
        OnPropertyChanged(nameof(HasUnsavedChanges));
        SaveCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        StatusMessage = "שינויים בוטלו.";
    }

    private void DiscardDirtyRowsWithoutStatus()
    {
        foreach (var row in Users.Where(u => u.IsDirty))
        {
            row.RevertChanges();
        }
    }

    public void NotifyRowChanged()
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        SaveCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
    }
}
