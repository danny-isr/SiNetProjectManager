using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.Application.Identity;

namespace SiNet.App.Wpf.Admin.UserGroups;

/// <summary>
/// Native New System view model for user-group assignment admin
/// (members + default assignee; dependent stages are read-only).
/// </summary>
public sealed class UserGroupsViewModel : ObservableObject
{
    private readonly IUserGroupQueryService _query;
    private readonly IUserGroupCommandService _command;
    private readonly IUserLookupService _userLookup;

    private bool _isBusy;
    private string _statusMessage = string.Empty;
    private UserGroupSummaryDto? _selectedGroup;
    private UserGroupMemberDto? _selectedMember;
    private UserLookupDto? _selectedAvailableUser;
    private UserGroupMemberDto? _selectedDefaultAssignee;
    private string _editCode = string.Empty;
    private string _editName = string.Empty;
    private string _editDescription = string.Empty;
    private string _newGroupCode = string.Empty;
    private string _newGroupName = string.Empty;

    public UserGroupsViewModel(
        IUserGroupQueryService query,
        IUserGroupCommandService command,
        IUserLookupService userLookup)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _command = command ?? throw new ArgumentNullException(nameof(command));
        _userLookup = userLookup ?? throw new ArgumentNullException(nameof(userLookup));

        Groups = [];
        Members = [];
        AvailableUsers = [];
        DependentStages = [];

        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        SaveMetadataCommand = new AsyncRelayCommand(SaveMetadataAsync, () => !IsBusy && SelectedGroup is not null);
        SoftDeleteCommand = new AsyncRelayCommand(SoftDeleteAsync, () => !IsBusy && SelectedGroup is not null);
        CreateGroupCommand = new AsyncRelayCommand(CreateGroupAsync, () => !IsBusy);
        AddMemberCommand = new AsyncRelayCommand(AddMemberAsync, () => !IsBusy && SelectedGroup is not null && SelectedAvailableUser is not null);
        RemoveMemberCommand = new AsyncRelayCommand(RemoveMemberAsync, () => !IsBusy && SelectedGroup is not null && SelectedMember is not null);
        SetDefaultAssigneeCommand = new AsyncRelayCommand(SetDefaultAssigneeAsync, () => !IsBusy && SelectedGroup is not null);
        ClearDefaultAssigneeCommand = new AsyncRelayCommand(ClearDefaultAssigneeAsync, () => !IsBusy && SelectedGroup is not null);
    }

    public ObservableCollection<UserGroupSummaryDto> Groups { get; }
    public ObservableCollection<UserGroupMemberDto> Members { get; }
    public ObservableCollection<UserLookupDto> AvailableUsers { get; }
    public ObservableCollection<WorkflowStageGroupDependencyDto> DependentStages { get; }

    public ICommand RefreshCommand { get; }
    public ICommand SaveMetadataCommand { get; }
    public ICommand SoftDeleteCommand { get; }
    public ICommand CreateGroupCommand { get; }
    public ICommand AddMemberCommand { get; }
    public ICommand RemoveMemberCommand { get; }
    public ICommand SetDefaultAssigneeCommand { get; }
    public ICommand ClearDefaultAssigneeCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value))
                return;
            RaiseCommands();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public UserGroupSummaryDto? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (!SetField(ref _selectedGroup, value))
                return;
            RaiseCommands();
            _ = LoadDetailAsync(manageBusy: true);
        }
    }

    public UserGroupMemberDto? SelectedMember
    {
        get => _selectedMember;
        set
        {
            if (!SetField(ref _selectedMember, value))
                return;
            RaiseCommands();
        }
    }

    public UserLookupDto? SelectedAvailableUser
    {
        get => _selectedAvailableUser;
        set
        {
            if (!SetField(ref _selectedAvailableUser, value))
                return;
            RaiseCommands();
        }
    }

    public UserGroupMemberDto? SelectedDefaultAssignee
    {
        get => _selectedDefaultAssignee;
        set => SetField(ref _selectedDefaultAssignee, value);
    }

    public string EditCode
    {
        get => _editCode;
        set => SetField(ref _editCode, value);
    }

    public string EditName
    {
        get => _editName;
        set => SetField(ref _editName, value);
    }

    public string EditDescription
    {
        get => _editDescription;
        set => SetField(ref _editDescription, value);
    }

    public string NewGroupCode
    {
        get => _newGroupCode;
        set => SetField(ref _newGroupCode, value);
    }

    public string NewGroupName
    {
        get => _newGroupName;
        set => SetField(ref _newGroupName, value);
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var selectedId = SelectedGroup?.Id;
            var groups = await _query.GetActiveGroupsAsync().ConfigureAwait(true);
            Groups.Clear();
            foreach (var g in groups)
                Groups.Add(g);

            var next = selectedId is int id
                ? Groups.FirstOrDefault(g => g.Id == id) ?? Groups.FirstOrDefault()
                : Groups.FirstOrDefault();
            SetField(ref _selectedGroup, next);
            OnPropertyChanged(nameof(SelectedGroup));
            RaiseCommands();
            await LoadDetailAsync(manageBusy: false).ConfigureAwait(true);

            StatusMessage = $"{Groups.Count} קבוצות פעילות";
        }
        catch (Exception ex)
        {
            StatusMessage = $"שגיאה בטעינה: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadDetailAsync(bool manageBusy)
    {
        Members.Clear();
        AvailableUsers.Clear();
        DependentStages.Clear();
        SelectedMember = null;
        SelectedAvailableUser = null;
        SelectedDefaultAssignee = null;

        if (SelectedGroup is null)
        {
            EditCode = string.Empty;
            EditName = string.Empty;
            EditDescription = string.Empty;
            return;
        }

        if (manageBusy)
            IsBusy = true;
        try
        {
            var detail = await _query.GetGroupDetailAsync(SelectedGroup.Id).ConfigureAwait(true);
            if (detail is null)
            {
                StatusMessage = "הקבוצה לא נמצאה.";
                return;
            }

            EditCode = detail.Code;
            EditName = detail.Name;
            EditDescription = detail.Description ?? string.Empty;

            foreach (var m in detail.Members)
                Members.Add(m);
            foreach (var s in detail.DependentStages)
                DependentStages.Add(s);

            SelectedDefaultAssignee = detail.DefaultAssigneeId is int defaultId
                ? Members.FirstOrDefault(m => m.UserId == defaultId)
                : null;

            var allUsers = await _userLookup.GetActiveUsersAsync().ConfigureAwait(true);
            var memberIds = Members.Select(m => m.UserId).ToHashSet();
            foreach (var u in allUsers.Where(u => !memberIds.Contains(u.UserId)))
                AvailableUsers.Add(u);
        }
        catch (Exception ex)
        {
            StatusMessage = $"שגיאה בטעינת פרטים: {ex.Message}";
        }
        finally
        {
            if (manageBusy)
                IsBusy = false;
        }
    }

    private async Task SaveMetadataAsync()
    {
        if (SelectedGroup is null)
            return;

        IsBusy = true;
        try
        {
            await _command.UpdateGroupMetadataAsync(
                    SelectedGroup.Id,
                    EditCode,
                    EditName,
                    EditDescription)
                .ConfigureAwait(true);
            StatusMessage = "פרטי הקבוצה נשמרו.";
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"שמירה נכשלה: {ex.Message}";
            MessageBox.Show(StatusMessage, "שגיאה", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SoftDeleteAsync()
    {
        if (SelectedGroup is null)
            return;

        var confirm = MessageBox.Show(
            $"למחוק (soft-delete) את הקבוצה '{SelectedGroup.Name}'?",
            "מחיקת קבוצה",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        try
        {
            await _command.SoftDeleteGroupAsync(SelectedGroup.Id).ConfigureAwait(true);
            StatusMessage = "הקבוצה הושבתה.";
            SelectedGroup = null;
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"מחיקה נכשלה: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CreateGroupAsync()
    {
        IsBusy = true;
        try
        {
            var id = await _command.CreateGroupAsync(NewGroupCode, NewGroupName).ConfigureAwait(true);
            NewGroupCode = string.Empty;
            NewGroupName = string.Empty;
            StatusMessage = "קבוצה נוצרה.";
            await LoadAsync().ConfigureAwait(true);
            SelectedGroup = Groups.FirstOrDefault(g => g.Id == id);
        }
        catch (Exception ex)
        {
            StatusMessage = $"יצירה נכשלה: {ex.Message}";
            MessageBox.Show(StatusMessage, "שגיאה", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddMemberAsync()
    {
        if (SelectedGroup is null || SelectedAvailableUser is null)
            return;

        IsBusy = true;
        try
        {
            await _command.AddMemberAsync(SelectedGroup.Id, SelectedAvailableUser.UserId).ConfigureAwait(true);
            StatusMessage = "חבר נוסף לקבוצה.";
            await LoadDetailAsync(manageBusy: false).ConfigureAwait(true);
            await ReloadSelectedSummaryAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"הוספת חבר נכשלה: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RemoveMemberAsync()
    {
        if (SelectedGroup is null || SelectedMember is null)
            return;

        var confirm = MessageBox.Show(
            $"להסיר את {SelectedMember.DisplayName} מהקבוצה?",
            "הסרת חבר",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        try
        {
            await _command.RemoveMemberAsync(SelectedGroup.Id, SelectedMember.UserId).ConfigureAwait(true);
            StatusMessage = "חבר הוסר מהקבוצה.";
            await LoadDetailAsync(manageBusy: false).ConfigureAwait(true);
            await ReloadSelectedSummaryAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"הסרת חבר נכשלה: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SetDefaultAssigneeAsync()
    {
        if (SelectedGroup is null)
            return;

        IsBusy = true;
        try
        {
            await _command.SetDefaultAssigneeAsync(SelectedGroup.Id, SelectedDefaultAssignee?.UserId)
                .ConfigureAwait(true);
            StatusMessage = SelectedDefaultAssignee is null
                ? "ברירת מחדל נוקתה."
                : "ברירת מחדל עודכנה.";
            await ReloadSelectedSummaryAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"עדכון ברירת מחדל נכשל: {ex.Message}";
            MessageBox.Show(StatusMessage, "שגיאה", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ClearDefaultAssigneeAsync()
    {
        SelectedDefaultAssignee = null;
        await SetDefaultAssigneeAsync().ConfigureAwait(true);
    }

    private async Task ReloadSelectedSummaryAsync()
    {
        if (SelectedGroup is null)
            return;
        var id = SelectedGroup.Id;
        var groups = await _query.GetActiveGroupsAsync().ConfigureAwait(true);
        Groups.Clear();
        foreach (var g in groups)
            Groups.Add(g);
        SetField(ref _selectedGroup, Groups.FirstOrDefault(g => g.Id == id));
        OnPropertyChanged(nameof(SelectedGroup));
        RaiseCommands();
    }

    private void RaiseCommands()
    {
        (RefreshCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (SaveMetadataCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (SoftDeleteCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (CreateGroupCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (AddMemberCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (RemoveMemberCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (SetDefaultAssigneeCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ClearDefaultAssigneeCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }
}
