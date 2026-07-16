using System.Windows;
using System.Windows.Input;

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

    private async void TaskGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not TaskWorkbenchViewModel vm)
            return;

        if (vm.OpenTaskCommand.CanExecute(null))
            await vm.OpenSelectedTaskAsync().ConfigureAwait(true);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is IDisposable disposable)
            disposable.Dispose();

        base.OnClosed(e);
    }
}
