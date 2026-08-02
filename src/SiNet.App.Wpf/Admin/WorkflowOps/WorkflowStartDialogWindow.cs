using System.Windows;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Admin.WorkflowOps;

public sealed class WorkflowStartDialogWindow : Window
{
    private readonly WorkflowStartDialogViewModel _viewModel;

    public WorkflowStartDialogWindow(WorkflowStartDialogViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        Title = "הפעלת תהליך — מערכת חדשה";
        Width = 560;
        Height = 520;
        MinWidth = 420;
        MinHeight = 400;
        FlowDirection = FlowDirection.RightToLeft;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        Content = new WorkflowStartDialogView { DataContext = viewModel };
        Loaded += async (_, _) => await viewModel.LoadAsync().ConfigureAwait(true);
        viewModel.RequestClose += (_, _) => DialogResult = viewModel.DialogResult;
    }

    public int? StartedInstanceId => _viewModel.StartedInstanceId;
}
