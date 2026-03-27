using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Data;
using SiNetSQL.Models;
using SiNetSQL.MVVM;
using SiNetSQL.Services.Workflow;

namespace SiNetProjectManager.WPFUserControl;

/// <summary>
/// Interaction logic for WorkflowDashboardView.xaml.
/// Resolves dependencies from DI and initializes the ViewModel.
/// </summary>
public partial class WorkflowDashboardView : UserControl
{
    public WorkflowDashboardView()
    {
        InitializeComponent();

        var dbFactory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>();
        var queryService = App.ServiceProvider.GetRequiredService<WorkflowQueryService>();
        var orchestrator = App.ServiceProvider.GetRequiredService<WorkflowTaskOrchestrator>();
        var policyService = App.ServiceProvider.GetRequiredService<ProjectWorkflowPolicyService>();

        var vm = new WorkflowDashboardViewModel(dbFactory, queryService, orchestrator, policyService);
        vm.InstanceStarted = OnInstanceStarted;
        DataContext = vm;
    }

    private WorkflowDashboardViewModel ViewModel => (WorkflowDashboardViewModel)DataContext;

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
    }

    private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedInstance is not { } instance) return;

        OpenInstanceWindow(instance.Id);
    }

    private void OnInstanceStarted(int instanceId)
    {
        OpenInstanceWindow(instanceId);
    }

    private void OpenInstanceWindow(int instanceId)
    {
        var window = new WPF_Window.WorkflowInstanceWindow(instanceId)
        {
            Owner = Window.GetWindow(this)
        };
        window.Show();
    }
}
