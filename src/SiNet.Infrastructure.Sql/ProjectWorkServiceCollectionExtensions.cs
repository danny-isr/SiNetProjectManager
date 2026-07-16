using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.ProjectWork;
using SiNet.Infrastructure.Sql.Services.Files;
using SiNet.Infrastructure.Sql.Services.ProjectWork;

namespace SiNet.Infrastructure.Sql;

/// <summary>
/// Modular DI registration for the SQL-backed ProjectWork read services: the DB-defined folder/file
/// tree query and the FileServer folder-path resolver used by the FileServer file store.
/// </summary>
public static class ProjectWorkServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetProjectWorkSql(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Reused by the folder-path resolver; safe to register here even if the filing module also does.
        services.AddTransient<IFileServerRootResolver, FileServerRootResolver>();

        services.AddTransient<IProjectFileQueryService, ProjectFileQueryService>();
        services.AddTransient<IProjectFolderPathResolver, ProjectFolderPathResolver>();
        services.AddTransient<IProjectDriveFolderResolver, ProjectDriveFolderResolver>();

        return services;
    }
}
