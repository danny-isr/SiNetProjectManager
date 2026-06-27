using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Email;
using SiNet.LegacyBridge.Email;

namespace SiNet.LegacyBridge;

/// <summary>
/// Registers strangler adapters that implement new Application ports by delegating to legacy
/// code. Adapters are added here per domain and removed once the real
/// <c>SiNet.Infrastructure.*</c> implementation replaces them.
/// </summary>
public static class LegacyBridgeServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetLegacyBridge(this IServiceCollection services)
    {
        // Example bridge (Foundation Round): new IEmailGateway port -> legacy GoogleService adapter.
        services.AddSingleton<IEmailGateway, LegacyEmailGatewayAdapter>();
        return services;
    }
}
