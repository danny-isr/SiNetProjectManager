using System.Windows;
using System.Windows.Input;
using SiNet.App.Wpf.Infrastructure;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Surfaces.Workflow;

public partial class WorkflowVisualCanvasWindow : Window
{
    private const double DragThresholdPx = 4;

    private Rect _restoreBounds;
    private bool _isCustomMaximized;
    private WorkflowCanvasNodeVm? _dragNode;
    private Point _dragStartOnCanvas;
    private Point _nodeOrigin;
    private bool _dragMoved;

    public WorkflowVisualCanvasWindow()
        : this(new WorkflowVisualCanvasViewModel())
    {
    }

    public WorkflowVisualCanvasWindow(WorkflowVisualCanvasViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
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

    private void CanvasScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        ViewModel.AdjustZoom(e.Delta > 0
            ? WorkflowVisualCanvasViewModel.ZoomStepFactor
            : 1.0 / WorkflowVisualCanvasViewModel.ZoomStepFactor);
        e.Handled = true;
    }

    private void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not WorkflowCanvasNodeVm node)
        {
            return;
        }

        _dragNode = node;
        _dragStartOnCanvas = e.GetPosition(GraphCanvas);
        _nodeOrigin = new Point(node.X, node.Y);
        _dragMoved = false;
        element.CaptureMouse();
        e.Handled = true;
    }

    private void Node_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragNode is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var pos = e.GetPosition(GraphCanvas);
        var dx = pos.X - _dragStartOnCanvas.X;
        var dy = pos.Y - _dragStartOnCanvas.Y;
        if (!_dragMoved && (Math.Abs(dx) > DragThresholdPx || Math.Abs(dy) > DragThresholdPx))
        {
            _dragMoved = true;
        }

        if (_dragMoved)
        {
            ViewModel.MoveNode(_dragNode, _nodeOrigin.X + dx, _nodeOrigin.Y + dy);
        }

        e.Handled = true;
    }

    private void Node_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragNode is not null && !_dragMoved)
        {
            ViewModel.SelectStage(_dragNode.Id);
        }

        if (sender is FrameworkElement element && element.IsMouseCaptured)
        {
            element.ReleaseMouseCapture();
        }

        _dragNode = null;
        _dragMoved = false;
        e.Handled = true;
    }

    private void Node_LostMouseCapture(object sender, MouseEventArgs e)
    {
        _dragNode = null;
        _dragMoved = false;
    }

    private void Edge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is WorkflowCanvasEdgeVm edge)
        {
            ViewModel.SelectTransition(edge.TransitionId);
            e.Handled = true;
        }
    }

    private void GraphCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Background click clears selection (child handlers mark Handled for nodes/edges).
        if (e.OriginalSource == GraphCanvas)
        {
            ViewModel.ClearSelection();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ViewModel.ClearSelection();
            e.Handled = true;
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
