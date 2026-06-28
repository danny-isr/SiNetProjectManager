using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Workflow;
using SiNetSQL.MVVM;
using SiNetSQL.Services.Workflow;

namespace SiNetProjectManagerV2.WPF_Window;

/// <summary>
/// Floating always-on-top window that monitors all workflow instances
/// across the entire system, grouped by workflow type.
/// Auto-refreshes every 15 seconds.
/// </summary>
public partial class WorkflowStatusMonitorWindow : Window
{
    public WorkflowStatusMonitorWindow()
    {
        InitializeComponent();

        var queryService = App.ServiceProvider.GetRequiredService<IWorkflowQueryService>();
        var vm = new WorkflowStatusViewModel(queryService);
        vm.InstanceRequested = OnInstanceRequested;
        DataContext = vm;
    }

    private WorkflowStatusViewModel ViewModel => (WorkflowStatusViewModel)DataContext;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
        ViewModel.StartAutoRefresh();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        ViewModel.StopAutoRefresh();
    }

    private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedItem is { InstanceId: var id })
        {
            OpenInstanceWindow(id);
        }
    }

    private void OnInstanceRequested(int instanceId)
    {
        OpenInstanceWindow(instanceId);
    }

    private void OpenInstanceWindow(int instanceId)
    {
        var window = new WorkflowInstanceWindow(instanceId) { Owner = this };
        window.Show();
    }

    private void FilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not WorkflowStatusViewModel vm) return;
        if (FilterCombo.SelectedItem is not ComboBoxItem item) return;

        vm.SelectedFilter = item.Tag?.ToString() switch
        {
            "All" => StatusFilterOption.All,
            "ActiveOnly" => StatusFilterOption.ActiveOnly,
            "Completed" => StatusFilterOption.Completed,
            "Draft" => StatusFilterOption.Draft,
            _ => StatusFilterOption.ActiveOnly
        };
    }

    private void Pin_Checked(object sender, RoutedEventArgs e) => Topmost = true;
    private void Pin_Unchecked(object sender, RoutedEventArgs e) => Topmost = false;
}
