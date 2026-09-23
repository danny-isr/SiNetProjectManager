using System.Windows;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Admin.WorkflowOps;

public sealed class AdoptExistingWorkflowWindow : Window
{
    private readonly AdoptExistingWorkflowViewModel _viewModel;

    public AdoptExistingWorkflowWindow(AdoptExistingWorkflowViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        Title = "הטמעת תהליך קיים";
        Width = 760;
        Height = 820;
        MinWidth = 560;
        MinHeight = 520;
        FlowDirection = FlowDirection.RightToLeft;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        Content = new AdoptExistingWorkflowView { DataContext = viewModel };
        Loaded += async (_, _) => await viewModel.LoadAsync().ConfigureAwait(true);
        viewModel.RequestClose += (_, ok) =>
        {
            DialogResult = ok;
            Close();
        };
    }

    public void SetProject(int projectId) => _viewModel.ProjectId = projectId;
}
