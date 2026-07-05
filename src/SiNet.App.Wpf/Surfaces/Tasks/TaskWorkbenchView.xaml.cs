using System.Windows;

namespace SiNet.App.Wpf.Surfaces.Tasks;

public partial class TaskWorkbenchView : Window
{
    public TaskWorkbenchView(TaskWorkbenchViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (DataContext is TaskWorkbenchViewModel vm)
            await vm.InitializeAsync().ConfigureAwait(true);
    }
}
