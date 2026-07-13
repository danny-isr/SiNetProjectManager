using Microsoft.Extensions.DependencyInjection;

namespace SiNet.LegacyBridge;

/// <summary>
/// Registers strangler adapters that implement new Application ports by delegating to legacy
/// code. Adapters are added here per domain and removed once the real
/// <c>SiNet.Infrastructure.*</c> implementation replaces them.
/// <para>
/// <b>Temporary / migration bridge — not target architecture.</b> New Work Surfaces must register
/// and consume native ports via <c>AddSiNetProcessBackbone()</c> instead of relying on this module.
/// Candidate for future removal once remaining inspection/email slices migrate.
/// </para>
/// </summary>
public static class LegacyBridgeServiceCollectionExtensions
{
    /// <summary>
    /// Email and Inspection ports are registered natively (<c>AddSiNetGoogle</c>,
    /// <c>AddSiNetInspectionSql</c>). This method is kept so hosts can continue calling it during
    /// the strangler transition.
    /// </summary>
    public static IServiceCollection AddSiNetLegacyBridge(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
