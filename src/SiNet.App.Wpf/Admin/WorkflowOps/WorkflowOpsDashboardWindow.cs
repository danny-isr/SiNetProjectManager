using System.Windows;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Admin.WorkflowOps;

public sealed class WorkflowOpsDashboardWindow : Window
{
    public WorkflowOpsDashboardWindow(WorkflowOpsDashboardViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        Title = "בריאות תהליכים — מערכת חדשה";
        Width = 1100;
        Height = 720;
        MinWidth = 860;
        MinHeight = 520;
        FlowDirection = FlowDirection.RightToLeft;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        Content = new WorkflowOpsDashboardView { DataContext = viewModel };
        Loaded += async (_, _) => await viewModel.LoadAsync().ConfigureAwait(true);
        Closed += (_, _) => viewModel.Dispose();
    }
}
