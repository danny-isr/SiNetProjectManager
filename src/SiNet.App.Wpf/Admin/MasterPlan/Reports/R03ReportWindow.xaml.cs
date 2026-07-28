using System.Windows;

namespace SiNet.App.Wpf.Admin.MasterPlan.Reports;

public partial class R03ReportWindow : Window
{
    public R03ReportWindow(R03ReportViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }
}
