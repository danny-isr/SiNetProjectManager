using System.Windows;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Admin.MasterPlan;

public partial class MasterPlanMonthlyRestoreWindow : Window
{
    public MasterPlanMonthlyRestoreWindow(MasterPlanMonthlyRestoreViewModel viewModel)
    {
        InitializeComponent();
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }
}
