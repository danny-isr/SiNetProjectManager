using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SiNet.App.Wpf.Surfaces.Inspection;
using SiNet.App.Wpf.Surfaces.ProjectWork;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.ProjectWork;

namespace SiNet.App.Wpf.WorkSurfaces;

public static class WorkSurfaceServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetWorkSurfaces(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<InspectionWindowViewModel>();
        services.AddSingleton<IInspectionWindowFactory, InspectionWindowFactory>();
        services.AddTransient<SiNet.App.Wpf.Surfaces.ProjectWork.ProjectWorkTreeViewModel>();
        services.AddTransient<SiNet.App.Wpf.Shared.Projects.ProjectSelectorViewModel>();
        services.AddTransient<ProjectWorkWindowViewModel>();
        services.AddSingleton<IProjectWorkWindowFactory, ProjectWorkWindowFactory>();
        services.AddSingleton<ProjectWorkTaskFloatingHost>();
        services.AddSingleton<ProjectWorkSurfaceHost>();
        services.AddSingleton<IProjectWorkSurfaceHost>(sp => sp.GetRequiredService<ProjectWorkSurfaceHost>());
        services.AddSingleton<IWorkSurfaceLauncher, WorkSurfaceLauncher>();

        services.TryAddSingleton<IInspectionFileTreePickerHost, NoOpInspectionFileTreePickerHost>();
        services.TryAddSingleton<IInspectionReportEmailHost, NoOpInspectionReportEmailHost>();
        services.TryAddSingleton<IInspectionNoteScreenshotHost, NoOpInspectionNoteScreenshotHost>();
        services.TryAddSingleton<IInspectionNoteLinkedFileHost, NoOpInspectionNoteLinkedFileHost>();
        // Standalone host registers GoogleDriveInspectionTemplateCatalog before this call.
        services.TryAddSingleton<IInspectionTemplateCatalog, EmptyInspectionTemplateCatalog>();

        return services;
    }
}