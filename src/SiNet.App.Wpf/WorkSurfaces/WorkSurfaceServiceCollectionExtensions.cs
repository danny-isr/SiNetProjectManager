using Microsoft.Extensions.DependencyInjection;

namespace SiNet.App.Wpf.WorkSurfaces;

public static class WorkSurfaceServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetWorkSurfaces(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IWorkSurfaceLauncher, WorkSurfaceLauncher>();

        return services;
    }
}
