using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Identity;
using SiNet.Infrastructure.Sql.Services.Identity;

namespace SiNet.Infrastructure.Sql;

/// <summary>
/// Registers the native <see cref="IAuthorizationQueryService"/> implementation backed by EF/SQL.
/// Requires <c>IDbContextFactory&lt;SiNetDbContext&gt;</c> (via
/// <see cref="SqlServiceCollectionExtensions.AddSiNetSql(Microsoft.Extensions.DependencyInjection.IServiceCollection,string)"/>)
/// and an <see cref="ICurrentUserContext"/> (a <see cref="NullCurrentUserContext"/> is registered as a
/// safe default when the host has not bound a real one).
/// </summary>
public static class AuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetAuthorizationSql(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!services.Any(d => d.ServiceType == typeof(ICurrentUserContext)))
        {
            services.AddSingleton<ICurrentUserContext>(NullCurrentUserContext.Instance);
        }

        services.AddTransient<SqlAuthorizationQueryService>();
        services.AddTransient<IAuthorizationQueryService>(
            sp => sp.GetRequiredService<SqlAuthorizationQueryService>());

        return services;
    }
}
