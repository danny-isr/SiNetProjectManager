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
    private readonly IUserAdminChangesNotifier? _changesNotifier;
    private bool _isLoading;
    private bool _isSaving;
    private string _statusMessage = string.Empty;
    private UserEditRow? _selectedUser;

    public UserManagementViewModel(
        IUserManagementService userManagementService,
        IUserAdminChangesNotifier? changesNotifier = null)
    {
        _userManagementService = userManagementService ?? throw new ArgumentNullException(nameof(userManagementService));
        _changesNotifier = changesNotifier;
        Users = [];
        AvailableRoles =
        [
            AppRole.Employee,
            AppRole.Management,
            AppRole.Administrator,
        ];

        RefreshCommand = new AsyncRelayCommand(LoadUsersAsync, () => !IsLoading && !IsSaving);
        SaveCommand = new AsyncRelayCommand(SaveChangesAsync, CanSave);
        CancelCommand = new RelayCommand(_ => CancelChanges(), _ => HasUnsavedChanges && !IsSaving);

        if (_changesNotifier is not null)
        {
            _changesNotifier.UsersChanged += (_, _) => _ = LoadUsersAsync();
        }
    }

    public ObservableCollection<UserEditRow> Users { get; }

    public ObservableCollection<AppRole> AvailableRoles { get; }

    public UserEditRow? SelectedUser
    {
        get => _selectedUser;
        set => SetField(ref _selectedUser, value);
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

    public async Task LoadUsersAsync()
    {
        if (HasUnsavedChanges)
        {
            StatusMessage = "יש שינויים שלא נשמרו — שמור או בטל לפני רענון.";
            return;
        }

        IsLoading = true;
        StatusMessage = "טוען משתמשים...";

        try
        {
            var users = await _userManagementService.GetUsersAsync().ConfigureAwait(true);
            Users.Clear();
            foreach (var user in users)
            {
                var row = new UserEditRow(user);
                row.PropertyChanged += (_, _) => NotifyRowChanged();
                Users.Add(row);
            }

            SelectedUser = Users.FirstOrDefault();
            StatusMessage = $"נטענו {Users.Count} משתמשים.";
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
        foreach (var row in Users.Where(u => u.IsDirty))
        {
            row.RevertChanges();
        }

        OnPropertyChanged(nameof(HasUnsavedChanges));
        SaveCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        StatusMessage = "שינויים בוטלו.";
    }

    public void NotifyRowChanged()
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        SaveCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
    }
}
