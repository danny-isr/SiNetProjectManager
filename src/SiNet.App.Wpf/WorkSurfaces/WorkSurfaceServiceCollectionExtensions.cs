using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Surfaces.Inspection;
using SiNet.Application.Abstractions.Inspection;

namespace SiNet.App.Wpf.WorkSurfaces;

public static class WorkSurfaceServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetWorkSurfaces(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<InspectionWindowViewModel>();
        services.AddSingleton<IInspectionWindowFactory, InspectionWindowFactory>();
        services.AddSingleton<IWorkSurfaceLauncher, WorkSurfaceLauncher>();

        services.AddSingleton<IInspectionFileTreePickerHost, NoOpInspectionFileTreePickerHost>();
        services.AddSingleton<IInspectionReportEmailHost, NoOpInspectionReportEmailHost>();
        services.AddSingleton<IInspectionNoteScreenshotHost, NoOpInspectionNoteScreenshotHost>();
        services.AddSingleton<IInspectionTemplateCatalog, EmptyInspectionTemplateCatalog>();

        return services;
    }
}