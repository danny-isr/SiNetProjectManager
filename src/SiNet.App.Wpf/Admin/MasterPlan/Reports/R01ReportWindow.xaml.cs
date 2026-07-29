using System.Windows;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Admin.MasterPlan.Reports;

public partial class R01ReportWindow : Window
{
    public R01ReportWindow(R01ReportViewModel viewModel)
    {
        InitializeComponent();
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }
}
