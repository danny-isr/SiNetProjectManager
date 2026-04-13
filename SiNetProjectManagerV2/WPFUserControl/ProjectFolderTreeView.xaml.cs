using SiNetSQL.MVVM;
using SiNetSQL.MyClass;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static SiNetProjectManagerV2.App;

namespace SiNetProjectManagerV2.WPFUserControl
{
    /// <summary>
    /// Interaction logic for ProjectFolderTreeView.xaml
    /// </summary>
    public partial class ProjectFolderTreeView : UserControl
    {
        private Point _dragStartPoint;
        public ProjectFolderTreeView()
        {
            InitializeComponent();
            var dialogs = SiNetProjectManagerV2.App.DialogServiceLocator.Instance ?? new SiNetProjectManagerV2.Services.DialogService();
            DataContext = new ProjectFolderTreeViewModel(dialogs);
        }
        // כשעוברים על ה־TreeView עצמו
        //private void TreeView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        //{
        //    // הדביאו את האירוע ל־ScrollViewer החיצוני
        //    var args = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        //    {
        //        RoutedEvent = UIElement.PreviewMouseWheelEvent
        //    };
        //    FoldersScrollViewer.RaiseEvent(args);
        //    e.Handled = true;
        //}

        // טופס גלגלת ישיר ב־ScrollViewer
        private void FoldersScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = (ScrollViewer)sender;
            // גלגלת מחזורית: e.Delta חיובי למעלה, שלילי למטה
            sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
            e.Handled = true;
        }
        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is ProjectFolderTreeViewModel viewModel && e.NewValue is ProjectFolderNode node)
            {
                viewModel.SelectedNode = node;
            }
        }

        private void TreeViewItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // שומרים את מיקום העכבר בתחילת לחיצה
            _dragStartPoint = e.GetPosition(null);
        }

        private void TreeViewItem_MouseMove(object sender, MouseEventArgs e)
        {
            // רק אם יש לחיצה ממושכת וניידים מעבר לסף
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPos = e.GetPosition(null);
                if (Math.Abs(currentPos.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(currentPos.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var item = sender as TreeViewItem;
                    if (item?.DataContext is VersionNode vm && !string.IsNullOrEmpty(vm.FullPath))
                    {
                        // יוצרים DataObject מסוג FileDrop עם מערך של נתיבים
                        var data = new DataObject(DataFormats.FileDrop, new[] { vm.FullPath });
                        // מפעילים את ה־Drag
                        DragDrop.DoDragDrop(item, data, DragDropEffects.Copy);
                    }
                }
            }
        }

        private void TreeViewItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // כדי לוודא שנלחץ באמת על ה־VersionNode:
            if (((FrameworkElement)e.OriginalSource).DataContext is VersionNode version)
            {
                FileHelpers.OpenFile(version.FullPath);
                e.Handled = true;
            }
        }


        private void FilesTreeView_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void FilesTreeView_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                Point position = e.GetPosition(FilesTreeView);
                HitTestResult result = VisualTreeHelper.HitTest(FilesTreeView, position);

                if (result != null)
                {
                    DependencyObject current = result.VisualHit;
                    while (current != null && current is not TreeViewItem)
                        current = VisualTreeHelper.GetParent(current);

                    if (current is TreeViewItem tvi)
                    {
                        if (tvi.DataContext is VersionNode)
                        {
                            // אסור לשחרר על גרסה
                            e.Effects = DragDropEffects.None;
                            e.Handled = true;
                            return;
                        }
                        // מותר לשחרר על אלטרנטיבה או פרויקט
                        e.Effects = DragDropEffects.Copy;
                        e.Handled = true;
                        return;
                    }
                }
            }

            // אם לא קבצים, או אם לא מזהים את ה־Target
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void FilesTreeView_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] droppedFiles = (string[])e.Data.GetData(DataFormats.FileDrop);
            var firstFile = droppedFiles.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f));
            if (string.IsNullOrWhiteSpace(firstFile)) return;

            // למצוא את המיקום של ה־Drop בעץ
            Point dropPosition = e.GetPosition(FilesTreeView);
            HitTestResult result = VisualTreeHelper.HitTest(FilesTreeView, dropPosition);

            if (result == null) return;

            // למצוא את ה־TreeViewItem שעליו נעשה ה־Drop
            DependencyObject current = result.VisualHit;
            while (current != null && current is not TreeViewItem)
                current = VisualTreeHelper.GetParent(current);

            if (current is not TreeViewItem treeViewItem)
            {
                MessageBox.Show("Dropped outside of any item.");
                return;
            }

            object targetNode = treeViewItem.DataContext;
            if (targetNode is AlternativeNode alternativeNode)
            {
                alternativeNode.Parent.GetAlternativeNode(alternativeNode.AlternativeName, firstFile);
            }
            else if (targetNode is ProjectFileNode projectNode)
            {
                projectNode.GetAlternativeNode("2", firstFile);
            }
            else
            {
                MessageBox.Show("Drop target is not recognized.");
            }
        }
    }
}

