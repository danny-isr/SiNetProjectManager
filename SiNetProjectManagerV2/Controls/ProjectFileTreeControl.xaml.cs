using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SiNetSQL.MVVM;
using SiNetSQL.MyClass;

namespace SiNetProjectManagerV2.Controls;

/// <summary>
/// Reusable file tree control — displays ProjectFileNode → AlternativeNode → VersionNode hierarchy.
/// Supports Drag&amp;Drop (drag out files, drop files onto alternatives) and double-click to open.
/// 
/// Usage:
///   &lt;controls:ProjectFileTreeControl
///       ProjectFiles="{Binding ProjectFiles}"
///       FileDropCommand="{Binding HandleFileDropCommand}" /&gt;
/// 
/// Events:
///   FileDoubleClicked — raised when user double-clicks a VersionNode.
/// </summary>
public partial class ProjectFileTreeControl : UserControl
{
    #region Dependency Properties

    public static readonly DependencyProperty ProjectFilesProperty = DependencyProperty.Register(
        nameof(ProjectFiles),
        typeof(ObservableCollection<ProjectFileNode>),
        typeof(ProjectFileTreeControl),
        new PropertyMetadata(null));

    /// <summary>
    /// The collection of file nodes to display in the tree.
    /// </summary>
    public ObservableCollection<ProjectFileNode>? ProjectFiles
    {
        get => (ObservableCollection<ProjectFileNode>?)GetValue(ProjectFilesProperty);
        set => SetValue(ProjectFilesProperty, value);
    }

    public static readonly DependencyProperty FileDropCommandProperty = DependencyProperty.Register(
        nameof(FileDropCommand),
        typeof(ICommand),
        typeof(ProjectFileTreeControl),
        new PropertyMetadata(null));

    /// <summary>
    /// Command executed when a file is dropped onto the tree. Parameter: FileDropInfo.
    /// </summary>
    public ICommand? FileDropCommand
    {
        get => (ICommand?)GetValue(FileDropCommandProperty);
        set => SetValue(FileDropCommandProperty, value);
    }

    #endregion

    #region Events

    /// <summary>
    /// Raised when a user double-clicks a VersionNode. The parent can handle opening the file.
    /// </summary>
    public event EventHandler<VersionNode>? FileDoubleClicked;

    #endregion

    private Point _dragStartPoint;

    public ProjectFileTreeControl()
    {
        InitializeComponent();
    }

    #region Event Handlers (Drag & Drop, Double-Click)

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
                if (sender is TreeViewItem { DataContext: VersionNode vm } item && !string.IsNullOrEmpty(vm.FullPath))
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
            FileDoubleClicked?.Invoke(this, version);

            // Default behavior: open the file if no one handled the event
            if (FileDoubleClicked == null)
            {
                FileHelpers.OpenFile(version.FullPath);
            }

            e.Handled = true;
        }
    }

    #endregion
}
