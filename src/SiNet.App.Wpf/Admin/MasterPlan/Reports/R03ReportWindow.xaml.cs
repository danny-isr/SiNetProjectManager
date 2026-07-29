using System.Windows;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Admin.MasterPlan.Reports;

public partial class R03ReportWindow : Window
{
    private readonly R03ReportViewModel _viewModel;

    public R03ReportWindow(R03ReportViewModel viewModel)
    {
        InitializeComponent();
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
        => await _viewModel.InitializeAsync().ConfigureAwait(true);

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
