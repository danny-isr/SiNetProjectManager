using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        // TryAdd throughout: both AddSiNet and AddSiNetNewSystemWpf reach AddSiNetProjectWorkRuntime,
        // and the filing module registers the root resolver as well. Each of these has a single
        // implementation, so first-wins keeps the graph free of redundant descriptors.
        services.TryAddTransient<IFileServerRootResolver, FileServerRootResolver>();

        services.TryAddTransient<IProjectFileQueryService, ProjectFileQueryService>();
        services.TryAddTransient<IProjectFolderPathResolver, ProjectFolderPathResolver>();
        services.TryAddTransient<IProjectDriveFolderResolver, ProjectDriveFolderResolver>();
        services.TryAddTransient<IProjectFolderWriteService, SqlProjectFolderWriteService>();

        return services;
    }
}
