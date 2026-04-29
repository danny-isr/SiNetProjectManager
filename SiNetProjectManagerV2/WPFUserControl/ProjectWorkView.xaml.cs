using SiNetSQL.MVVM;
using SiNetSQL.MyClass;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SiNetProjectManagerV2.WPFUserControl;

/// <summary>
/// "בעבודה 2" — Unified tree view with folders containing files inline,
/// plus ACC WebView2 panel on the right.
/// </summary>
public partial class ProjectWorkView : UserControl
{
    private Point _dragStartPoint;

    /// <summary>Floating window hosting the ACC viewer panel when popped out.</summary>
    private Window? _accFloatWindow;

    public ProjectWorkView()
    {
        InitializeComponent();
        var dialogs = App.DialogServiceLocator.Instance
                      ?? new SiNetProjectManagerV2.Services.DialogService();
        var vm = new ProjectWorkViewModel(dialogs);
        DataContext = vm;

        // Wire VersionNode ACC callback → navigate the right-panel AccWebView
        VersionNode.OnAccFileOpenRequested = url =>
        {
            if (DataContext is ProjectWorkViewModel current)
                current.AccViewerUrl = url;
        };

        // Wire AlternativeNode checkbox hooks → open / close ACC viewer tabs.
        // The static hooks are app-wide singletons, so always resolve the current
        // DataContext lazily to support DataContext swaps.
        AlternativeNode.OnAccTabOpenRequested = alt =>
        {
            if (DataContext is ProjectWorkViewModel current)
                current.OpenOrActivateAccTab(alt);
        };
        AlternativeNode.OnAccTabCloseRequested = alt =>
        {
            if (DataContext is ProjectWorkViewModel current)
                current.CloseAccTabForAlternative(alt);
        };
    }

    private void TreeViewItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    private void TreeViewItem_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            var currentPos = e.GetPosition(null);
            if (Math.Abs(currentPos.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(currentPos.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                var item = sender as TreeViewItem;
                if (item?.DataContext is VersionNode vm && !string.IsNullOrEmpty(vm.FullPath))
                {
                    var data = new DataObject(DataFormats.FileDrop, new[] { vm.FullPath });
                    DragDrop.DoDragDrop(item, data, DragDropEffects.Copy);
                }
            }
        }
    }

    private void TreeViewItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)e.OriginalSource).DataContext is VersionNode version)
        {
            if (version.IsAccFile && !string.IsNullOrEmpty(version.AccViewerUrl))
            {
                // Open or activate an ACC viewer tab for the parent alternative.
                if (DataContext is ProjectWorkViewModel vm && version.Parent is AlternativeNode alt)
                    vm.OpenOrActivateAccTab(alt);
            }
            else if (version.IsAccFile)
            {
                MessageBox.Show(
                    "הקובץ שמור ב-ACC אך לא ניתן לפתוח אותו — חסר מיפוי פרויקט ב-ACC.",
                    "קובץ ACC", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                FileHelpers.OpenFile(version.FullPath);
            }
            e.Handled = true;
        }
        else if (((FrameworkElement)e.OriginalSource).DataContext is AlternativeNode alternative)
        {
            // Double-click on an alternative with ACC versions also opens/activates its tab.
            if (alternative.HasAccVersions && DataContext is ProjectWorkViewModel vm)
            {
                vm.OpenOrActivateAccTab(alternative);
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// Toggles the ACC viewer panel between docked (inside this UserControl)
    /// and floating (a top-level Window). The visual subtree (including all
    /// WebView2 instances) is moved — not recreated — so navigation state
    /// and loaded ACC documents are preserved across pop-out / dock.
    /// </summary>
    private void AccDockToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_accFloatWindow is null)
            PopOutAccViewer();
        else
            DockAccViewer();
    }

    private void PopOutAccViewer()
    {
        // Detach the inner Grid from the docked host Border, then attach it
        // to a fresh floating Window. Disconnecting the parent first is required
        // because a WPF visual can have only one logical/visual parent.
        AccViewerDockHost.Child = null;

        // The inner grid inherits DataContext from the UserControl while docked.
        // Once it moves to a top-level Window it no longer inherits anything, so
        // bindings to AccViewerTabs/HasAccTabs would silently break and the tab
        // strip + WebView2 items would disappear (and be recreated on dock-back).
        // Pin the DataContext explicitly to preserve all bindings across the move.
        AccViewerInner.DataContext = DataContext;

        var owner = Window.GetWindow(this);
        _accFloatWindow = new Window
        {
            Title = "ACC - תצוגת קבצים",
            Owner = owner,
            DataContext = DataContext,
            Width = Math.Max(800, (owner?.Width ?? 1200) * 0.7),
            Height = Math.Max(600, (owner?.Height ?? 800) * 0.8),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = AccViewerInner
        };
        _accFloatWindow.Closed += (_, _) =>
        {
            // If the user closes the float window with the OS X, dock back
            // so the WebView2 instances are not orphaned/destroyed.
            if (_accFloatWindow != null) DockAccViewer();
        };
        _accFloatWindow.Show();
    }

    private void DockAccViewer()
    {
        if (_accFloatWindow is null) return;

        // Move the inner grid back into the docked host before closing
        // the float window so the visual subtree is never disposed.
        _accFloatWindow.Content = null;
        AccViewerDockHost.Child = AccViewerInner;

        // Clear the explicit DataContext pin so the grid resumes inheriting
        // from the UserControl (which is the canonical source while docked).
        AccViewerInner.ClearValue(FrameworkElement.DataContextProperty);

        var win = _accFloatWindow;
        _accFloatWindow = null;
        if (win.IsLoaded) win.Close();
    }
}
