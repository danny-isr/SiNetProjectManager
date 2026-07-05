using Microsoft.Extensions.DependencyInjection;

namespace SiNet.App.Wpf.Surfaces.Tasks;

/// <summary>
/// DI registration for the Task Workbench surface.
/// </summary>
public static class TaskPanelServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetTaskPanelReadOnly(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<TaskCreateDialogViewModel>();
        services.AddTransient<ITaskCreateDialogFactory, TaskCreateDialogFactory>();
        services.AddTransient<TaskWorkbenchViewModel>();
        services.AddSingleton<ITaskPanelReadOnlyWindowFactory, TaskPanelReadOnlyWindowFactory>();

        return services;
    }
}
