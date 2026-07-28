using System.Windows;

namespace SiNet.App.Wpf.Admin.MasterPlan;

public partial class MasterPlanMappingWindow : Window
{
    public MasterPlanMappingWindow(MasterPlanMappingViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync().ConfigureAwait(true);
    }
}
