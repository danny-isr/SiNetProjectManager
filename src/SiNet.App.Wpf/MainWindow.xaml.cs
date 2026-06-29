using System.Windows;
using SiNet.App.Wpf.Inspection;

namespace SiNet.App.Wpf;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel, InspectionShellView inspectionShell)
    {
        InitializeComponent();
        DataContext = viewModel;
        InspectionHost.Content = inspectionShell;
    }
}
