using System.Windows;
using SiNet.App.Wpf.Theme;
using SiNet.Application.Workflow;

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
        Loaded += async (_, _) =>
        {
            AdoptionDebugLog.Write(
                "AdoptExistingWorkflowWindow.Loaded",
                $"ENTER IsVisible={IsVisible} IsActive={IsActive} projectId={viewModel.ProjectId}");
            try
            {
                await viewModel.LoadAsync().ConfigureAwait(true);
                AdoptionDebugLog.Write("AdoptExistingWorkflowWindow.Loaded", "LoadAsync returned");
            }
            catch (Exception ex)
            {
                AdoptionDebugLog.Error("AdoptExistingWorkflowWindow.Loaded", ex);
                throw;
            }
        };
        viewModel.RequestClose += (_, ok) =>
        {
            DialogResult = ok;
            Close();
        };
    }

    public void SetProject(int projectId) => _viewModel.ProjectId = projectId;
}
