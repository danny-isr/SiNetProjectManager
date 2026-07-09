using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Surfaces.Inspection;

namespace SiNet.App.Wpf.WorkSurfaces;

public static class WorkSurfaceServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetWorkSurfaces(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<InspectionWindowViewModel>();
        services.AddSingleton<IInspectionWindowFactory, InspectionWindowFactory>();
        services.AddSingleton<IWorkSurfaceLauncher, WorkSurfaceLauncher>();

        return services;
    }
}
