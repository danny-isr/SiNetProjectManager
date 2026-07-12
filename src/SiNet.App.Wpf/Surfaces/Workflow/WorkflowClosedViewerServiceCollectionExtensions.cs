using Microsoft.Extensions.DependencyInjection;

namespace SiNet.App.Wpf.Surfaces.Workflow;

/// <summary>
/// DI registration for the native closed-world workflow viewer surface.
/// </summary>
public static class WorkflowClosedViewerServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetWorkflowClosedViewer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<WorkflowClosedViewerViewModel>();
        services.AddSingleton<IWorkflowClosedViewerWindowFactory, WorkflowClosedViewerWindowFactory>();

        return services;
    }
}
