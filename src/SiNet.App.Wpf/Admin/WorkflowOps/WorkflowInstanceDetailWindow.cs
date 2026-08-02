using System.Windows;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Admin.WorkflowOps;

public sealed class WorkflowInstanceDetailWindow : Window
{
    public WorkflowInstanceDetailWindow(WorkflowInstanceDetailViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        Title = "מופע תהליך — מערכת חדשה";
        Width = 780;
        Height = 640;
        MinWidth = 560;
        MinHeight = 420;
        FlowDirection = FlowDirection.RightToLeft;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        Content = new WorkflowInstanceDetailView { DataContext = viewModel };
        Loaded += async (_, _) => await viewModel.LoadAsync().ConfigureAwait(true);
    }
}
