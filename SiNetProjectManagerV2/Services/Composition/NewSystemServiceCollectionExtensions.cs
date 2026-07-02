using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Configuration;
using SiNet.Application.Identity;
using SiNet.Infrastructure.Secrets;
using SiNetProjectManagerV2.Services;

namespace SiNetProjectManagerV2.Services.Composition;

/// <summary>
/// Modular DI for the New System shell graph (Project Context + shell menu + admin surfaces).
/// Legacy host still shares the same container today; this extension documents the New System slice
/// explicitly (P7 composition split stepping stone).
/// </summary>
public static class NewSystemServiceCollectionExtensions
{
    /// <summary>
    /// Registers Project Context and the clean shell factory. Does not register legacy window factories.
    /// Call after SQL project reads and Inspection shell views are registered.
    /// </summary>
    public static IServiceCollection AddSiNetNewSystemGraph(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        SiNet.Infrastructure.Sql.ProjectQueryServiceCollectionExtensions.AddSiNetProjectQuerySql(services);
        SiNet.Infrastructure.Sql.UserManagementServiceCollectionExtensions.AddSiNetUserManagementSql(services);
        services.AddSiNetSecrets();
        services.AddSingleton(LegacyGoogleClientSecretsFallback.Create());
        SiNet.App.Wpf.Shared.Projects.ProjectContextServiceCollectionExtensions.AddSiNetProjectContext(services);
        SiNet.App.Wpf.Admin.Users.UserAdminServiceCollectionExtensions.AddSiNetUserAdminWpf(services);
        SiNet.App.Wpf.Admin.Permissions.PermissionAdminServiceCollectionExtensions.AddSiNetPermissionAdminWpf(services);
        SiNet.App.Wpf.Admin.Security.SecretAdminServiceCollectionExtensions.AddSiNetSecretAdminWpf(services);
        services.AddSingleton<IMasterPlanEmployeeConnectionProvider, LegacyMasterPlanEmployeeConnectionProvider>();
        services.AddSingleton<IDirectoryUserConnectionProvider, LegacyDirectoryUserConnectionProvider>();
        services.AddSingleton<ISecretSetupHostConfiguration, LegacySecretSetupHostConfiguration>();
        services.AddTransient<IDirectoryUserLookupService, ActiveDirectoryUserLookupService>();
        ShellServiceCollectionExtensions.AddSiNetShell(services);

        return services;
    }
}
