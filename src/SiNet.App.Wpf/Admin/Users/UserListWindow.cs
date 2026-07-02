using System.Windows;

namespace SiNet.App.Wpf.Admin.Users;

/// <summary>Native New System host window for <see cref="UserManagementView"/>.</summary>
public sealed class UserListWindow : Window
{
    public UserListWindow(UserManagementViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        Title = "ניהול משתמשים — מערכת חדשה";
        Width = 960;
        Height = 560;
        FlowDirection = FlowDirection.RightToLeft;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new UserManagementView { DataContext = viewModel };
        Loaded += async (_, _) => await viewModel.LoadUsersAsync().ConfigureAwait(true);
    }
}
