using System.Windows;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Admin.MasterPlan.Reports;

public partial class R02ReportWindow : Window
{
    public R02ReportWindow(R02ReportViewModel viewModel)
    {
        InitializeComponent();
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }
}
