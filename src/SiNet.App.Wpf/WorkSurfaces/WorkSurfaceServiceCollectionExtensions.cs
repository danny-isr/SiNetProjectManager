using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Surfaces.Inspection;
using SiNet.App.Wpf.Surfaces.ProjectWork;
using SiNet.Application.Abstractions.Inspection;

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
        services.AddSingleton<IWorkSurfaceLauncher, WorkSurfaceLauncher>();

        services.AddSingleton<IInspectionFileTreePickerHost, NoOpInspectionFileTreePickerHost>();
        services.AddSingleton<IInspectionReportEmailHost, NoOpInspectionReportEmailHost>();
        services.AddSingleton<IInspectionNoteScreenshotHost, NoOpInspectionNoteScreenshotHost>();
        services.AddSingleton<IInspectionNoteLinkedFileHost, NoOpInspectionNoteLinkedFileHost>();
        services.AddSingleton<IInspectionTemplateCatalog, EmptyInspectionTemplateCatalog>();

        return services;
    }
}