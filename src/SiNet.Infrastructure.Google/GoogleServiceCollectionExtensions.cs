using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Common;

namespace SiNet.Infrastructure.Google;

/// <summary>
/// Modular DI registration for the Google/Gmail module. Wires the native Gmail
/// <see cref="IEmailGateway"/> implementation (direct Gmail API access via
/// <see cref="GmailClientProvider"/>) and the native <see cref="IConnectorAuthService"/>
/// auth/health bridge, with no dependency on the legacy <c>GoogleService</c>
/// or <c>SiNet.LegacyBridge</c>.
/// </summary>
public static class GoogleServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Gmail module with default options. The host should normally use the
    /// <see cref="AddSiNetGoogle(IServiceCollection, Action{GmailOptions})"/> overload to point
    /// the gateway at its client secrets and token store.
    /// </summary>
    public static IServiceCollection AddSiNetGoogle(this IServiceCollection services)
        => services.AddSiNetGoogle(static _ => { });

    /// <summary>
    /// Registers the Gmail module and lets the host configure <see cref="GmailOptions"/>
    /// (client secrets path, token store, root label, interactive sign-in).
    /// </summary>
    public static IServiceCollection AddSiNetGoogle(
        this IServiceCollection services,
        Action<GmailOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        services.AddSingleton(sp =>
        {
            var options = new GmailOptions();
            configure(options);
            return options;
        });

        services.AddSingleton<GmailClientProvider>();
        services.AddSingleton<IEmailGateway, GmailEmailGateway>();

        // Native auth/health bridge over the same provider singleton, so signed-in state and
        // AuthStateChanged notifications are a single source of truth shared with the gateway.
        services.AddSingleton<IConnectorAuthService, GmailConnectorAuthService>();

        return services;
    }
}
