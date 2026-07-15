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
    /// workflow reads, the native workflow write engine, task navigation/completion/metadata, and the
    /// action dispatcher. The <see cref="IWorkflowCommandService"/> port is fulfilled by the native
    /// <see cref="NativeWorkflowCommandService"/> (composing the re-homed orchestrator + engine), so
    /// hosts no longer need the legacy SiNetSQL command adapter. Requires
    /// <c>IDbContextFactory&lt;SiNetSQLDbContext&gt;</c> to be registered separately (e.g. via
    /// <c>AddSiNetSql(connectionString)</c>).
    /// </summary>
    public static IServiceCollection AddSiNetProcessBackbone(this IServiceCollection services)
    {
        services.AddSiNetWorkflowReads();
        services.AddSiNetWorkflowEngine();
        services.AddSiNetTaskServices();
        services.AddSiNetActionServices();
        services.AddTransient<NativeWorkflowCommandService>();
        services.AddTransient<IWorkflowCommandService>(
            sp => sp.GetRequiredService<NativeWorkflowCommandService>());

        // Stalled-workflow safety net. Depends only on the DbContext factory and the native command
        // port above; the host schedules the periodic sweep (see V2 startup background loop).
        services.AddTransient<StalledWorkflowWatchdog>();
        return services;
    }
}
