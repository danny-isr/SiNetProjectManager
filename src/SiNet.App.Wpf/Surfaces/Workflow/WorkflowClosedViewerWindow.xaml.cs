using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SiNet.App.Wpf.Infrastructure;
using SiNet.App.Wpf.Theme;

namespace SiNet.App.Wpf.Surfaces.Workflow;

/// <summary>
/// Native closed-world workflow viewer window (chrome + tree selection glue only).
/// </summary>
public partial class WorkflowClosedViewerWindow : Window
{
    private Rect _restoreBounds;
    private bool _isCustomMaximized;

    public WorkflowClosedViewerWindow()
        : this(new WorkflowClosedViewerViewModel())
    {
    }

    public WorkflowClosedViewerWindow(WorkflowClosedViewerViewModel viewModel)
    {
        InitializeComponent();
        ThemeWindowChrome.ApplyThemedWindowBackground(this);
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = ViewModel;
        Loaded += OnLoaded;
        UpdateMaximizeButtonGlyph();
    }

    public WorkflowClosedViewerViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        try
        {
            await ViewModel.LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppErrorReporter.Report(ex, "WorkflowClosedViewerWindow.OnLoaded");
        }
    }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        ViewModel.SelectedNode = e.NewValue as WorkflowViewerNode;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || IsUnderChromeButton(e.OriginalSource))
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            e.Handled = true;
            return;
        }

        if (_isCustomMaximized)
        {
            return;
        }

        DragMove();
    }

    private static bool IsUnderChromeButton(object? originalSource)
    {
        for (var current = originalSource as DependencyObject;
             current is not null;
             current = System.Windows.Media.VisualTreeHelper.GetParent(current))
        {
            if (current is Button)
            {
                return true;
            }
        }

        return false;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        if (_isCustomMaximized)
        {
            RestoreFromCustomMaximize();
        }
        else
        {
            MaximizeToWorkArea();
        }
    }

    private void MaximizeToWorkArea()
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
        UpdateMaximizeButtonGlyph();
    }

    private void RestoreFromCustomMaximize()
    {
        Left = _restoreBounds.Left;
        Top = _restoreBounds.Top;
        Width = _restoreBounds.Width;
        Height = _restoreBounds.Height;
        ContentBorder.Margin = new Thickness(8);
        ContentBorder.CornerRadius = new CornerRadius(8);
        _isCustomMaximized = false;
        ResizeMode = ResizeMode.CanResizeWithGrip;
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
