using System.Collections.ObjectModel;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.Application.Identity;

namespace SiNet.App.Wpf.Admin.Permissions;

/// <summary>
/// Native New System action-permissions admin view model. Uses <see cref="IActionPermissionAdminService"/>
/// only — no legacy MVVM (see <c>docs/NEW_SYSTEM_BOUNDARY.md</c>).
/// </summary>
public sealed class ActionPermissionsViewModel : ObservableObject
{
    private readonly IActionPermissionAdminService _adminService;
    private readonly Dictionary<string, HashSet<int>> _permissionMap = new(StringComparer.Ordinal);
    private bool _isLoading;
    private bool _isSaving;
    private bool _hasUnsavedChanges;
    private string _statusMessage = string.Empty;
    private string _userSearchText = string.Empty;
    private ActionPermissionActionRow? _selectedAction;
    private string? _selectedActionCode;

    public ActionPermissionsViewModel(IActionPermissionAdminService adminService)
    {
        _adminService = adminService ?? throw new ArgumentNullException(nameof(adminService));
        Actions = [];
        AssignableUsers = [];
        FilteredUserRows = [];

        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsLoading && !IsSaving);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsLoading && !IsSaving && HasUnsavedChanges);
    }

    public ObservableCollection<ActionPermissionActionRow> Actions { get; }

    public ObservableCollection<ActionPermissionAssigneeDto> AssignableUsers { get; }

    public ObservableCollection<ActionPermissionUserRow> FilteredUserRows { get; }

    public ActionPermissionActionRow? SelectedAction
    {
        get => _selectedAction;
        set
        {
            if (SetField(ref _selectedAction, value))
            {
                _selectedActionCode = value?.ActionCode;
                RefreshUserChecklist();
            }
        }
    }

    public string UserSearchText
    {
        get => _userSearchText;
        set
        {
            if (SetField(ref _userSearchText, value))
            {
                RefreshUserChecklist();
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
            }
        }
    }

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set
        {
            if (SetField(ref _hasUnsavedChanges, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand SaveCommand { get; }

    public async Task LoadAsync()
    {
        if (HasUnsavedChanges)
        {
            StatusMessage = "יש שינויים שלא נשמרו — שמור לפני רענון.";
            return;
        }

        IsLoading = true;
        StatusMessage = "טוען הרשאות...";

        try
        {
            var users = await _adminService.GetAssignableUsersAsync().ConfigureAwait(true);
            var permissions = await _adminService.GetActivePermissionsByActionAsync().ConfigureAwait(true);

            AssignableUsers.Clear();
            foreach (var user in users)
            {
                AssignableUsers.Add(user);
            }

            _permissionMap.Clear();
            Actions.Clear();

            foreach (var entry in ActionPermissionCatalog.All)
            {
                var authorized = permissions.TryGetValue(entry.ActionCode, out var set)
                    ? new HashSet<int>(set)
                    : [];

                _permissionMap[entry.ActionCode] = authorized;
                Actions.Add(new ActionPermissionActionRow(
                    entry.ActionCode,
                    entry.DisplayName,
                    authorized.Count));
            }

            SelectedAction = Actions.FirstOrDefault();
            HasUnsavedChanges = false;
            StatusMessage = $"{AssignableUsers.Count} עובדים, {permissions.Values.Sum(v => v.Count)} הרשאות פעילות.";
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

    public void OnUserAuthorizationChanged(ActionPermissionUserRow row)
    {
        if (_selectedActionCode is null)
        {
            return;
        }

        if (!_permissionMap.TryGetValue(_selectedActionCode, out var set))
        {
            set = [];
            _permissionMap[_selectedActionCode] = set;
        }

        if (row.IsAuthorized)
        {
            set.Add(row.UserId);
        }
        else
        {
            set.Remove(row.UserId);
        }

        if (SelectedAction is not null)
        {
            SelectedAction.AuthorizedCount = set.Count;
        }

        HasUnsavedChanges = true;
    }

    private async Task SaveAsync()
    {
        IsSaving = true;
        StatusMessage = "שומר...";

        try
        {
            var payload = _permissionMap.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlySet<int>)kvp.Value,
                StringComparer.Ordinal);

            await _adminService.SaveAllActionPermissionsAsync(payload).ConfigureAwait(true);
            HasUnsavedChanges = false;
            StatusMessage = "הרשאות נשמרו בהצלחה.";
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

    private void RefreshUserChecklist()
    {
        FilteredUserRows.Clear();
        if (_selectedActionCode is null)
        {
            return;
        }

        var authorized = _permissionMap.GetValueOrDefault(_selectedActionCode) ?? [];
        var search = UserSearchText.Trim();

        foreach (var user in AssignableUsers)
        {
            if (!string.IsNullOrEmpty(search)
                && !(user.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
                     || (user.Email?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)))
            {
                continue;
            }

            FilteredUserRows.Add(new ActionPermissionUserRow(
                user.UserId,
                user.DisplayName,
                user.Email,
                authorized.Contains(user.UserId)));
        }
    }
}
