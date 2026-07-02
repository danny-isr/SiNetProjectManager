using System.Windows;
using System.Windows.Controls;

namespace SiNet.App.Wpf.Admin.Permissions;

public partial class ActionPermissionsView : UserControl
{
    public ActionPermissionsView()
    {
        InitializeComponent();
    }

    private void UserCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ActionPermissionsViewModel vm)
        {
            return;
        }

        if (sender is CheckBox { DataContext: ActionPermissionUserRow row })
        {
            vm.OnUserAuthorizationChanged(row);
        }
    }
}
