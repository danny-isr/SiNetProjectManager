using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Identity;
using SiNet.Infrastructure.Sql.Data;
using SiNet.Infrastructure.Sql.Services.Identity;

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

        services.AddTransient<SqlUserManagementService>();
        services.AddTransient<IUserManagementService>(sp => sp.GetRequiredService<SqlUserManagementService>());

        return services;
    }
}
