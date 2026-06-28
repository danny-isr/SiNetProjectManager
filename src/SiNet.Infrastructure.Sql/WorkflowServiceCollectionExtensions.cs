using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Workflow;
using SiNetSQL.Services.Workflow;

namespace SiNet.Infrastructure.Sql;

/// <summary>
/// Modular DI registration for the Workflow read slice extracted into this clean module.
/// Registers the read-only query and policy services both as their abstraction ports
/// (<see cref="IWorkflowQueryService"/>, <see cref="IProjectWorkflowPolicyService"/>) and as
/// their concrete types, so existing consumers that resolve the concrete services keep working
/// during the transitional round.
/// <para>
/// Write/engine services (orchestrator, executor, seed, validation, watchdog, etc.) remain in
/// the legacy host and are intentionally NOT registered here.
/// </para>
/// </summary>
public static class WorkflowServiceCollectionExtensions
{
    /// <summary>
    /// Registers the moved Workflow read services and their ports. Requires an
    /// <c>IDbContextFactory&lt;SiNetSQLDbContext&gt;</c> to be registered separately
    /// (for example via <see cref="SqlServiceCollectionExtensions.AddSiNetSql(IServiceCollection, string)"/>).
    /// </summary>
    public static IServiceCollection AddSiNetWorkflowReads(this IServiceCollection services)
    {
        // Register concrete types once, then forward the ports to the same scoped instance
        // so each request resolves a single service object.
        services.AddTransient<WorkflowQueryService>();
        services.AddTransient<IWorkflowQueryService>(sp => sp.GetRequiredService<WorkflowQueryService>());

        services.AddTransient<ProjectWorkflowPolicyService>();
        services.AddTransient<IProjectWorkflowPolicyService>(sp => sp.GetRequiredService<ProjectWorkflowPolicyService>());

        return services;
    }
}
