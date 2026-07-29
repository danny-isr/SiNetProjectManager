using System.Windows;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Admin.MasterPlan.Reports;

public partial class R03ReportWindow : Window
{
    public R03ReportWindow(R03ReportViewModel viewModel)
    {
        InitializeComponent();
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }
}
