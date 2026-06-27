using Microsoft.Extensions.DependencyInjection;

namespace SiNet.LegacyBridge;

/// <summary>
/// Registers strangler adapters that implement new Application ports by delegating to legacy
/// code. Adapters are added here per domain and removed once the real
/// <c>SiNet.Infrastructure.*</c> implementation replaces them.
/// </summary>
public static class LegacyBridgeServiceCollectionExtensions
{
    /// <summary>
    /// Reserved for bridge-wide registrations. Currently a no-op: there are no active strangler
    /// adapters in the new stack. The Email/Google slice has been migrated to the native
    /// <c>GmailEmailGateway</c> in <c>SiNet.Infrastructure.Google</c> (registered by
    /// <c>AddSiNetGoogle</c>), so the bridge no longer wires <c>IEmailGateway</c>. The remaining
    /// <c>ILegacyEmailSource</c> seam is bound only by the legacy WPF host
    /// (<c>SiNetProjectManagerV2</c>) and is intentionally NOT registered here, preserving the
    /// rule that this assembly never depends on the legacy connector.
    /// </summary>
    public static IServiceCollection AddSiNetLegacyBridge(this IServiceCollection services)
    {
        return services;
    }
}
