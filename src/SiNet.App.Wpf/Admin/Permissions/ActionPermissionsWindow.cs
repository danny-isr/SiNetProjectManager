using System.Windows;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Admin.Permissions;

/// <summary>Native New System host window for <see cref="ActionPermissionsView"/>.</summary>
public sealed class ActionPermissionsWindow : Window
{
    public ActionPermissionsWindow(ActionPermissionsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        Title = "הרשאות פעולה — מערכת חדשה";
        Width = 900;
        Height = 620;
        FlowDirection = FlowDirection.RightToLeft;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        Content = new ActionPermissionsView { DataContext = viewModel };
        Loaded += async (_, _) => await viewModel.LoadAsync().ConfigureAwait(true);
    }
}
