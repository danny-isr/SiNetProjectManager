using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Autodesk;

namespace SiNet.Infrastructure.Sql.AutodeskLocal;

/// <summary>
/// Registers SQL-backed implementations used by the local ACC runtime.
/// Called by composition after the Autodesk module so Autodesk remains independent of SQL.
/// </summary>
public static class AutodeskLocalSqlServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetAutodeskLocalSql(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<LocalAccProjectService>();
        services.AddTransient<ILocalAccProjectService>(sp => sp.GetRequiredService<LocalAccProjectService>());
        services.AddTransient<LocalAccProjectCatalogService>();
        services.AddTransient<ILocalAccProjectCatalogService>(sp => sp.GetRequiredService<LocalAccProjectCatalogService>());
        services.AddTransient<IAccProjectRootFolderResolver, LocalAccProjectRootFolderResolver>();
        services.AddTransient<IAccLookupSeedService, LocalAccLookupSeedService>();

        return services;
    }
}
