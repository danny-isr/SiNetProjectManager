using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Projects;
using SiNet.Infrastructure.Autodesk;
using SiNetSQL.Services.AccBootstrap;

namespace SiNet.Infrastructure.AccBootstrap;

/// <summary>
/// DI registration for AccBootstrap seams used by Standalone New hosts:
/// Local ACC Inbox bootstrap executor, and project ACC mapping provisioning
/// (Remote AccService / Local in-process) + <see cref="IProjectAccMappingProvisioner"/>.
/// </summary>
public static class AccBootstrapServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetAccInboxBootstrapLocal(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<IAccInboxBootstrapLocalExecutor, AccBootstrapLocalInboxBootstrapExecutor>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="IAccProjectProvisioningService"/> (mode-switched Remote/Local) and
    /// <see cref="IProjectAccMappingProvisioner"/> for create-time + on-demand EnsureMapping.
    /// Call after <c>AddSiNetAutodesk()</c> so mode/TLS options resolve. Idempotent via TryAdd.
    /// Does <b>not</b> belong inside <c>AddSiNetAutodesk()</c> (control-plane boundary).
    /// </summary>
    public static IServiceCollection AddSiNetAccProjectProvisioning(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddTransient<AccProjectProvisioningService>();

        // Typed client: infinite timeout — ensure-mapping can take 1–2 minutes.
        // BaseUrl + API key are applied per request (see RemoteAccProjectProvisioningService).
        services.AddHttpClient<RemoteAccProjectProvisioningService>(static client =>
                client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(sp =>
                AccServiceHttpClientConfigurator.CreateHandler(
                    sp.GetRequiredService<AccServiceControlPlaneOptions>()));

        services.TryAddTransient<IAccProjectProvisioningService>(sp =>
            new ModeSwitchingAccProjectProvisioningService(
                sp.GetRequiredService<IAccServiceModeProvider>(),
                sp.GetRequiredService<AccProjectProvisioningService>(),
                sp.GetRequiredService<RemoteAccProjectProvisioningService>()));

        services.TryAddTransient<IProjectAccMappingProvisioner, ProjectAccMappingProvisionerAdapter>();

        return services;
    }
}
