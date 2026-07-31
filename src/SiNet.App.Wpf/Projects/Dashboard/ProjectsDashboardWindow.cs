using System.Windows;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Projects.Dashboard;

public sealed class ProjectsDashboardWindow : Window
{
    public ProjectsDashboardWindow(ProjectsDashboardViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        Title = "ריכוז פרויקטים — מערכת חדשה";
        Width = 1280;
        Height = 780;
        MinWidth = 960;
        MinHeight = 560;
        FlowDirection = FlowDirection.RightToLeft;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        Content = new ProjectsDashboardView { DataContext = viewModel };
        Loaded += async (_, _) => await viewModel.LoadAsync().ConfigureAwait(true);
    }
}
