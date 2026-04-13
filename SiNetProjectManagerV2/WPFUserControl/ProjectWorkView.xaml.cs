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

    public ProjectWorkView()
    {
        InitializeComponent();
        var dialogs = App.DialogServiceLocator.Instance
                      ?? new SiNetProjectManagerV2.Services.DialogService();
        DataContext = new ProjectWorkViewModel(dialogs);

        // Wire VersionNode ACC callback → navigate the right-panel AccWebView
        VersionNode.OnAccFileOpenRequested = url =>
        {
            if (DataContext is ProjectWorkViewModel vm)
                vm.AccViewerUrl = url;
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
                // Navigate ACC viewer panel
                if (DataContext is ProjectWorkViewModel vm)
                    vm.AccViewerUrl = version.AccViewerUrl;
            }
            else if (version.IsAccFile)
            {
                // ACC file but no viewer URL (project not mapped to ACC)
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
    }
}
