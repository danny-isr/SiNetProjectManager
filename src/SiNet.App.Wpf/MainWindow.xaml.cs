using System.Windows;
using SiNet.App.Wpf.Inbox;

namespace SiNet.App.Wpf;

public partial class MainWindow : Window
{
    public MainWindow(InboxViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
