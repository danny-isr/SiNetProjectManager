using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SiNet.App.Wpf.Admin.FileCatalog;

public partial class FileCatalogView : UserControl
{
    private bool _suppressFolderFilterFromRightClick;

    public FileCatalogView()
    {
        InitializeComponent();
    }

    private void FolderTree_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Context menu (assign / add sub-folder) must not change the files-grid folder filter.
        _suppressFolderFilterFromRightClick = true;
    }

    private void FolderTree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is not FileCatalogViewModel vm)
            return;
        if (e.NewValue is not FileCatalogFolderNodeVm folder)
            return;

        if (_suppressFolderFilterFromRightClick
            || Mouse.RightButton == MouseButtonState.Pressed)
        {
            _suppressFolderFilterFromRightClick = false;
            return;
        }

        vm.ApplyFolderFilter(folder);
    }
}
