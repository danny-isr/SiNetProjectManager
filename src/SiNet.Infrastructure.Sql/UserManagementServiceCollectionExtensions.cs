using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Identity;
using SiNet.Infrastructure.Sql.Services.Identity;
using SiNet.Infrastructure.Sql.Services.MasterPlan;

namespace SiNet.Infrastructure.Sql;

/// <summary>
/// Registers the native <see cref="IUserManagementService"/> implementation backed by EF/SQL.
/// </summary>
public static class UserManagementServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="SqlUserManagementService"/> as <see cref="IUserManagementService"/>.
    /// Requires <see cref="IAuthorizationQueryService"/> and
    /// <c>IDbContextFactory&lt;SiNetDbContext&gt;</c> (registered via <see cref="SqlServiceCollectionExtensions.AddSiNetSql"/>).
    /// </summary>
    public static IServiceCollection AddSiNetUserManagementSql(this IServiceCollection services)    {
        ArgumentNullException.ThrowIfNull(services);

        if (!services.Any(d => d.ServiceType == typeof(ICurrentUserContext)))
        {
            services.AddSingleton<ICurrentUserContext>(NullCurrentUserContext.Instance);
        }

        if (!services.Any(d => d.ServiceType == typeof(IMasterPlanEmployeeConnectionProvider)))
        {
            services.AddSingleton<IMasterPlanEmployeeConnectionProvider>(
                NullMasterPlanEmployeeConnectionProvider.Instance);
        }

        if (!services.Any(d => d.ServiceType == typeof(IDirectoryUserConnectionProvider)))
        {
            services.AddSingleton<IDirectoryUserConnectionProvider>(
                NullDirectoryUserConnectionProvider.Instance);
        }

        if (!services.Any(d => d.ServiceType == typeof(IDirectoryUserLookupService)))
        {
            services.AddTransient<IDirectoryUserLookupService>(_ => NullDirectoryUserLookupService.Instance);
        }

        services.AddTransient<SqlUserManagementService>();
        services.AddTransient<IUserManagementService>(sp => sp.GetRequiredService<SqlUserManagementService>());
        services.AddTransient<SqlActionPermissionAdminService>();
        services.AddTransient<IActionPermissionAdminService>(sp => sp.GetRequiredService<SqlActionPermissionAdminService>());
        services.AddTransient<SqlMasterPlanEmployeeLookupService>();
        services.AddTransient<IMasterPlanEmployeeLookupService>(sp => sp.GetRequiredService<SqlMasterPlanEmployeeLookupService>());

        return services;
    }
}
