using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Common;
using SiNet.Application.Configuration;
using SiNet.Application.ProjectWork;
using SiNet.Infrastructure.Google.ProjectWork;

namespace SiNet.Infrastructure.Google;

/// <summary>
/// Modular DI registration for the native Google module: shared user OAuth
/// (<see cref="GmailClientProvider"/>) for Gmail + Drive, Gmail gateway/send/modify,
/// <see cref="IConnectorAuthService"/>, and ProjectWork <see cref="GoogleDriveFileStore"/>.
/// </summary>
public static class GoogleServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Google module with default options. The host should normally use the
    /// <see cref="AddSiNetGoogle(IServiceCollection, Action{GmailOptions})"/> overload to point
    /// the gateway at its client secrets, token store, and Drive folder ids.
    /// </summary>
    public static IServiceCollection AddSiNetGoogle(this IServiceCollection services)
        => services.AddSiNetGoogle(static _ => { });

    /// <summary>
    /// Registers the Google module and lets the host configure <see cref="GmailOptions"/>
    /// (client secrets path, token store, root label, interactive sign-in, Drive folder ids).
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

        // Shared user credential owner for Gmail + Drive (one token, auto-refresh).
        services.AddSingleton<GmailClientProvider>(sp => new GmailClientProvider(
            sp.GetRequiredService<GmailOptions>(),
            sp.GetRequiredService<IAppLogger>(),
            sp.GetService<IGoogleClientSecretsPathProvider>()));
        services.AddSingleton<IEmailGateway, GmailEmailGateway>();

        // Native auth/health bridge over the same provider singleton, so signed-in state and
        // AuthStateChanged notifications are a single source of truth shared with the gateway.
        services.AddSingleton<IConnectorAuthService, GmailConnectorAuthService>();

        // Native Gmail send over the same provider singleton. Requires the GmailSend scope; until a
        // user re-consents, SendAsync reports RequiresConsent rather than throwing.
        services.AddSingleton<IEmailSender, GmailEmailSender>();
        services.AddSingleton<IEmailGmailModifyService, GmailEmailModifyService>();

        // ProjectWork Google Drive: Shared Drive primitives + IFileStore over the shared session.
        services.AddSingleton<IGoogleDriveFileService, GoogleDriveFileService>();
        services.AddSingleton<IFileStore, GoogleDriveFileStore>();

        return services;
    }
}
