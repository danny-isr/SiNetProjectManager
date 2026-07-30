using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SiNet.Application.FileCatalog;
using SiNet.Infrastructure.Sql.Services.FileCatalog;

namespace SiNet.Infrastructure.Sql;

/// <summary>DI for the global admin file/folder catalog (ניהול קבצים).</summary>
public static class FileCatalogServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetFileCatalogSql(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddTransient<IFileCatalogQueryService, SqlFileCatalogQueryService>();
        services.TryAddTransient<IFileCatalogWriteService, SqlFileCatalogWriteService>();
        return services;
    }
}
