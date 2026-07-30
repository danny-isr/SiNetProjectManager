using Microsoft.Extensions.DependencyInjection;

namespace SiNet.App.Wpf.Admin.FileCatalog;

/// <summary>DI for native File Catalog admin (ניהול קבצים).</summary>
public static class FileCatalogServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetFileCatalogAdminWpf(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddTransient<FileCatalogViewModel>();
        services.AddTransient<FileCatalogView>();
        services.AddTransient<FileCatalogWindow>();
        return services;
    }
}
