using System.Windows;
using System.Windows.Input;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Surfaces.Tasks;

public partial class TaskWorkbenchView : Window
{
    public const double DefaultNarrowWidth = 400;

    public TaskWorkbenchView(TaskWorkbenchViewModel viewModel)
    {
        InitializeComponent();
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        Loaded += OnLoaded;
    }

    /// <summary>Narrow + full work-area height, docked to the right (floating workbench shape).</summary>
    public void ApplyTallNarrowLayout()
    {
        var workArea = SystemParameters.WorkArea;
        Width = DefaultNarrowWidth;
        MinWidth = 320;
        Height = workArea.Height;
        Top = workArea.Top;
        Left = workArea.Left + workArea.Width - Width;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        ApplyTallNarrowLayout();
        if (DataContext is TaskWorkbenchViewModel vm)
            await vm.InitializeAsync().ConfigureAwait(true);
    }

    private async void TaskList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
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
