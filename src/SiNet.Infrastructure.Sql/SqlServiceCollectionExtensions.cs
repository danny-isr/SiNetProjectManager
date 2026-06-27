using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql;

/// <summary>
/// Modular DI registration for SQL persistence. Implementations of the Application persistence
/// ports (for example <c>IProjectDirectory</c>) are wired here during the SQL migration round,
/// backed by <c>IDbContextFactory&lt;SiNetSQLDbContext&gt;</c> over the existing context.
/// EF migrations / ModelSnapshot are never hand-edited.
/// </summary>
public static class SqlServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQL module without a connection string. Retained for callers that wire
    /// the <see cref="SiNetSQLDbContext"/> factory through their own composition root.
    /// </summary>
    public static IServiceCollection AddSiNetSql(this IServiceCollection services)
    {
        // No-op overload: the host owns DbContextFactory registration (e.g. connection string
        // sourced from the host's secret store). Use the connectionString overload to let this
        // module register IDbContextFactory<SiNetSQLDbContext> directly.
        return services;
    }

    /// <summary>
    /// Registers <see cref="IDbContextFactory{TContext}"/> for <see cref="SiNetSQLDbContext"/>
    /// using the supplied connection string. The connection string must be provided by the caller
    /// (for example the host's secret store); this clean module performs no platform-specific lookup.
    /// </summary>
    public static IServiceCollection AddSiNetSql(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // Mirrors the legacy DbContext configuration: SQL Server with compatibility level 120
        // (prevents OPENJSON-based SQL for Contains() that requires DB compat level >= 130).
        services.AddDbContextFactory<SiNetSQLDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.UseCompatibilityLevel(120)));

        return services;
    }
}
