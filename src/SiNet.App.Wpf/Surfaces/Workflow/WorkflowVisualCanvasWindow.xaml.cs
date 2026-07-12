using System.Windows;
using System.Windows.Input;
using SiNet.App.Wpf.Infrastructure;

namespace SiNet.App.Wpf.Surfaces.Workflow;

public partial class WorkflowVisualCanvasWindow : Window
{
    private Rect _restoreBounds;
    private bool _isCustomMaximized;

    public WorkflowVisualCanvasWindow()
        : this(new WorkflowVisualCanvasViewModel())
    {
    }

    public WorkflowVisualCanvasWindow(WorkflowVisualCanvasViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;
        Loaded += OnLoaded;
        UpdateMaximizeButtonGlyph();
    }

    public WorkflowVisualCanvasViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        try
        {
            await ViewModel.LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppErrorReporter.Report(ex, "WorkflowVisualCanvasWindow.OnLoaded");
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            e.Handled = true;
            return;
        }

        if (!_isCustomMaximized)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        if (_isCustomMaximized)
        {
            Left = _restoreBounds.Left;
            Top = _restoreBounds.Top;
            Width = _restoreBounds.Width;
            Height = _restoreBounds.Height;
            ContentBorder.Margin = new Thickness(8);
            ContentBorder.CornerRadius = new CornerRadius(8);
            _isCustomMaximized = false;
            ResizeMode = ResizeMode.CanResizeWithGrip;
        }
        else
        {
            _restoreBounds = new Rect(Left, Top, Width, Height);
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left;
            Top = workArea.Top;
            Width = workArea.Width;
            Height = workArea.Height;
            ContentBorder.Margin = new Thickness(0);
            ContentBorder.CornerRadius = new CornerRadius(0);
            _isCustomMaximized = true;
            ResizeMode = ResizeMode.NoResize;
        }

        UpdateMaximizeButtonGlyph();
    }

    private void UpdateMaximizeButtonGlyph()
    {
        if (MaximizeButton is null)
        {
            return;
        }

        MaximizeButton.Content = _isCustomMaximized ? "\u29C9" : "\u25A1";
        MaximizeButton.ToolTip = _isCustomMaximized ? "שחזר" : "הגדל";
    }
}
