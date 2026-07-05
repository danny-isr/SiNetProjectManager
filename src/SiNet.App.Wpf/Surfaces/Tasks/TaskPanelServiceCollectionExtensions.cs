using Microsoft.Extensions.DependencyInjection;

namespace SiNet.App.Wpf.Surfaces.Tasks;

/// <summary>
/// DI registration for the read-only Task Panel pilot surface.
/// </summary>
public static class TaskPanelServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetTaskPanelReadOnly(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<TaskPanelReadOnlyViewModel>();
        services.AddSingleton<ITaskPanelReadOnlyWindowFactory, TaskPanelReadOnlyWindowFactory>();

        return services;
    }
}
