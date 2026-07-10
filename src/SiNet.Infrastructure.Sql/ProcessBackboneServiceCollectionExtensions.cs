using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Workflow;
using SiNet.Infrastructure.Sql.DependencyInjection;
using SiNet.Infrastructure.Sql.Services.Workflow;

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
    /// Registers a fail-fast <see cref="UnboundWorkflowCommandService"/> for
    /// <see cref="IWorkflowCommandService"/>; hosts with SiNetSQL must replace it via
    /// <c>AddSiNetWorkflowCommands()</c> before auto-advance can succeed.
    /// </summary>
    public static IServiceCollection AddSiNetProcessBackbone(this IServiceCollection services)
    {
        services.AddSiNetWorkflowReads();
        services.AddSiNetTaskServices();
        services.AddSiNetActionServices();
        services.AddTransient<IWorkflowCommandService, UnboundWorkflowCommandService>();
        return services;
    }
}
