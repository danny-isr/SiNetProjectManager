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
        services.AddTransient<IProcessActionHandler, SendNotificationProcessActionHandler>();
        services.AddTransient<IProcessActionHandler, SetProjectStatusProcessActionHandler>();
        services.AddTransient<IProcessActionHandler, RecordTaskResultProcessActionHandler>();
        services.AddTransient<IProcessActionHandler, CreateStageTasksProcessActionHandler>();
        services.AddTransient<IProcessActionHandler, SetBillingPendingProcessActionHandler>();

        services.AddTransient<ProcessActionService>();
        services.AddTransient<IProcessActionService>(sp => sp.GetRequiredService<ProcessActionService>());

        return services;
    }
}
