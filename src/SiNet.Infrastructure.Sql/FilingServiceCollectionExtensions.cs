using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Projects;
using SiNet.Infrastructure.Sql.Services.Files;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql;

/// <summary>
/// Registers the native centralized project-file filing services (FileServer + ACC). The ACC branch
/// requires an <c>IAccFileUploadService</c> to be registered separately (Infrastructure.Autodesk).
/// On-demand <c>ProjectAccMapping</c> EnsureMapping uses optional
/// <see cref="IProjectAccMappingProvisioner"/> when the host registered it
/// (StandaloneNew: <c>AddSiNetAccProjectProvisioning</c>; V2: App composition).
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
        services.TryAddTransient<IProjectFileFilingService>(sp =>
            new ProjectFileFilingService(
                sp.GetRequiredService<IDbContextFactory<SiNetSQLDbContext>>(),
                sp.GetRequiredService<IFolderPathResolver>(),
                sp.GetRequiredService<IFileServerMetadataStore>(),
                sp.GetRequiredService<IFileServerVersionArchiver>(),
                sp.GetRequiredService<IFileServerRootResolver>(),
                sp.GetRequiredService<IAccFileUploadService>(),
                sp.GetService<IProjectAccMappingProvisioner>()));
        return services;
    }
}
