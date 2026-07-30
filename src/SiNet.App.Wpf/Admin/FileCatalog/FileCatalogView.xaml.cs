using System.Windows;
using System.Windows.Controls;

namespace SiNet.App.Wpf.Admin.FileCatalog;

public partial class FileCatalogView : UserControl
{
    public FileCatalogView()
    {
        InitializeComponent();
    }

    private void FolderTree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is FileCatalogViewModel vm && e.NewValue is FileCatalogFolderNodeVm folder)
            vm.SelectedFolder = folder;
    }
}
