using System.Windows;

namespace SiNet.App.Wpf.Admin.MasterPlan.Reports;

public partial class R01ReportWindow : Window
{
    public R01ReportWindow(R01ReportViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }
}
