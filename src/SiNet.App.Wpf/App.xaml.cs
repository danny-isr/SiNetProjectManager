using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Admin.Security;
using SiNet.App.Wpf.DevTools;
using SiNet.App.Wpf.Infrastructure;
using SiNet.App.Wpf.Shell;
using SiNet.App.Wpf.Surfaces.Email;
using SiNet.App.Wpf.Theme;
using SiNet.Application.Common;
using SiNet.Application.Configuration;
using SiNet.Application.Data;
using SiNet.Application.Identity;
using SiNet.Application.ProjectWork;
using SiNet.Application.Settings;
using SiNet.Infrastructure.Autodesk;
using SiNet.Infrastructure.Google;
using SiNet.Infrastructure.Logging;
using SiNet.Infrastructure.Secrets;
using SiNet.Infrastructure.Sql;
using SiNet.Infrastructure.Sql.Services.Identity;

namespace SiNet.App.Wpf;

/// <summary>
/// Production New System host (<c>SiNet.App.Wpf.exe</c>). See
/// <c>docs/STANDALONE_NEW_SYSTEM_HOST.md</c>. Does not reference SiNetSQL or SiNetProjectManagerV2.
/// Vault Google client secrets resolve via <see cref="IGoogleClientSecretsPathProvider"/>
/// (registered by <c>AddSiNetSecrets</c>) — not from env/appsettings as the primary source.
/// </summary>
// Base type is fully qualified: this project references the SiNet.Application namespace, so an
// unqualified "Application" would bind to that namespace instead of System.Windows.Application.
public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;
    private IConfiguration? _configuration;
    private readonly CancellationTokenSource _shutdownCts = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        AppGlobalExceptionHandling.Configure(this);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        StandaloneHostLoggingBootstrap.ConfigureDefault();
        StandaloneHostLoggingBootstrap.Warning("[STARTUP] Client process alive");

        // Without this bridge every AppErrorReporter.Report — including the global dispatcher
        // handler — only reached Debug.WriteLine and was invisible in the log file.
        AppErrorReporter.ExceptionReported += OnWpfExceptionReported;

        base.OnStartup(e);

        try
        {
            await RunStartupAsync(e).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StandaloneHostLoggingBootstrap.Fatal(ex, "[STARTUP] Standalone New System failed.");
            MessageBox.Show(
                $"הפעלת המערכת החדשה נכשלה:\n\n{ex.Message}",
                "שגיאת הפעלה",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private async Task RunStartupAsync(StartupEventArgs e)
    {
        _ = e;
        _configuration = BuildConfiguration();

        ThemeResourceLoader.EnsureApplicationResourcesMerged();
        var splash = new StartupSplashWindow();
        splash.Show();
        splash.SetStatus("מתחיל את מנהל הפרויקטים של שיא חדש...");
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);

        try
        {
            StandaloneHostLoggingBootstrap.Warning("[STARTUP] Standalone New System host starting (SiNet.App.Wpf).");

            splash.SetStatus("בודק כספת סודות ומסד נתונים...");
            // Vault setup may open a modal dialog — hide splash so it cannot cover the UI.
            splash.Hide();
            var vaultReady = await EnsureVaultDatabaseReadyAsync().ConfigureAwait(true);
            if (!vaultReady)
            {
                StandaloneHostLoggingBootstrap.Warning("[STARTUP] Vault/DB secret not configured. Shutting down.");
                Shutdown();
                return;
            }

            splash.Show();
            splash.SetStatus("מתחבר לשירותים...");

            var sqlConnectionString = ResolveSqlConnectionStringFromVault()
                ?? throw new InvalidOperationException(
                    "SiNet database connection string is missing from the Credential Vault after setup.");

            // Phase 2 logging: adds the central network sink from DB settings. Must run before the
            // provider is built — the logging adapter captures the host logger in its constructor.
            StandaloneHostLoggingBootstrap.ConfigureCentral(sqlConnectionString);

            splash.SetStatus("טוען שירותים...");
            var services = new ServiceCollection();
            services.AddSiNetStandaloneHost(
                _configuration,
                sqlConnectionString,
                ConfigureGmail,
                ConfigureSqlDiagnostics);

            _services = services.BuildServiceProvider();

            StartConnectorAuthRestore();

            splash.SetStatus("מאמת סכמת מסד...");
            StandaloneHostLoggingBootstrap.Info("[STARTUP] Schema gate...");
            if (!await ValidateSchemaAsync().ConfigureAwait(true))
            {
                Shutdown();
                return;
            }

#if DEBUG
            splash.Hide();
            await RunDebugAuthorizationRoleSelectorAsync().ConfigureAwait(true);
            splash.Show();
#endif

            splash.SetStatus("מאמת משתמש...");
            StandaloneHostLoggingBootstrap.Info("[STARTUP] Authorizing Windows user...");
            var authenticator = _services.GetRequiredService<SqlWindowsCurrentUserAuthenticator>();
            if (!await authenticator.TryAuthenticateAsync(_shutdownCts.Token).ConfigureAwait(true))
            {
                MessageBox.Show(
                    "המשתמש הנוכחי אינו מורשה להשתמש במערכת.\nנא לפנות למנהל המערכת.",
                    "אין הרשאה",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown();
                return;
            }

            splash.SetStatus("מחיל הגדרות...");
            await ApplySavedUserSettingsAsync().ConfigureAwait(true);
            await ApplyAccHostConfigFromSystemSettingsAsync().ConfigureAwait(true);

            splash.SetStatus("פותח את המעטפת...");
            StandaloneHostLoggingBootstrap.Info("[STARTUP] Opening NewShell...");
            var factory = _services.GetRequiredService<INewShellFactory>();
            var shell = await factory.CreateShellAsync(_shutdownCts.Token).ConfigureAwait(true);

            MainWindow = shell;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            shell.Show();

            ScheduleEmailBodyPdfRendererInit();

            StandaloneHostLoggingBootstrap.Warning("[STARTUP] Client session ready");
        }
        finally
        {
            splash.Close();
        }
    }

    /// <summary>
    /// Non-fatal warmup for ACC Inbox <c>00_Email.pdf</c> (N4). Lazy init still happens on first render.
    /// </summary>
    private void ScheduleEmailBodyPdfRendererInit()
    {
        var renderer = _services?.GetService<WpfEmailBodyPdfRenderer>();
        if (renderer is null)
        {
            return;
        }

        Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await renderer.InitializeAsync(_shutdownCts.Token).ConfigureAwait(true);
                StandaloneHostLoggingBootstrap.Info(
                    $"[STARTUP] Email body PDF renderer ready={renderer.IsAvailable}");
            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
            catch (Exception ex)
            {
                StandaloneHostLoggingBootstrap.Warning(
                    ex,
                    "[STARTUP] Email body PDF renderer init failed (non-fatal).");
            }
        });
    }

    private async Task<bool> EnsureVaultDatabaseReadyAsync()
    {
        if (!string.IsNullOrWhiteSpace(ResolveSqlConnectionStringFromVault()))
        {
            return true;
        }

        StandaloneHostLoggingBootstrap.Info(
            "[STARTUP] Opening native Secret Setup (database connection required).");

        var bootstrap = new ServiceCollection();
        bootstrap.AddSiNetVaultBootstrap();
        await using var provider = bootstrap.BuildServiceProvider();

        var window = provider.GetRequiredService<SecretSetupWindow>();
        // DialogResult may stay false when other optional secrets fail validation; accept any close
        // once the SiNet DB connection string is present in the vault.
        _ = window.ShowDialog();

        if (!string.IsNullOrWhiteSpace(ResolveSqlConnectionStringFromVault()))
        {
            return true;
        }

        MessageBox.Show(
            "נדרש מפתח חיבור למסד הנתונים (Credential Vault) כדי להפעיל את המערכת החדשה.",
            "חסרה כספת סודות",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }

    private async Task<bool> ValidateSchemaAsync()
    {
        var gate = _services!.GetRequiredService<IDatabaseSchemaGate>();
        var result = await gate.ValidateAsync(_shutdownCts.Token).ConfigureAwait(true);

        if (!result.CanConnect)
        {
            StandaloneHostLoggingBootstrap.Fatal("[STARTUP] Cannot connect to database.");
            MessageBox.Show(
                "לא ניתן להתחבר למסד הנתונים.\nנא לוודא שהשרת זמין ושמחרוזת החיבור ב-Vault תקינה.",
                "שגיאת חיבור",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }

        if (!result.IsSchemaPresent)
        {
            var tableList = string.Join(", ", result.MissingTables);
            StandaloneHostLoggingBootstrap.Fatal(
                $"Database schema is outdated. Missing tables: {tableList}");
            MessageBox.Show(
                "מבנה מסד הנתונים אינו עדכני.\n\n" +
                $"טבלאות חסרות: {tableList}\n\n" +
                "יש להריץ Update-Database / efbundle לעדכון המבנה.\n" +
                "פרטים: docs\\DATABASE_RECOVERY_BASELINE.md",
                "נדרש עדכון מסד נתונים",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        if (result.HasPendingMigrations)
        {
            var pendingList = string.Join(", ", result.PendingMigrations);
            StandaloneHostLoggingBootstrap.Fatal(
                $"Database migrations pending: {pendingList}");
            MessageBox.Show(
                "חסרות מיגרציות במסד הנתונים (הבילד מצפה למיגרציה אחרונה שעדיין לא הוחלה).\n\n" +
                $"מיגרציות ממתינות:\n{pendingList}\n\n" +
                "הפעל ב-Package Manager Console / CLI:\n" +
                "Update-Database -Context SiNetSQLDbContext " +
                "-Project SiNet.Infrastructure.Sql -StartupProject SiNetProjectManagerV2\n\n" +
                "או:\n" +
                "dotnet ef database update --context SiNetSQLDbContext " +
                "--project src\\SiNet.Infrastructure.Sql\\SiNet.Infrastructure.Sql.csproj " +
                "--startup-project SiNetProjectManagerV2\\SiNetProjectManagerV2.csproj",
                "נדרש Update-Database",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private static void OnWpfExceptionReported(Exception exception, string context) =>
        StandaloneHostLoggingBootstrap.Warning(exception, $"[UI] Reported error. Context={context}");

    private async Task ApplySavedUserSettingsAsync()
    {
        try
        {
            var appSettings = _services!.GetRequiredService<IAppSettingsService>();
            var loggingApplier = _services.GetRequiredService<ILoggingRuntimeApplier>();
            var logging = await appSettings.GetUserLoggingSettingsAsync(_shutdownCts.Token).ConfigureAwait(true);
            loggingApplier.ApplyUserLogging(logging);

            var theme = _services.GetRequiredService<ThemeStartupInitializer>();
            await theme.ApplySavedThemeAsync(_shutdownCts.Token).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StandaloneHostLoggingBootstrap.Warning(
                ex,
                "[STARTUP] Failed to apply saved user settings; continuing with defaults.");
        }
    }

    private async Task ApplyAccHostConfigFromSystemSettingsAsync()
    {
        try
        {
            var query = _services!.GetRequiredService<ISystemSettingsQueryService>();
            var settings = await query.GetSystemSettingsAsync(_shutdownCts.Token).ConfigureAwait(true);
            var hostConfig = _services.GetRequiredService<MutableSecretSetupHostConfiguration>();
            hostConfig.ApplySystemSettings(settings.Acc);

            var controlPlane = _services.GetService<AccServiceControlPlaneOptions>();
            if (controlPlane is not null
                && !string.IsNullOrWhiteSpace(settings.Acc.AccServicePinnedCertificateThumbprints))
            {
                controlPlane.PinnedCertificateThumbprints = AccServiceControlPlaneConfiguration.SplitPins(
                    settings.Acc.AccServicePinnedCertificateThumbprints);
            }

            StandaloneHostLoggingBootstrap.Info(
                $"[STARTUP] Acc host config applied. BaseUrl={(hostConfig.AccServiceBaseUrl ?? "(local)")}");

            if (_services.GetService<IProjectWorkScanExclusionPolicy>() is { } scanExclusions)
            {
                await scanExclusions.RefreshAsync(_shutdownCts.Token).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            StandaloneHostLoggingBootstrap.Warning(
                ex,
                "[STARTUP] Failed to load AccService settings from DB; using appsettings/vault defaults.");
        }
    }

    private void StartConnectorAuthRestore()
    {
        var connectorAuthServices = _services!.GetServices<IConnectorAuthService>().ToArray();
        _ = Task.Run(async () =>
        {
            foreach (var authService in connectorAuthServices)
            {
                try
                {
                    await authService.TryRestoreSessionAsync(_shutdownCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    StandaloneHostLoggingBootstrap.Warning(
                        ex,
                        $"[AuthRestore] {authService.GetType().Name} failed: {ex.Message}");
                }
            }
        }, _shutdownCts.Token);
    }

    private static IConfiguration BuildConfiguration() => new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
        .AddEnvironmentVariables()
        .Build();

    private void ConfigureGmail(GmailOptions options)
    {
        _configuration?.GetSection("Gmail").Bind(options);

        var drive = _configuration?.GetSection("GoogleDrive");
        if (drive is not null)
        {
            options.SharedDriveId ??= drive["SharedDriveId"];
            options.ProjectsRootFolderId ??= drive["ProjectsRootFolderId"];
        }

        var reports = _configuration?.GetSection("GoogleReports");
        if (reports is not null)
        {
            options.ReportsSharedDriveId ??= reports["SharedDriveId"];
            options.ReportsRootFolderId ??= reports["RootReportsFolderId"];
            options.R01TemplateSpreadsheetId ??= reports["R01TemplateSpreadsheetId"];
            options.R02TemplateSpreadsheetId ??= reports["R02TemplateSpreadsheetId"];
            if (int.TryParse(reports["BatchSize"], out var batchSize) && batchSize > 0)
                options.ReportsBatchSize = batchSize;
            if (int.TryParse(reports["BatchDelayMs"], out var batchDelay) && batchDelay >= 0)
                options.ReportsBatchDelayMs = batchDelay;
        }

        var tokenStore = Environment.GetEnvironmentVariable("SINET_GOOGLE_TOKEN_STORE");
        if (!string.IsNullOrWhiteSpace(tokenStore))
        {
            options.TokenStorePath = tokenStore;
        }
    }

#if DEBUG
    /// <summary>
    /// DEBUG-only: always shows the role selector on standalone Debug startups.
    /// Mutates <c>SIUser</c> only when the tester picks a role — not compiled in Release.
    /// (Opt-out: set env <c>SINET_SKIP_DEBUG_ROLE_SELECTOR=1</c>.)
    /// </summary>
    private Task RunDebugAuthorizationRoleSelectorAsync()
    {
        if (_services is null)
            return Task.CompletedTask;

        try
        {
            var skip = Environment.GetEnvironmentVariable("SINET_SKIP_DEBUG_ROLE_SELECTOR");
            if (string.Equals(skip, "1", StringComparison.Ordinal)
                || string.Equals(skip, "true", StringComparison.OrdinalIgnoreCase))
            {
                StandaloneHostLoggingBootstrap.Info(
                    "[STARTUP] Debug Authorization Role Selector skipped (SINET_SKIP_DEBUG_ROLE_SELECTOR).");
                return Task.CompletedTask;
            }

            StandaloneHostLoggingBootstrap.Info("[STARTUP] Debug Authorization Role Selector...");
            var overrideService = _services.GetRequiredService<IDebugAuthorizationRoleOverrideService>();
            var window = new DebugAuthorizationRoleSelectorWindow(overrideService);
            _ = window.ShowDialog();
        }
        catch (Exception ex)
        {
            StandaloneHostLoggingBootstrap.Warning(
                ex,
                "[STARTUP] Failed to run Debug Authorization Role Selector.");
        }

        return Task.CompletedTask;
    }
#endif

    private static string? ResolveSqlConnectionStringFromVault()
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
            StandaloneHostLoggingBootstrap.Warning(
                ex,
                "[STARTUP] Failed to read SiNet DB connection string from Credential Vault.");
            return null;
        }
    }

    private static void ConfigureSqlDiagnostics(SiNetSqlOptions options)
    {
#if DEBUG
        options.EnableEfDebugDiagnostics = true;
#endif
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shutdownCts.Cancel();
        _shutdownCts.Dispose();

        // Root provider owns IAsyncDisposable-only services (e.g. GmailClientProvider).
        // Sync Dispose() throws InvalidOperationException for those — must DisposeAsync.
        if (_services is not null)
        {
            try
            {
                _services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                StandaloneHostLoggingBootstrap.Warning(
                    ex,
                    "[SHUTDOWN] ServiceProvider DisposeAsync failed.");
            }
            finally
            {
                _services = null;
            }
        }

        StandaloneHostLoggingBootstrap.CloseAndFlush();
        base.OnExit(e);
    }
}
