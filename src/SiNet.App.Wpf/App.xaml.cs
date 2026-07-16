using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Autodesk;
using SiNet.App.Composition;
using SiNet.App.Wpf.Infrastructure;
using SiNet.App.Wpf.Inbox;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Common;
using SiNet.Application.Configuration;
using SiNet.Infrastructure.Google;
using SiNet.Infrastructure.Secrets;
using SiNet.Infrastructure.Sql;

namespace SiNet.App.Wpf;

/// <summary>
/// WPF host scaffold. Demonstrates the intended startup shape for the App-startup migration
/// round: build configuration, build the service graph with the single <c>AddSiNet()</c>
/// composition call, then resolve the shell window from DI. This scaffold is NOT yet the
/// production entry point and does not replace the existing application.
/// </summary>
// Base type is fully qualified: this project references the SiNet.Application namespace, so an
// unqualified "Application" would bind to that namespace instead of System.Windows.Application.
public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;
    private IConfiguration? _configuration;

    protected override void OnStartup(StartupEventArgs e)
    {
        AppGlobalExceptionHandling.Configure(this);
        base.OnStartup(e);

        _configuration = BuildConfiguration();

        var services = new ServiceCollection();
        services.AddSingleton(_configuration);
        services.AddSiNet(ConfigureGmail);
        services.AddSiNetSecrets();

        // Vault-sourced SQL wiring: the native process backbone (workflow engine, task completion,
        // action handlers) and the identity/settings services need a real IDbContextFactory. The
        // connection string is the single-source-of-truth secret in the Credential Vault; when it is
        // absent the host degrades gracefully (SQL-backed features simply stay unavailable) rather
        // than crashing at startup — mirroring the Gmail no-secrets behavior.
        var sqlConnectionString = TryResolveSqlConnectionStringFromVault();
        if (!string.IsNullOrWhiteSpace(sqlConnectionString))
        {
            services.AddSiNetSql(sqlConnectionString, ConfigureSqlDiagnostics);
            services.AddSiNetSystemSettingsSql();
            services.AddSiNetAuthorizationSql();
        }

        services.AddSiNetNewSystemWpf();
        services.AddSingleton<InboxViewModel>();
        services.AddSingleton<MainWindow>();

        // New Inspection screen foundation (now surfaced as a safe Inbox/Inspection tab switch).
        services.AddSingleton<InspectionTreeViewModel>();
        services.AddSingleton<InspectionNotesViewModel>();
        services.AddSingleton<InspectionDrawingsViewModel>();
        services.AddSingleton<InspectionReviewedPlanViewModel>();
        services.AddSingleton<InspectionReportViewModel>();
        services.AddSingleton<InspectionShellViewModel>();
        services.AddSingleton<InspectionShellView>();
        services.AddSingleton<MainViewModel>();

        _services = services.BuildServiceProvider();

        // Attempt a silent (no-browser) restore for any connector auth services registered in the
        // New System graph. This keeps startup independent of concrete providers such as
        // GmailClientProvider and scales to future connector-auth consumers without adding more
        // WPF-side service knowledge.
        var connectorAuthServices = _services.GetServices<IConnectorAuthService>().ToArray();
        _ = Task.Run(async () =>
        {
            foreach (var authService in connectorAuthServices)
            {
                await authService.TryRestoreSessionAsync().ConfigureAwait(false);
            }
        });

        var window = _services.GetRequiredService<MainWindow>();
        window.Show();
    }

    /// <summary>
    /// Builds the host configuration from <c>appsettings.json</c> (the real config source) with
    /// environment variables layered on top. The new stack owns its own configuration and has no
    /// dependency on the legacy <c>SiNetProjectManagerV2.AppConfiguration</c>.
    /// </summary>
    private static IConfiguration BuildConfiguration() => new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
        .AddEnvironmentVariables()
        .Build();

    /// <summary>
    /// Configures the native Gmail integration. Values are bound from the <c>Gmail</c> section of
    /// <c>appsettings.json</c>; the legacy <c>SINET_GOOGLE_*</c> environment variables, when set,
    /// override the file so existing developer setups keep working. When no client secrets are
    /// configured, <see cref="GmailOptions.ClientSecretsPath"/> stays empty and the gateway
    /// degrades gracefully (no sign-in, empty inbox) instead of throwing.
    /// When <see cref="IGoogleClientSecretsPathProvider"/> is registered (AddSiNetSecrets), the
    /// vault is the source of truth; config/env paths are fallback only.
    /// </summary>
    private void ConfigureGmail(GmailOptions options)
    {
        _configuration?.GetSection("Gmail").Bind(options);

        // ProjectWork Drive base folder (Shared Drive + projects root) — same keys as V2 host.
        var drive = _configuration?.GetSection("GoogleDrive");
        if (drive is not null)
        {
            options.SharedDriveId ??= drive["SharedDriveId"];
            options.ProjectsRootFolderId ??= drive["ProjectsRootFolderId"];
        }

        // Token store env override only — client secrets come from Vault via IGoogleClientSecretsPathProvider.
        var tokenStore = Environment.GetEnvironmentVariable("SINET_GOOGLE_TOKEN_STORE");
        if (!string.IsNullOrWhiteSpace(tokenStore))
        {
            options.TokenStorePath = tokenStore;
        }
    }

    /// <summary>
    /// Reads the SiNet database connection string from the Credential Vault (the single source of
    /// truth). Uses a throwaway bootstrap provider so the vault store can be resolved before the main
    /// service graph is built. Returns <see langword="null"/> when the secret is missing or the vault
    /// is unavailable, so the host can degrade gracefully instead of failing startup.
    /// </summary>
    private static string? TryResolveSqlConnectionStringFromVault()
    {
        try
        {
            var bootstrap = new ServiceCollection();
            bootstrap.AddSiNetSecrets();
            using var provider = bootstrap.BuildServiceProvider();

            var vault = provider.GetRequiredService<ISecretVaultStore>();
            var raw = vault.GetSecret(SecretCatalog.SiNetDatabase);
            return string.IsNullOrWhiteSpace(raw) ? null : raw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[App] Failed to read SiNet DB connection string from the Credential Vault: {ex}");
            return null;
        }
    }

    /// <summary>
    /// Enables EF diagnostics only in Debug builds, matching the legacy host's development-time
    /// behavior. Release behavior is unchanged (diagnostics stay off).
    /// </summary>
    private static void ConfigureSqlDiagnostics(SiNetSqlOptions options)
    {
#if DEBUG
        options.EnableEfDebugDiagnostics = true;
#endif
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
