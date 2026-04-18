using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.MVVM;

namespace SiNetProjectManagerV2.WPFUserControl;

/// <summary>
/// Code-behind for the Workflow Visual Designer canvas.
/// Handles only interactions that cannot be done in XAML bindings:
/// node drag, node/connector click selection, zoom, and keyboard shortcuts.
/// </summary>
public partial class WorkflowDesignerView : UserControl
{
    // ── Drag state ──
    private bool _isDragging;
    private Point _dragStart;
    private DesignerNodeViewModel? _dragNode;

    public WorkflowDesignerView()
    {
        InitializeComponent();
    }

    private WorkflowDesignerViewModel? ViewModel => DataContext as WorkflowDesignerViewModel;

    // ═══════════════════════════════════════════════════════════════════════
    //  Loaded
    // ═══════════════════════════════════════════════════════════════════════

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is null)
        {
            var vm = App.ServiceProvider.GetRequiredService<WorkflowDesignerViewModel>();
            DataContext = vm;
        }

        if (ViewModel is not null)
        {
            await ViewModel.InitializeAsync();
        }

        Focus();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Node interactions
    // ═══════════════════════════════════════════════════════════════════════

    private void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not DesignerNodeViewModel node) return;
        if (ViewModel is null) return;

        if (ViewModel.IsConnecting)
        {
            ViewModel.EndConnectCommand.Execute(node);
            e.Handled = true;
            return;
        }

        ViewModel.SelectedNode = node;

        _isDragging = true;
        _dragNode = node;
        _dragStart = e.GetPosition(CanvasRoot);
        fe.CaptureMouse();
        e.Handled = true;
    }

    private void Node_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || _dragNode is null || ViewModel is null) return;

        var pos = e.GetPosition(CanvasRoot);
        var dx = pos.X - _dragStart.X;
        var dy = pos.Y - _dragStart.Y;

        _dragNode.X = Math.Max(0, _dragNode.X + dx);
        _dragNode.Y = Math.Max(0, _dragNode.Y + dy);
        _dragStart = pos;

        ViewModel.HasUnsavedChanges = true;
    }

    private void Node_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        _dragNode = null;
        if (sender is FrameworkElement fe) fe.ReleaseMouseCapture();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Connector click
    // ═══════════════════════════════════════════════════════════════════════

    private void Connector_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not DesignerConnectorViewModel conn) return;
        if (ViewModel is null) return;

        ViewModel.SelectedConnector = conn;
        e.Handled = true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Canvas click (deselect)
    // ═══════════════════════════════════════════════════════════════════════

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is null) return;

        // If connecting, clicking empty canvas cancels
        if (ViewModel.IsConnecting)
        {
            ViewModel.CancelConnectCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Deselect everything
        ViewModel.SelectedNode = null;
        ViewModel.SelectedConnector = null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Zoom via Ctrl+MouseWheel
    // ═══════════════════════════════════════════════════════════════════════

    private void Canvas_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;

        var factor = e.Delta > 0 ? 1.1 : 0.9;
        var newScale = Math.Clamp(CanvasScale.ScaleX * factor, 0.3, 3.0);

        CanvasScale.ScaleX = newScale;
        CanvasScale.ScaleY = newScale;
        e.Handled = true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Keyboard shortcuts
    // ═══════════════════════════════════════════════════════════════════════

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (ViewModel is null) { base.OnPreviewKeyDown(e); return; }

        switch (e.Key)
        {
            case Key.Escape when ViewModel.IsConnecting:
                ViewModel.CancelConnectCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Delete:
                if (ViewModel.SelectedNode is not null)
                    ViewModel.DeleteNodeCommand.Execute(null);
                else if (ViewModel.SelectedConnector is not null)
                    ViewModel.DeleteConnectorCommand.Execute(null);
                e.Handled = true;
                break;
        }

        base.OnPreviewKeyDown(e);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Help
    // ═══════════════════════════════════════════════════════════════════════

    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        var help = new WorkflowDesignerHelpWindow
        {
            Owner = Window.GetWindow(this)
        };
        help.Show();
    }
}
