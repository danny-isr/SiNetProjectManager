using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SiNet.Application.Data;
using SiNet.Application.Identity;
using SiNet.Infrastructure.Sql.Services.Data;
using SiNet.Infrastructure.Sql.Services.Identity;

namespace SiNet.Infrastructure.Sql;

/// <summary>
/// Native identity + schema gate for standalone New System host mode.
/// Call <b>before</b> <c>AddSiNet</c> / <c>AddSiNetUserManagementSql</c> so
/// <see cref="NullCurrentUserContext"/> is not registered as a default.
/// </summary>
public static class IdentitySqlServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetIdentitySql(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<AuthenticatedUserSession>();
        services.TryAddSingleton<ICurrentUserContext>(sp => sp.GetRequiredService<AuthenticatedUserSession>());
        services.TryAddSingleton<ICurrentUserProfileService>(sp => sp.GetRequiredService<AuthenticatedUserSession>());
        services.TryAddTransient<SqlWindowsCurrentUserAuthenticator>();
        services.TryAddTransient<IDatabaseSchemaGate, SqlDatabaseSchemaGate>();

        return services;
    }
}
