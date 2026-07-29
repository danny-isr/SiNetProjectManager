using System.Windows;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel, InspectionShellView inspectionShell)
    {
        InitializeComponent();
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        DataContext = viewModel;
        InspectionHost.Content = inspectionShell;
    }
}
