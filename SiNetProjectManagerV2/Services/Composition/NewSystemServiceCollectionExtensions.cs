using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf;
using SiNet.Application.Abstractions.Autodesk;
using SiNet.Application.Configuration;
using SiNet.Application.Identity;
using SiNet.Application.Settings;
using SiNet.Infrastructure.Autodesk;
using SiNet.Infrastructure.Google;
using SiNet.Infrastructure.Logging;
using SiNet.Infrastructure.Secrets;
using SiNet.Infrastructure.Sql.Services.DevTools;
using SiNetProjectManagerV2.Services;

namespace SiNetProjectManagerV2.Services.Composition;

/// <summary>
/// Modular DI for the New System shell graph (Project Context + shell menu + admin surfaces).
/// Legacy host still shares the same container today; this extension documents the New System slice
/// explicitly (P7 composition split stepping stone).
/// </summary>
public static class NewSystemServiceCollectionExtensions
{
    /// <summary>
    /// Registers Project Context and the clean shell factory. Does not register legacy window factories.
    /// Call after SQL project reads and Inspection shell views are registered.
    /// </summary>
    public static IServiceCollection AddSiNetNewSystemGraph(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        SiNet.Infrastructure.Sql.ProjectQueryServiceCollectionExtensions.AddSiNetProjectQuerySql(services);
        SiNet.Infrastructure.Sql.ProjectQueryServiceCollectionExtensions.AddSiNetProjectCreateSql(services);
        SiNet.Infrastructure.Sql.UserManagementServiceCollectionExtensions.AddSiNetUserManagementSql(services);
        services.AddSiNetSecrets();
        services.AddSiNetSerilogLogging();
        services.AddSiNetUserLoggingSettings();
        SiNet.Infrastructure.Sql.SystemSettingsServiceCollectionExtensions.AddSiNetSystemSettingsSql(services);
        services.AddTransient<IAccInboxBootstrapLocalExecutor, LegacyHostLocalAccInboxBootstrapExecutor>();
        services.AddSingleton<ILoggingRuntimeApplier, LegacyLoggingRuntimeApplier>();
        SiNet.App.Wpf.Theme.ThemeServiceCollectionExtensions.AddSiNetThemeWpf(services);
        services.AddSiNetAutodesk();
        services.AddSingleton(LegacyGoogleClientSecretsFallback.Create());
        services.AddSiNetGoogle(ConfigureNewSystemGmail);
        SiNet.Infrastructure.Sql.EmailReadServiceCollectionExtensions.AddSiNetEmailReadSql(services);
        SiNet.Infrastructure.Sql.EmailWriteServiceCollectionExtensions.AddSiNetEmailWriteSql(services);
        SiNet.Infrastructure.Sql.EmailAccServiceCollectionExtensions.AddSiNetEmailAccSql(services);
        SiNet.Infrastructure.Sql.EmailDetailServiceCollectionExtensions.AddSiNetEmailDetailSql(services);
        services.AddSiNetNewSystemWpf();
        services.AddSiNetDevTools();
        services.AddSingleton<IMasterPlanEmployeeConnectionProvider, LegacyMasterPlanEmployeeConnectionProvider>();
        services.AddSingleton<IDirectoryUserConnectionProvider, LegacyDirectoryUserConnectionProvider>();
        services.AddSingleton<ISecretSetupHostConfiguration, LegacySecretSetupHostConfiguration>();
        services.AddTransient<IDirectoryUserLookupService, ActiveDirectoryUserLookupService>();

        return services;
    }

    private static void ConfigureNewSystemGmail(GmailOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.TokenStorePath = AppConfiguration.GoogleTokenStorePath;
        options.ApplicationName = AppConfiguration.GoogleApplicationName;
    }
}
