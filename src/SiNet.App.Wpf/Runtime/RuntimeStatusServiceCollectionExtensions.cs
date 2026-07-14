using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Common;
using SiNet.Application.Email.Acc;
using SiNet.Application.Runtime;

namespace SiNet.App.Wpf.Runtime;

public static class RuntimeStatusServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetRuntimeStatus(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IStartupTaskRegistry, StartupTaskRegistry>();
        services.AddSingleton<IRuntimeSubsystemStatusService>(sp =>
            new RuntimeSubsystemStatusService(
                sp.GetRequiredService<IStartupTaskRegistry>(),
                sp.GetService<IExternalHealthCheckSource>(),
                sp.GetService<IAccServiceModeProvider>(),
                sp.GetService<IAccServiceHealthProbe>(),
                sp.GetService<IEmailAccBackgroundWorkTracker>(),
                sp.GetServices<IConnectorAuthService>()));

        return services;
    }
}
