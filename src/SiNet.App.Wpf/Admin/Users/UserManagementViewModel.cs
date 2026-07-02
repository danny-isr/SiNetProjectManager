using System.Collections.ObjectModel;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.Application.Identity;

namespace SiNet.App.Wpf.Admin.Users;

/// <summary>
/// Native New System user-management list (read-only in this slice). Uses <see cref="IUserManagementService"/>
/// only — no legacy MVVM (see <c>docs/NEW_SYSTEM_BOUNDARY.md</c>).
/// </summary>
public sealed class UserManagementViewModel : ObservableObject
{
    private readonly IUserManagementService _userManagementService;
    private bool _isLoading;
    private string _statusMessage = string.Empty;

    public UserManagementViewModel(IUserManagementService userManagementService)
    {
        _userManagementService = userManagementService ?? throw new ArgumentNullException(nameof(userManagementService));
        Users = [];
        RefreshCommand = new AsyncRelayCommand(LoadUsersAsync, () => !IsLoading);
    }

    public ObservableCollection<UserSummaryDto> Users { get; }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetField(ref _isLoading, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public AsyncRelayCommand RefreshCommand { get; }

    public async Task LoadUsersAsync()
    {
        IsLoading = true;
        StatusMessage = "טוען משתמשים...";

        try
        {
            var users = await _userManagementService.GetUsersAsync().ConfigureAwait(true);
            Users.Clear();
            foreach (var user in users)
            {
                Users.Add(user);
            }

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
}
