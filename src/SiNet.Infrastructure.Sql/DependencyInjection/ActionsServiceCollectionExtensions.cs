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
