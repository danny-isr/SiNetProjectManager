using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Workflow;
using SiNetSQL.MVVM;
using SiNetSQL.Services.Workflow;

namespace SiNetProjectManagerV2.WPF_Window;

/// <summary>
/// Interaction logic for WorkflowInstanceWindow.xaml.
/// Displays a single workflow instance with stages, history, and action controls.
/// </summary>
public partial class WorkflowInstanceWindow : Window
{
    private readonly int _instanceId;

    public WorkflowInstanceWindow(int instanceId)
    {
        InitializeComponent();
        _instanceId = instanceId;

        var queryService = App.ServiceProvider.GetRequiredService<IWorkflowQueryService>();
        var workflowCommands = App.ServiceProvider.GetRequiredService<IWorkflowCommandService>();

        DataContext = new WorkflowInstanceViewModel(queryService, workflowCommands);
    }

    private WorkflowInstanceViewModel ViewModel => (WorkflowInstanceViewModel)DataContext;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync(_instanceId);

        if (ViewModel.Instance is not null)
        {
            Title = $"תהליך: {ViewModel.DefinitionName}";
        }
    }
}
