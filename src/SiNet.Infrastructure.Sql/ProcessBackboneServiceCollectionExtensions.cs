using Microsoft.Extensions.DependencyInjection;
using SiNet.Infrastructure.Sql.DependencyInjection;

namespace SiNet.Infrastructure.Sql;

/// <summary>
/// Aggregates the Workflow / Task / Action foundation registrations for the New System process
/// backbone. This is the target composition entry point — not LegacyBridge.
/// </summary>
public static class ProcessBackboneServiceCollectionExtensions
{
    /// <summary>
    /// Registers native read/write backbone ports implemented in Infrastructure.Sql:
    /// workflow reads, task navigation/completion/metadata, and the foundation action dispatcher.
    /// Workflow command writes remain in SiNetSQL until orchestrator migration completes; hosts must
    /// bind <see cref="SiNet.Application.Workflow.IWorkflowCommandService"/> — it is required by
    /// <c>SqlTaskCompletionService</c> when auto-advance is requested.
    /// </summary>
    public static IServiceCollection AddSiNetProcessBackbone(this IServiceCollection services)
    {
        services.AddSiNetWorkflowReads();
        services.AddSiNetTaskServices();
        services.AddSiNetActionServices();
        return services;
    }
}
