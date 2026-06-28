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
    /// EF diagnostics are off by default; use the <see cref="AddSiNetSql(IServiceCollection, string, Action{SiNetSqlOptions})"/>
    /// overload to opt in.
    /// </summary>
    public static IServiceCollection AddSiNetSql(this IServiceCollection services, string connectionString)
        => services.AddSiNetSql(connectionString, static _ => { });

    /// <summary>
    /// Registers <see cref="IDbContextFactory{TContext}"/> for <see cref="SiNetSQLDbContext"/>
    /// using the supplied connection string, with optional EF diagnostics configured via
    /// <paramref name="configure"/>. The connection string must be provided by the caller
    /// (for example the host's secret store); this clean module performs no platform-specific lookup.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The SQL Server connection string (caller-supplied).</param>
    /// <param name="configure">
    /// Configures <see cref="SiNetSqlOptions"/>. Diagnostics default to disabled; a host should
    /// opt in to <see cref="SiNetSqlOptions.EnableEfDebugDiagnostics"/> only under <c>#if DEBUG</c>
    /// to match the legacy host's development-time behavior. Release behavior is unchanged.
    /// </param>
    public static IServiceCollection AddSiNetSql(
        this IServiceCollection services,
        string connectionString,
        Action<SiNetSqlOptions> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SiNetSqlOptions();
        configure(options);

        // Mirrors the legacy DbContext configuration: SQL Server with compatibility level 120
        // (prevents OPENJSON-based SQL for Contains() that requires DB compat level >= 130).
        services.AddDbContextFactory<SiNetSQLDbContext>(builder =>
        {
            builder.UseSqlServer(connectionString, sql => sql.UseCompatibilityLevel(120));

            // Opt-in EF diagnostics. The host enables this only under #if DEBUG so the delegated
            // registration matches the previous inline host behavior exactly; in Release the flag
            // stays false and these calls are never made.
            if (options.EnableEfDebugDiagnostics)
            {
                builder.EnableSensitiveDataLogging();
                builder.EnableDetailedErrors();
            }
        });

        return services;
    }
}
