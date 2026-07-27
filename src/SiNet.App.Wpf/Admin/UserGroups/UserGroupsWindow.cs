using System.Windows;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Admin.UserGroups;

/// <summary>Native New System host window for <see cref="UserGroupsView"/>.</summary>
public sealed class UserGroupsWindow : Window
{
    public UserGroupsWindow(UserGroupsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        Title = "הקצאות משתמשים / קבוצות — מערכת חדשה";
        Width = 980;
        Height = 640;
        MinWidth = 780;
        MinHeight = 480;
        FlowDirection = FlowDirection.RightToLeft;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        Content = new UserGroupsView { DataContext = viewModel };
        Loaded += async (_, _) => await viewModel.LoadAsync().ConfigureAwait(true);
    }
}
