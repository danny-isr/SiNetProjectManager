using System.Windows;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Admin.SystemStatus;

public sealed class SystemStatusWindow : Window
{
    public SystemStatusWindow(SystemStatusViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        Title = "מצב מערכת — מערכת חדשה";
        Width = 780;
        Height = 520;
        FlowDirection = FlowDirection.RightToLeft;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        Content = new SystemStatusView { DataContext = viewModel };
        Loaded += async (_, _) => await viewModel.LoadAsync().ConfigureAwait(true);
        Closed += (_, _) => viewModel.Dispose();
    }
}
