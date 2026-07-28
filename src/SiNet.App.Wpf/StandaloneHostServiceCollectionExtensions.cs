using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SiNet.App.Composition;
using SiNet.App.Wpf.Admin.Security;
using SiNet.App.Wpf.Infrastructure;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Theme;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Configuration;
using SiNet.Application.Identity;
using SiNet.Application.Settings;
using SiNet.Infrastructure.Autodesk;
using SiNet.Infrastructure.Google;
using SiNet.Infrastructure.Logging;
using SiNet.Infrastructure.Secrets;
using SiNet.Infrastructure.Sql;

namespace SiNet.App.Wpf;

/// <summary>
/// Composition for the production standalone New System host (<c>SiNet.App.Wpf.exe</c>).
/// See <c>docs/STANDALONE_NEW_SYSTEM_HOST.md</c>.
/// </summary>
public static class StandaloneHostServiceCollectionExtensions
{
    public static IServiceCollection AddSiNetStandaloneHost(
        this IServiceCollection services,
        IConfiguration configuration,
        string sqlConnectionString,
        Action<GmailOptions> configureGmail,
        Action<SiNetSqlOptions>? configureSql = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlConnectionString);
        ArgumentNullException.ThrowIfNull(configureGmail);

        services.AddSingleton(configuration);

        // Identity session must precede AddSiNet so NullCurrentUserContext is not registered.
        services.AddSiNetIdentitySql();

        // Vault / host config before AddSiNet so UserManagement does not register Null* providers.
        services.AddSingleton<MutableSecretSetupHostConfiguration>();
        services.AddSingleton<ISecretSetupHostConfiguration>(sp =>
            sp.GetRequiredService<MutableSecretSetupHostConfiguration>());
        services.AddSiNetSecrets();
        services.AddSingleton<IDirectoryUserConnectionProvider, VaultDirectoryUserConnectionProvider>();
        services.AddTransient<IDirectoryUserLookupService, ActiveDirectoryUserLookupService>();
        services.AddSingleton<IMasterPlanEmployeeConnectionProvider, VaultMasterPlanEmployeeConnectionProvider>();
        services.AddSiNetAutodeskVaultTokenProvider();

        services.AddSiNet(SiNetHostMode.StandaloneNew, configureGmail);
        services.AddSiNetSerilogLogging();
        services.AddSiNetUserLoggingSettings();
        services.AddSingleton<ILoggingRuntimeApplier, WpfLoggingRuntimeApplier>();
        services.AddSiNetThemeWpf();

        services.AddSiNetSql(sqlConnectionString, configureSql ?? (_ => { }));
        services.AddSiNetSystemSettingsSql();
        services.AddSiNetAuthorizationSql();
        services.AddSiNetFilingServices();

        // Prefer Drive catalog over EmptyInspectionTemplateCatalog (TryAdd in WorkSurfaces).
        services.AddSingleton<IInspectionTemplateCatalog, GoogleDriveInspectionTemplateCatalog>();

        services.AddSiNetNewSystemWpf();

        // DEBUG Inspection harness (menu item gated in NewShellFactory).
        services.TryAddSingleton<InspectionTreeViewModel>();
        services.TryAddSingleton<InspectionNotesViewModel>();
        services.TryAddSingleton<InspectionDrawingsViewModel>();
        services.TryAddSingleton<InspectionReviewedPlanViewModel>();
        services.TryAddSingleton<InspectionReportViewModel>();
        services.TryAddSingleton<InspectionShellViewModel>();
        services.TryAddSingleton<InspectionShellView>();

        return services;
    }

    /// <summary>
    /// Minimal DI graph for vault bootstrap before SQL exists (native Secret Setup only).
    /// Does not register <see cref="Autodesk.AccControlPlaneStatusPresenter"/> so ACC status stays optional.
    /// </summary>
    public static IServiceCollection AddSiNetVaultBootstrap(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSiNetSecrets();
        services.AddTransient<SecretSetupViewModel>();
        services.AddTransient<SecretSetupWindow>();

        return services;
    }
}
