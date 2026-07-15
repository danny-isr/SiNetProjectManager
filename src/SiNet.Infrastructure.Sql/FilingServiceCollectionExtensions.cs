using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SiNet.Infrastructure.Sql.Services.Files;

namespace SiNet.Infrastructure.Sql;

/// <summary>
/// Registers the native centralized project-file filing services (FileServer + ACC). The ACC branch
/// requires an <c>IAccFileUploadService</c> to be registered separately (Infrastructure.Autodesk).
/// Idempotent (TryAdd) so it can be composed alongside other backbone registrations.
/// </summary>
public static class FilingServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetFilingServices(this IServiceCollection services)
    {
        services.TryAddTransient<IFileServerMetadataStore, FileServerMetadataStore>();
        services.TryAddTransient<IFileServerVersionArchiver, FileServerVersionArchiver>();
        services.TryAddTransient<IFolderPathResolver, FolderPathResolver>();
        services.TryAddTransient<IFileServerRootResolver, FileServerRootResolver>();
        services.TryAddTransient<IProjectFileFilingService, ProjectFileFilingService>();
        return services;
    }
}
