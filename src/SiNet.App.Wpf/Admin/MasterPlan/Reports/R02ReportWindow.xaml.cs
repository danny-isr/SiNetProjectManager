using System.Windows;

namespace SiNet.App.Wpf.Admin.MasterPlan.Reports;

public partial class R02ReportWindow : Window
{
    public R02ReportWindow(R02ReportViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }
}
