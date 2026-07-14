using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Actions;
using SiNet.Infrastructure.Sql.Services.Actions;

namespace SiNet.Infrastructure.Sql.DependencyInjection;

/// <summary>
/// Registers native Infrastructure.Sql process-action services (foundation slice).
/// </summary>
public static class ActionsServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetActionServices(this IServiceCollection services)
    {
        // StartSubWorkflowProcessActionHandler depends on the native workflow engine graph
        // (WorkflowEngine + WorkflowStageTaskProvisioningService). Register it idempotently
        // (TryAdd) so the action services are self-contained even when this method is called
        // on its own (e.g. boundary tests), without double-registering when
        // AddSiNetProcessBackbone also wires the engine.
        services.AddSiNetWorkflowEngine();

        services.AddTransient<IProcessActionHandler, SendNotificationProcessActionHandler>();
        services.AddTransient<IProcessActionHandler, SetProjectStatusProcessActionHandler>();
        services.AddTransient<IProcessActionHandler, RecordTaskResultProcessActionHandler>();
        services.AddTransient<IProcessActionHandler, CreateStageTasksProcessActionHandler>();
        services.AddTransient<IProcessActionHandler, SetBillingPendingProcessActionHandler>();
        services.AddTransient<IProcessActionHandler, ClosePreviousStageTasksProcessActionHandler>();
        services.AddTransient<IProcessActionHandler, StartSubWorkflowProcessActionHandler>();
        services.AddTransient<IProcessActionHandler, CloseProjectProcessActionHandler>();

        services.AddTransient<ProcessActionService>();
        services.AddTransient<IProcessActionService>(sp => sp.GetRequiredService<ProcessActionService>());

        return services;
    }
}
