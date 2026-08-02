using System.Windows.Controls;
using System.Windows.Input;

namespace SiNet.App.Wpf.Admin.WorkflowOps;

public partial class WorkflowOpsDashboardView : UserControl
{
    public WorkflowOpsDashboardView()
    {
        InitializeComponent();
    }

    private void OnRowsMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is WorkflowOpsDashboardViewModel vm)
            vm.OpenSelectedInstance();
    }
}
