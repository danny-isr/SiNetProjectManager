using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SiNet.Application.Abstractions.Email;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Common;
using SiNet.Application.Configuration;
using SiNet.Application.Email;
using SiNet.Application.MasterPlan.Reports;
using SiNet.Application.Projects;
using SiNet.Application.ProjectWork;
using SiNet.Infrastructure.Google.ProjectWork;
using SiNet.Infrastructure.Google.Reports;

namespace SiNet.Infrastructure.Google;

/// <summary>
/// Modular DI registration for the native Google module: shared user OAuth
/// (<see cref="GmailClientProvider"/>) for Gmail + Drive + Sheets, Gmail gateway/send/modify,
/// <see cref="IConnectorAuthService"/>, ProjectWork <see cref="GoogleDriveFileStore"/>,
/// and MasterPlan R01–R03 report services.
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
        services.AddSingleton<IGmailLabelChangeJournal, LocalGmailLabelChangeJournal>();

        // ProjectWork Google Drive: Shared Drive primitives + IFileStore over the shared session.
        services.AddSingleton<IGoogleDriveFileService, GoogleDriveFileService>();
        services.AddSingleton<IProjectDriveRootRenameService, GoogleDriveProjectRootRenameService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFileStore, GoogleDriveFileStore>());

        // MasterPlan Reports (R01/R02/R03) — require IR0xReportDataSource from AddSiNetUserManagementSql.
        services.AddTransient<IMasterPlanR03ReportService, NativeR03ReportService>();
        services.AddTransient<IMasterPlanR01ReportService, NativeR01ReportService>();
        services.AddTransient<IMasterPlanR02ReportService, NativeR02ReportService>();

        return services;
    }
}
