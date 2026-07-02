using Microsoft.Extensions.DependencyInjection;

namespace SiNet.App.Wpf.Admin.Permissions;

/// <summary>
/// DI registration for native New System action-permissions admin surfaces.
/// </summary>
public static class PermissionAdminServiceCollectionExtensions
{
    /// <summary>
    /// Registers native action-permissions views, view models, and host window.
    /// Requires <see cref="SiNet.Application.Identity.IActionPermissionAdminService"/> from Infrastructure.Sql.
    /// </summary>
    public static IServiceCollection AddSiNetPermissionAdminWpf(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<ActionPermissionsViewModel>();
        services.AddTransient<ActionPermissionsView>();
        services.AddTransient<ActionPermissionsWindow>();

        return services;
    }
}
