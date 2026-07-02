using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Shell;

namespace SiNetProjectManagerV2.Services.Composition;

/// <summary>
/// Modular DI for the New System shell graph (Project Context + shell menu + admin surfaces).
/// Legacy host still shares the same container today; this extension documents the New System slice
/// explicitly (P7 composition split stepping stone).
/// </summary>
public static class NewSystemServiceCollectionExtensions
{
    /// <summary>
    /// Registers Project Context, the clean shell factory, and host-owned admin window factories.
    /// Call after SQL project reads and Inspection shell views are registered.
    /// </summary>
    public static IServiceCollection AddSiNetNewSystemGraph(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        SiNet.Infrastructure.Sql.ProjectQueryServiceCollectionExtensions.AddSiNetProjectQuerySql(services);
        SiNet.App.Wpf.Shared.Projects.ProjectContextServiceCollectionExtensions.AddSiNetProjectContext(services);
        ShellServiceCollectionExtensions.AddSiNetShell(services);

        services.AddSingleton<IActionPermissionAdminWindowFactory, ActionPermissionAdminWindowFactory>();
        services.AddSingleton<IUserManagementWindowFactory, UserManagementWindowFactory>();
        services.AddSingleton<IAddUserWindowFactory, AddUserWindowFactory>();

        return services;
    }
}
