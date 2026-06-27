using Microsoft.Extensions.DependencyInjection;

namespace SiNet.Infrastructure.Sql;

/// <summary>
/// Modular DI registration for SQL persistence. Implementations of the Application persistence
/// ports (for example <c>IProjectDirectory</c>) are wired here during the SQL migration round,
/// backed by <c>IDbContextFactory&lt;SiNetSQLDbContext&gt;</c> over the existing context.
/// EF migrations / ModelSnapshot are never hand-edited.
/// </summary>
public static class SqlServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetSql(this IServiceCollection services)
    {
        // TODO (SQL migration round): register IProjectDirectory -> SqlProjectDirectory
        // using IDbContextFactory<SiNetSQLDbContext>.
        return services;
    }
}
