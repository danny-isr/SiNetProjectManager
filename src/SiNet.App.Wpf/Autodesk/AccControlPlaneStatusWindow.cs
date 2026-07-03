using System.Windows;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Autodesk;

public sealed class AccControlPlaneStatusWindow : Window
{
    public AccControlPlaneStatusWindow(AccControlPlaneStatusWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        Title = "סטטוס ACC — מערכת חדשה";
        Width = 760;
        Height = 620;
        FlowDirection = FlowDirection.RightToLeft;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        Content = new AccControlPlaneStatusWindowView { DataContext = viewModel };
        Loaded += async (_, _) => await viewModel.LoadAsync().ConfigureAwait(true);
    }
}
