using System.Windows.Controls;
using System.Windows.Input;

namespace SiNet.App.Wpf.Projects.Dashboard;

public partial class ProjectsDashboardView : UserControl
{
    public ProjectsDashboardView()
    {
        InitializeComponent();
    }

    private void OnProjectsGridMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ProjectsDashboardViewModel vm
            && vm.OpenSelectedCommand.CanExecute(null))
        {
            vm.OpenSelectedCommand.Execute(null);
        }
    }
}
