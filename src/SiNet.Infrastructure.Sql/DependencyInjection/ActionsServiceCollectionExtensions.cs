using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SiNet.Application.Actions;
using SiNet.Application.Notifications;
using SiNet.Infrastructure.Sql.Services.Actions;
using SiNet.Infrastructure.Sql.Services.Notifications;

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

        // Notification delivery channel behind the SendNotification handler. Default is the policy-safe
        // log/audit channel; a host may override with a Gmail/in-app implementation (TryAdd = last
        // explicit registration wins) once G-Policy approves.
        services.TryAddTransient<INotificationDeliveryService, LogNotificationDeliveryService>();

        // TryAddEnumerable, not AddTransient: a host may reach this method twice (V2 calls
        // AddSiNetProcessBackbone directly and again through AddSiNet). Plain AddTransient would put
        // each handler in the container twice, and IEnumerable<IProcessActionHandler> would execute
        // every process action twice.
        services.TryAddEnumerable(
        [
            ServiceDescriptor.Transient<IProcessActionHandler, SendNotificationProcessActionHandler>(),
            ServiceDescriptor.Transient<IProcessActionHandler, SetProjectStatusProcessActionHandler>(),
            ServiceDescriptor.Transient<IProcessActionHandler, RecordTaskResultProcessActionHandler>(),
            ServiceDescriptor.Transient<IProcessActionHandler, CreateStageTasksProcessActionHandler>(),
            ServiceDescriptor.Transient<IProcessActionHandler, SetBillingPendingProcessActionHandler>(),
            ServiceDescriptor.Transient<IProcessActionHandler, ClosePreviousStageTasksProcessActionHandler>(),
            ServiceDescriptor.Transient<IProcessActionHandler, StartSubWorkflowProcessActionHandler>(),
            ServiceDescriptor.Transient<IProcessActionHandler, CloseProjectProcessActionHandler>(),
        ]);

        services.AddTransient<ProcessActionService>();
        services.AddTransient<IProcessActionService>(sp => sp.GetRequiredService<ProcessActionService>());

        return services;
    }
}
