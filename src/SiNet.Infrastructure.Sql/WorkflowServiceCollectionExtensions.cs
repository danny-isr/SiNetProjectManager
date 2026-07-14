using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SiNet.Application.Workflow;
using SiNet.Infrastructure.Sql.Services.Workflow;
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
        // so each request resolves a single service object. TryAdd keeps this idempotent so
        // AddSiNetActionServices can pull the read/policy services in for handler dependencies
        // without double-registering when AddSiNetProcessBackbone also calls this method.
        services.TryAddTransient<WorkflowQueryService>();
        services.TryAddTransient<IWorkflowQueryService>(sp => sp.GetRequiredService<WorkflowQueryService>());

        services.TryAddTransient<ProjectWorkflowPolicyService>();
        services.TryAddTransient<IProjectWorkflowPolicyService>(sp => sp.GetRequiredService<ProjectWorkflowPolicyService>());

        services.TryAddTransient<SqlWorkflowClosedViewerQueryService>();
        services.TryAddTransient<IWorkflowClosedViewerQueryService>(
            sp => sp.GetRequiredService<SqlWorkflowClosedViewerQueryService>());

        services.TryAddTransient<SqlWorkflowCanvasLayoutService>();
        services.TryAddTransient<IWorkflowCanvasLayoutService>(
            sp => sp.GetRequiredService<SqlWorkflowCanvasLayoutService>());

        return services;
    }

    /// <summary>
    /// Registers the native workflow write engine graph (re-homed from legacy SiNetSQL):
    /// lifecycle <see cref="WorkflowEngine"/>, <see cref="WorkflowTransitionEvaluator"/>,
    /// <see cref="WorkflowStageTaskProvisioningService"/>, <see cref="WorkflowActionExecutor"/>, and the
    /// composing <see cref="WorkflowTaskOrchestrator"/>. Requires
    /// <c>IDbContextFactory&lt;SiNetSQLDbContext&gt;</c>, the workflow read/policy services
    /// (<see cref="AddSiNetWorkflowReads"/>), and the action dispatcher
    /// (<see cref="DependencyInjection.ActionsServiceCollectionExtensions.AddSiNetActionServices"/>)
    /// to be registered. The command port itself is registered by
    /// <see cref="ProcessBackboneServiceCollectionExtensions.AddSiNetProcessBackbone"/>.
    /// </summary>
    public static IServiceCollection AddSiNetWorkflowEngine(this IServiceCollection services)
    {
        // Policy/read services are engine dependencies; ensure they are present even when this
        // method is invoked on its own. TryAdd keeps everything idempotent.
        services.AddSiNetWorkflowReads();

        services.TryAddTransient<WorkflowEngine>();
        services.TryAddTransient<WorkflowTransitionEvaluator>();
        services.TryAddTransient<WorkflowStageTaskProvisioningService>();
        services.TryAddTransient<WorkflowActionExecutor>();
        services.TryAddTransient<WorkflowTaskOrchestrator>();
        return services;
    }
}
