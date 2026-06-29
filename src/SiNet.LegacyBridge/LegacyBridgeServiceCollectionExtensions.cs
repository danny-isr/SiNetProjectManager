using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Inspection;
using SiNet.LegacyBridge.Inspection;

namespace SiNet.LegacyBridge;

/// <summary>
/// Registers strangler adapters that implement new Application ports by delegating to legacy
/// code. Adapters are added here per domain and removed once the real
/// <c>SiNet.Infrastructure.*</c> implementation replaces them.
/// </summary>
public static class LegacyBridgeServiceCollectionExtensions
{
    /// <summary>
    /// Registers bridge adapters. The Email/Google slice has been migrated to the native
    /// <c>GmailEmailGateway</c> in <c>SiNet.Infrastructure.Google</c> (registered by
    /// <c>AddSiNetGoogle</c>), so the bridge no longer wires <c>IEmailGateway</c>. It does wire the
    /// <see cref="IInspectionWorkspace"/> port to <see cref="LegacyInspectionWorkspace"/> for the new
    /// Inspection screen: that adapter delegates to the optional <c>ILegacyInspectionSource</c> seam
    /// (bound only by the legacy WPF host, which knows both worlds) and degrades to an empty series
    /// list when the seam is unbound, so the new app stays free of any <c>SiNetSQL</c> dependency.
    /// </summary>
    public static IServiceCollection AddSiNetLegacyBridge(this IServiceCollection services)
    {
        services.AddTransient<IInspectionWorkspace, LegacyInspectionWorkspace>();
        return services;
    }
}
