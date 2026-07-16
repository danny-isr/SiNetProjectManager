using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SiNet.Application.ProjectWork;
using SiNet.Application.WorkSurfaces;

namespace SiNet.App.Wpf.Surfaces.ProjectWork;

/// <summary>
/// Window for the native ProjectWork task-execution surface. Chrome only; task/completion logic
/// lives in <see cref="ProjectWorkWindowViewModel"/>.
/// </summary>
public partial class ProjectWorkWindowView : Window
{
    /// <summary>Design/standalone constructor.</summary>
    public ProjectWorkWindowView()
        : this(new ProjectWorkWindowViewModel())
    {
    }

    /// <summary>Primary constructor: binds to the supplied view model.</summary>
    private IAccViewerHost? _accViewerHost;
    private Point _dragStart;

    public ProjectWorkWindowView(ProjectWorkWindowViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;

        FileTree.PreviewMouseLeftButtonDown += (_, e) => _dragStart = e.GetPosition(null);
        FileTree.PreviewMouseMove += FileTree_PreviewMouseMove;
        FileTree.AllowDrop = true;
        FileTree.DragOver += FileTree_DragOver;
        FileTree.Drop += FileTree_Drop;

        Closed += (_, _) =>
        {
            _accViewerHost?.Clear();
            ViewModel.Dispose();
        };
    }

    // Drag-in: dropping OS files onto a file/alternative/version node adds/replaces a version.
    private void FileTree_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && ResolveDropTarget(e) is not null
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void FileTree_Drop(object sender, DragEventArgs e)
    {
        if (ViewModel.Tree is not { } tree)
            return;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths)
            return;

        var target = ResolveDropTarget(e);
        if (target is null)
            return;

        foreach (var path in paths)
        {
            if (File.Exists(path))
                await tree.HandleFileDropAsync(target, path);
        }
    }

    private ProjectWorkNodeVm? ResolveDropTarget(DragEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
            return null;
        var item = FindAncestor<TreeViewItem>(source);
        return item?.DataContext as ProjectWorkNodeVm;
    }

    // Drag-out: dragging a FileServer version node onto Explorer / another app copies the file.
    private void FileTree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (e.OriginalSource is not DependencyObject source)
            return;

        var item = FindAncestor<TreeViewItem>(source);
        if (item?.DataContext is not VersionNodeVm version)
            return;

        if (string.IsNullOrEmpty(version.FullPath) || !File.Exists(version.FullPath))
            return; // ACC / missing versions can't be dragged to the file system.

        var data = new DataObject(DataFormats.FileDrop, new[] { version.FullPath });
        try
        {
            DragDrop.DoDragDrop(item, data, DragDropEffects.Copy);
        }
        catch
        {
            // A cancelled / failed drag must not crash the surface.
        }
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T typed)
                return typed;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    /// <summary>
    /// Attaches the host-provided embedded ACC viewer (WebView2) to the right-pane host element.
    /// Called by <see cref="ProjectWorkWindowFactory"/>; a <see langword="null"/> host keeps the
    /// external-browser fallback (see <c>ProjectWorkTreeViewModel</c>).
    /// </summary>
    public void SetAccViewerHost(IAccViewerHost? accViewerHost)
    {
        _accViewerHost = accViewerHost;
        if (accViewerHost is { IsAvailable: true })
            accViewerHost.AttachHost(AccViewerHost);
    }

    /// <summary>The bound view model.</summary>
    public ProjectWorkWindowViewModel ViewModel { get; }

    /// <summary>Task-mode entry. Prefer <see cref="ApplyContextAsync"/> to await load completion.</summary>
    public void ApplyContext(WorkSurfaceContext? context) => ViewModel.ApplyContext(context);

    /// <summary>Task-mode entry that awaits context load (validates key + project; no fallback).</summary>
    public Task<bool> ApplyContextAsync(WorkSurfaceContext? context, CancellationToken cancellationToken = default)
        => ViewModel.ApplyContextAsync(context, cancellationToken);

    /// <summary>Browse-mode entry (menu): no task strip; loads tree from current project when set.</summary>
    public Task OpenBrowseModeAsync(CancellationToken cancellationToken = default)
        => ViewModel.OpenBrowseModeAsync(cancellationToken);
}
