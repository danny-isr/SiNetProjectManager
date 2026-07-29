using System.Windows;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Admin.MasterPlan;

public partial class MasterPlanMappingWindow : Window
{
    public MasterPlanMappingWindow(MasterPlanMappingViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync().ConfigureAwait(true);
    }
}
