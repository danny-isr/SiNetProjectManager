using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Projects;
using SiNetSQL.Services.Projects;

namespace SiNet.Infrastructure.Sql;

/// <summary>
/// Modular DI registration for the real, read-only Project query slice
/// (see <c>docs/PROJECTS.md</c> §5 and <c>docs/PROJECT_CONTEXT_MIGRATION.md</c>).
/// <para>
/// Registers <see cref="ProjectQueryService"/> as the concrete type and forwards the Application port
/// <see cref="IProjectQueryService"/> to the same instance, so the shared Project Selector loads real
/// projects instead of the in-memory fake. This is a <b>read-only</b> slice: no writes, no schema, no
/// migrations.
/// </para>
/// </summary>
public static class ProjectQueryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the real read-only <see cref="IProjectQueryService"/> backed by
    /// <see cref="ProjectQueryService"/>. Requires an
    /// <c>IDbContextFactory&lt;SiNetSQLDbContext&gt;</c> to be registered separately (for example via
    /// <see cref="SqlServiceCollectionExtensions.AddSiNetSql(IServiceCollection, string)"/>).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddSiNetProjectQuerySql(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register the concrete type once, then forward the port to the same instance so each request
        // resolves a single service object (mirrors AddSiNetWorkflowReads).
        services.AddTransient<ProjectQueryService>();
        services.AddTransient<IProjectQueryService>(sp => sp.GetRequiredService<ProjectQueryService>());

        return services;
    }
}
