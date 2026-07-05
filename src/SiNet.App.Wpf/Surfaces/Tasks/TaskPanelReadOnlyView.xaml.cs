using System.Windows;

namespace SiNet.App.Wpf.Surfaces.Tasks;

public partial class TaskPanelReadOnlyView : Window
{
    public TaskPanelReadOnlyView(TaskPanelReadOnlyViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (DataContext is TaskPanelReadOnlyViewModel vm)
            await vm.InitializeAsync().ConfigureAwait(true);
    }
}
