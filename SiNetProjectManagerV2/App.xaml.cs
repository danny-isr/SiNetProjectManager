using SiNetProjectManagerV2.Services;
using SiNetProjectManagerV2.WPF;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using SiNetSQL.MVVM; // core dialog interface
using System.IO;
using System.Reflection;
using SiNetSQL.Diagnostics;
using SiNetSQL.Services;
using SiNetSQL.Services.AccBootstrap;
using SiNetSQL.Services.EmailIngestion;
using SiNetSQL.Services.Logging;
using SiOffice.GoogleConnector.Logging;
using SiOffice.GoogleConnector;
using SiOffice.GoogleConnector.RateLimiting;
using SiOffice.GoogleConnector.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PdfSharp.Fonts;
using SiNetProjectManagerV2.Services.Stamping;

namespace SiNetProjectManagerV2
{
    public partial class App : Application
    {
        private static Mutex? _mutex; // created only when single-instance is enabled
        public static AppSettings? AppSettings { get; private set; }
        public static string SessionId { get; } = Guid.NewGuid().ToString("N");
        private static string _logDir = string.Empty;

        /// <summary>
        /// CancellationTokenSource for background tasks. Cancelled on app exit.
        /// </summary>
        private static readonly CancellationTokenSource _appShutdownCts = new();

        /// <summary>
        /// DI Service Provider for the application.
        /// Use ServiceProvider.GetRequiredService&lt;T&gt;() to resolve services.
        /// </summary>
        public static IServiceProvider ServiceProvider { get; private set; } = null!;

        static App()
        {
            // PdfSharp 6.x on .NET 8 requires an explicit font resolver
            // (system fonts are not auto-discovered).
            GlobalFontSettings.FontResolver = new WindowsFontResolver();

            _logDir = GetLogDirectory();
            try { Directory.CreateDirectory(_logDir); } catch { }

            // Sync AppLogger's display directory with the resolved local log directory.
            AppLogger.LogDirectory = _logDir;

            // ─── Centralized logging — DB-driven config (single source of truth) ───
            // Connection string is read from the Windows Credential Manager. If the
            // vault is not provisioned yet (first run), the loader silently falls
            // back to compile-time defaults so the logger always boots.
            var loggingConnectionString =
                CredentialVaultService.GetSecret(SecretKeys.SiNetDatabase);

            var loggingConfig = CentralLoggingSettings.LoadFromDatabase(
                loggingConnectionString,
                SiNetApp.Client,
                enableConsole: false,
                // Local file level is controlled at runtime by the user's
                // "Enable detailed logging" toggle (Settings → Logging).
                localFileLevelSwitch: AppLogger.FileLevelSwitch) with
            {
                LocalLogDirectory = _logDir
            };

            var logConfig = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .Enrich.WithProperty("SessionId", SessionId)
                .AddSiNetCentralLogging(loggingConfig);

            // ═══ DEBUG OUTPUT SINK: VS Output window (DEBUG builds only) ═══
            // Shows ALL log levels in the Output window with the same format as the file.
            // No level filtering — everything that reaches Serilog appears in Output.
#if DEBUG
            logConfig = logConfig.WriteTo.Sink(new DebugOutputSink(CentralLoggingDefaults.OutputTemplate));
#endif

            Log.Logger = logConfig.CreateLogger();

            // Wire AppLog to Serilog
            AppLog.ErrorHandler = (ex, op, ctx) => Log.Error(ex, "Operation {Operation} failed. Context={@Context}", op, ctx ?? new { });
            AppLog.FatalHandler = (ex, op, ctx) => Log.Fatal(ex, "Operation {Operation} failed. Context={@Context}", op, ctx ?? new { });
        }

        public static void ApplySettings()
        {
            if (AppSettings == null) return;

            // Update all dynamic resources — this triggers immediate UI updates across ALL windows
            Current.Resources["AppFontFamily"] = new FontFamily(AppSettings.FontFamily);
            Current.Resources["AppFontSize"] = AppSettings.FontSize;
            Current.Resources["AppFontSizeSecondary"] = Math.Round(AppSettings.FontSize * 0.9, 1);
            Current.Resources["AppFontSizeSmall"] = Math.Round(AppSettings.FontSize * 0.65, 1);
            Current.Resources["AppForeground"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(AppSettings.ForegroundColor));
            Current.Resources["AppBackground"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(AppSettings.BackgroundColor));

            AppLogger.Info($"[Settings] UI theme updated globally — Font={AppSettings.FontFamily}, Size={AppSettings.FontSize}, FG={AppSettings.ForegroundColor}, BG={AppSettings.BackgroundColor}");
        }

        /// <summary>
        /// Call this method after saving settings to immediately apply changes to all open windows.
        /// This is the single source of truth for theme updates.
        /// </summary>
        public static void RefreshTheme()
        {
            AppSettings = SettingsManager.LoadSettings();
            ApplySettings();
        }

        public static class DialogServiceLocator
        {
            // Single, unambiguous interface reference
            public static SiNetSQL.MVVM.IDialogService? Instance { get; set; }
        }

        /// <summary>
        /// Configures the DI container with all application services.
        /// DbContext uses IDbContextFactory for WPF lifetime safety (short-lived contexts per operation).
        /// </summary>
        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // ═══════════════════════════════════════════════════════════════════
            // LOGGING: Wire Microsoft.Extensions.Logging to Serilog
            // Enables ILogger<T> injection throughout the application.
            // ═══════════════════════════════════════════════════════════════════
            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddSerilog(Log.Logger, dispose: false); // Serilog manages its own lifecycle
            });

            // ═══════════════════════════════════════════════════════════════════
            // DATABASE: Register DbContextFactory for safe WPF usage
            // Factory pattern ensures each operation gets a fresh, short-lived context.
            // This prevents stale data and threading issues in long-running WPF apps.
            // Single source of truth: Windows Credential Manager (vault key: SiNet/ConnectionStrings/SiNetDatabase)
            // ═══════════════════════════════════════════════════════════════════
            var connectionString = AppConfiguration.GetConnectionString("SiNetDatabase")
                ?? throw new InvalidOperationException(
                    "חסר connection string ל-SiNetDatabase. הגדר ב-Credential Manager דרך חלון הגדרת סודות (SiNet/ConnectionStrings/SiNetDatabase).");

            services.AddDbContextFactory<SiNetSQL.Data.SiNetSQLDbContext>(options =>
            {
                // UseCompatibilityLevel(120) prevents EF Core 8 from generating OPENJSON($)
                // for collection.Contains() queries — requires SQL Server 2016+ (compat 130).
                // Our DB has a lower compat level, causing "Incorrect syntax near '$'".
                options.UseSqlServer(connectionString, sql => sql.UseCompatibilityLevel(120));
#if DEBUG
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
#endif
            });

            // ═══════════════════════════════════════════════════════════════════
            // SERVICES: Register application services
            // Services that need DbContext should receive IDbContextFactory<T>
            // and create short-lived contexts per operation.
            // ═══════════════════════════════════════════════════════════════════

            // DialogService: Singleton (UI service, shared across app)
            services.AddSingleton<SiNetSQL.MVVM.IDialogService, DialogService>();

            // User Service: Transient (lightweight, no state)
            services.AddTransient<ISiUserService, SiUserService>();

            // Email Ingestion Service Factory: Singleton (caches ACC configuration)
            services.AddSingleton<SiNetSQL.Services.IEmailIngestionServiceFactory, SiNetSQL.Services.EmailIngestionServiceFactory>();

            // PDF Renderer: Singleton (reused for all PDF generations)
            services.AddSingleton<WebView2PdfRenderer>();

            // ACC User Bootstrap Service: Transient (runs once at startup)
            services.AddTransient<IAccUserBootstrapService, AccUserBootstrapService>();

            // Status Color Service: Singleton (caches resolved status colors per user)
            services.AddSingleton<SiNetSQL.Services.StatusColorService>();

            // System Settings Service: Singleton (caches global DB settings in-memory)
            services.AddSingleton<SiNetSQL.Services.SystemSettingsService>();

            // Google Auth: Singleton (shared credential across all Google-connected windows)
            // Ensures the user authenticates once per session instead of per-window.
            services.AddSingleton(sp => new GoogleAuthService(
                AppConfiguration.GetGoogleClientSecretsPath() ?? string.Empty,
                AppConfiguration.GoogleTokenStorePath,
                AppConfiguration.GoogleApplicationName));

            // Gmail Service: Singleton (shared Gmail credential + throttle across all email views)
            // Ensures the user authenticates once per session instead of per-navigation.
            services.AddSingleton<IGmailThrottleService, GmailThrottleService>();
            services.AddSingleton<GoogleService>();

            // Workflow Services: Transient (short-lived, use IDbContextFactory internally)
            services.AddTransient<SiNetSQL.Services.Workflow.WorkflowEngine>();
            services.AddTransient<SiNetSQL.Services.Workflow.WorkflowTransitionEvaluator>();
            services.AddTransient<SiNetSQL.Services.Workflow.WorkflowActionExecutor>();
            services.AddTransient<SiNetSQL.Services.Workflow.WorkflowTaskOrchestrator>();
            services.AddTransient<SiNetSQL.Services.Workflow.WorkflowQueryService>();
            services.AddTransient<SiNetSQL.Services.Workflow.WorkflowValidationService>();
            services.AddTransient<SiNetSQL.Services.Workflow.WorkflowSeedService>();
            services.AddTransient<SiNetSQL.Services.Workflow.ProjectWorkflowPolicyService>();

            // Task Lifecycle Services: Transient (auto-create/auto-close tasks based on behavior definitions)
            services.AddTransient<SiNetSQL.Services.TaskLifecycle.TaskLifecycleService>();
            services.AddTransient<SiNetSQL.Services.TaskLifecycle.TaskBehaviorSeedService>();

            // Email Context Services: Transient (analyze email → business context → actions)
            services.AddTransient<SiNetSQL.Services.EmailContext.EmailContextAnalyzer>();
            services.AddTransient<SiNetSQL.Services.EmailContext.SuggestedActionsBuilder>();
            services.AddTransient<SiNetSQL.Services.EmailContext.ActionExecutor>();

            // File Import Coordinator: Transient (orchestrates email attachment → project filesystem)
            services.AddTransient<SiNetSQL.Services.Coordinators.FileImportCoordinator>();

            // ACC File Sync: Transient (copies tagged attachments from ACC Inbox → ACC project folders)
            services.AddTransient<SiNetSQL.Services.Coordinators.AccFileSyncService>();

            // Project File Upload: Transient (uploads project file alternatives to ACC/GoogleDrive)
            services.AddTransient<SiNetSQL.Services.Coordinators.ProjectFileUploadService>();

            // ACC Metadata Status Reporter: Singleton (shared collector of Custom-Attribute
            // failures — surfaced as a badge in ProjectWorkView so the user sees when
            // their ACC role / license can't read/write metadata).
            services.AddSingleton<SiNetSQL.FileIndex.IAccMetadataStatusReporter, SiNetSQL.FileIndex.AccMetadataStatusReporter>();

            // File Index: unified scan/open/upload abstraction over FileServer / ACC / GoogleDrive.
            // Stores are registered as IFileStore so FileIndexService can enumerate them all.
            services.AddSingleton<SiNetSQL.FileIndex.IFileStore, SiNetSQL.FileIndex.Stores.FileServerStore>();
            services.AddSingleton<SiNetSQL.FileIndex.IFileStore, SiNetSQL.FileIndex.Stores.AccFileStore>();
            services.AddSingleton<SiNetSQL.FileIndex.IFileStore, SiNetSQL.FileIndex.Stores.GoogleDriveStore>();
            services.AddSingleton<SiNetSQL.FileIndex.FileIndexService>();

            // Drag-and-drop "replace existing file" flow: stateless service +
            // WPF-backed prompt provider. Singleton because the dialogs are
            // pure (no per-call state) — keeps allocation noise down on hot drops.
            services.AddSingleton<SiNetSQL.FileIndex.IFileReplacePrompts,
                                  SiNetProjectManagerV2.Services.FileReplacePrompts>();
            services.AddSingleton<SiNetSQL.FileIndex.FileReplaceService>();

            // Ollama AI Service: Singleton (shared HTTP client for local Ollama server)
            // On first resolve, checks DB for saved BaseUrl/Model overrides.
            services.AddSingleton(sp =>
            {
                var ollama = new SiNetSQL.Services.OllamaService(
                    AppConfiguration.Configuration,
                    sp.GetService<ILoggerFactory>()?.CreateLogger<SiNetSQL.Services.OllamaService>());

                // Apply DB overrides if available (non-blocking — DB is already cached by this point)
                var settings = sp.GetRequiredService<SystemSettingsService>();
                var dbUrl = Task.Run(() => settings.GetAsync(SystemSettingKeys.OllamaBaseUrl).AsTask()).GetAwaiter().GetResult();
                if (!string.IsNullOrWhiteSpace(dbUrl))
                    ollama.BaseUrl = dbUrl;
                var dbModel = Task.Run(() => settings.GetAsync(SystemSettingKeys.OllamaModel).AsTask()).GetAwaiter().GetResult();
                if (!string.IsNullOrWhiteSpace(dbModel))
                    ollama.Model = dbModel;

                return ollama;
            });

            // Task Status Resolver: Singleton (cached open/closed status ID lookups)
            services.AddSingleton<SiNetSQL.Services.TaskStatusResolver>();

            // ACC Project Provisioning:
            //   • If "AccService:BaseUrl" is configured (production), forward all
            //     privileged ACC operations to SiOffice.AccService over HTTPS so
            //     regular users don't need Account Admin credentials.
            //   • Otherwise (dev / standalone install), fall back to the in-process
            //     AccProjectProvisioningService, which talks to ACC directly.
            // The shared API key is read from the same vault used for every other
            // secret (SecretKeys.AccServiceApiKey).
            var accServiceBaseUrl = AppConfiguration.Configuration["AccService:BaseUrl"];
            if (!string.IsNullOrWhiteSpace(accServiceBaseUrl))
            {
                var apiKey = SiNetSQL.Services.CredentialVaultService.GetSecret(
                    SiNetSQL.Services.SecretKeys.AccServiceApiKey);

                // Shared HttpClient configurator — same base address, header and
                // infinite timeout for both the project-provisioning and the
                // inbox-provisioning typed clients.
                void ConfigureAccServiceClient(HttpClient client)
                {
                    client.BaseAddress = new Uri(accServiceBaseUrl.TrimEnd('/') + "/");
                    // Long-running endpoints (ensure-mapping ≈ 1–2 min) rely on the
                    // per-call CancellationToken; HttpClient.Timeout would short-circuit it.
                    client.Timeout = Timeout.InfiniteTimeSpan;
                    if (!string.IsNullOrWhiteSpace(apiKey))
                    {
                        client.DefaultRequestHeaders.Add(
                            SiNetSQL.Services.AccBootstrap.Contracts.AccServiceContracts.ApiKeyHeader,
                            apiKey);
                    }
                }

                Action<HttpClient> configure = ConfigureAccServiceClient;
                services.AddHttpClient<IAccProjectProvisioningService, RemoteAccProjectProvisioningService>(configure);
                services.AddHttpClient<SiNetSQL.Services.AccBootstrap.IAccInboxProvisioner, RemoteAccInboxProvisioner>(configure);
                Log.Information("ACC Provisioning: using REMOTE SiOffice.AccService at {Url}.", accServiceBaseUrl);
            }
            else
            {
                services.AddTransient<IAccProjectProvisioningService, AccProjectProvisioningService>();
                services.AddTransient<SiNetSQL.Services.AccBootstrap.IAccInboxProvisioner,
                                      SiNetSQL.Services.AccBootstrap.LocalAccInboxProvisioner>();
                Log.Information("ACC Provisioning: using LOCAL in-process AccProjectProvisioningService (AccService:BaseUrl not configured).");
            }

            // ACC Membership Reconciler: Singleton (single background worker, debounced Channel).
            // Re-syncs every ACC project's members with the local Siuser table when triggered.
            services.AddSingleton<IAccMembershipReconciler, AccMembershipReconciler>();

            // ═══════════════════════════════════════════════════════════════════
            // VIEWMODELS: Register all ViewModels that use IDbContextFactory
            // Transient lifetime ensures each request gets a fresh instance.
            // ═══════════════════════════════════════════════════════════════════
            services.AddTransient<TaskPanelViewModel>();
            services.AddTransient<FloatingProjectTasksViewModel>();
            services.AddTransient<FloatingInspectionViewModel>();
            services.AddTransient<ProjectTypeRulesViewModel>();
            services.AddTransient<ProjectTypeViewModel>();
            services.AddTransient<CompanyViewModel>();
            services.AddTransient<ContactViewModel>();
            services.AddTransient<PlaceViewModel>();
            services.AddTransient<CreateProjectViewModel>();
            services.AddTransient<EditProjectViewModel>();
            services.AddTransient<AddUserViewModel>();
            services.AddTransient<MasterPlanMappingViewModel>();
            services.AddTransient<EmailContextViewModel>();
            services.AddTransient<WorkflowDashboardViewModel>();
            services.AddTransient<WorkflowInstanceViewModel>();
            services.AddTransient<WorkflowDesignerViewModel>();

            return services.BuildServiceProvider();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                // Lifecycle marker — emitted at Warning so it lands in the
                // central log share even with the default per-app levels.
                // Mirrors the AccService "service-up" / SyncEngine "starting"
                // lines, so the shared log shows every app open/close.
                Log.Warning(
                    "SiNetProjectManagerV2 opened — version {Version}, machine {Machine}, user {User}, session {Session}.",
                    Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?",
                    Environment.MachineName,
                    Environment.UserName,
                    SessionId);

                Log.Information("[STARTUP] ═══ Application startup initiated ═══");

                // Set explicit shutdown mode during startup to prevent premature shutdown
                // when setup dialogs (credential vault, database connection) are shown.
                // This will be changed to OnMainWindowClose once MainWindow is established.
                ShutdownMode = ShutdownMode.OnExplicitShutdown;

                Log.Information("[STARTUP] Step 0: Configuring global handlers...");
                ConfigureGlobalHandlers();
                EnableBindingTracing();

                Log.Information("[STARTUP] Step 0: Loading app settings...");
                AppSettings = SettingsManager.LoadSettings();
                ApplySettings();

                // ── Step 1: Credential Vault ──────────────────────────────
                Log.Information("[STARTUP] Step 1: Setting up credential vault...");
                SetupCredentialVault();

                // ── Step 2: Database Connection Gate ──────────────────────
                Log.Information("[STARTUP] Step 2: Ensuring database connection...");
                if (!EnsureDatabaseConnection())
                {
                    Log.Warning("[STARTUP] Database connection failed. Shutting down.");
                    Shutdown();
                    return;
                }

                // ── Step 3: Logging ──────────────────────────────────────
                Log.Information("[STARTUP] Step 3: Configuring logging...");
                ConfigureLoggingAndSettings();

                // ── Step 4: Dependency Injection ──────────────────────────
                Log.Information("[STARTUP] Step 4: Configuring DI services...");
                ServiceProvider = ConfigureServices();
                WireLegacyLocators();

                // Initialize ServiceLocator for SiNetSQL library access
                SiNetSQL.Services.ServiceLocator.Initialize(ServiceProvider);

                // ── Step 4b: Load Management Settings from DB ────────────
                Log.Information("[STARTUP] Step 4b: Loading management settings from DB...");
                LoadManagementSettingsFromDb();

                // ── Step 5: Background Services (non-blocking) ───────────
                Log.Information("[STARTUP] Step 5: Scheduling background services...");
                SchedulePdfRendererInit();
                StartAccUserBootstrap();

                // ── Step 6: Database Validation & Seeding ─────────────────
                Log.Information("[STARTUP] Step 6: Validating database schema...");
                if (!ValidateDatabaseSchema(out var defaultProjectError))
                {
                    Log.Warning("[STARTUP] Database schema validation failed. Shutting down.");
                    Shutdown();
                    return;
                }

                // ── Step 7: User Authorization ────────────────────────────
                Log.Information("[STARTUP] Step 7: Authorizing current user...");
                if (!AuthorizeCurrentUser())
                {
                    Log.Warning("[STARTUP] User authorization failed. Shutting down.");
                    Shutdown();
                    return;
                }

                // ── Step 8: Post-Auth Initialization ──────────────────────
                Log.Information("[STARTUP] Step 8: Post-auth initialization...");
                if (defaultProjectError is not null && !HandleDefaultProjectFailure())
                {
                    Log.Warning("[STARTUP] Default project handling failed. Shutting down.");
                    Shutdown();
                    return;
                }

                Log.Information("[STARTUP] Step 8: Initializing status colors...");
                InitializeStatusColors();

                // ── Step 9: Launch ────────────────────────────────────────
                Log.Information("[STARTUP] Step 9: Enforcing single instance...");
                if (!EnforceSingleInstance())
                {
                    Log.Information("[STARTUP] Another instance is already running. Shutting down.");
                    Shutdown();
                    return;
                }

                Log.Information("[STARTUP] Step 10: Launching main window...");
                base.OnStartup(e);
                ShowSplashThenMainWindow();

                // ── Step 10: Post-Launch Admin Alerts ─────────────────────
                // Show sync failures AFTER MainWindow is established
                // This ensures the alert is a non-blocking floating notification
                Dispatcher.BeginInvoke(ShowSyncFailureAlertIfAdmin, DispatcherPriority.Background);

                Log.Information("[STARTUP] ═══ Application startup completed successfully ═══");
            }
            catch (Exception ex)
            {
                // CRITICAL: Unhandled exception during startup
                // This is the LAST line of defense before silent crash
                var errorId = Guid.NewGuid().ToString("N");

                try
                {
                    Log.Fatal(ex, "[STARTUP FATAL] Application failed to start. ErrorId={ErrorId}", errorId);
                    Log.CloseAndFlush();
                }
                catch { }

                try
                {
                    var logPath = GetLogDirectory();
                    var msg = $"האפליקציה נכשלה בהפעלה:\n\n{ex.Message}\n\n" +
                              $"קוד שגיאה: {errorId}\n" +
                              $"לוגים: {logPath}\n\n" +
                              $"פרטים טכניים:\n{ex.GetType().Name}";

                    MessageBox.Show(msg, "שגיאת הפעלה קריטית", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch { }

                Shutdown();
            }
        }

        #region Startup Pipeline Steps

        /// <summary>
        /// Wires the credential bridge and auto-imports provisioning file if available.
        /// Non-fatal — continues even if user skips vault setup.
        /// </summary>
        private static void SetupCredentialVault()
        {
            CredentialProvider.GetSecret = CredentialVaultService.GetSecret;

            // Auto-detect encrypted provisioning file next to the exe
            var provisioningPath = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
                "SiNet.secrets");

            if (!CredentialVaultService.IsVaultConfigured()
                && SecretProvisioningService.IsProvisioningFile(provisioningPath))
            {
                // Found a provisioning package — prompt for password and import
                var pwDialog = new WPF_Window.ProvisioningPasswordDialog
                {
                    RequireConfirmation = false,
                    Title = "נמצא קובץ הגדרות — הזן סיסמה לייבוא"
                };

                if (pwDialog.ShowDialog() == true)
                {
                    try
                    {
                        var imported = SecretProvisioningService.ImportFromFile(
                            provisioningPath, pwDialog.EnteredPassword);
                        Log.Information("Provisioning import: {Count} secrets imported from file.", imported);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Provisioning import failed.");
                        MessageBox.Show(
                            $"ייבוא חבילת ההגדרות נכשל:\n{ex.Message}",
                            "שגיאה בייבוא", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }

            // Check if vault is provisioned — if not, open setup dialog
            if (!CredentialVaultService.IsVaultConfigured())
            {
                var setupWindow = new WPF_Window.SecretSetupWindow();
                if (setupWindow.ShowDialog() != true)
                {
                    // User cancelled setup — warn but continue (fallback to config files)
                    Log.Warning("Credential vault setup was skipped. Falling back to configuration files.");
                }
            }
        }

        /// <summary>
        /// Verifies database connectivity in a retry loop, opening SecretSetupWindow if needed.
        /// </summary>
        /// <returns>True if connected successfully, false if user cancelled (app should shutdown).</returns>
        private static bool EnsureDatabaseConnection()
        {
            while (true)
            {
                var siNetConnStr = AppConfiguration.GetConnectionString("SiNetDatabase");

                if (string.IsNullOrWhiteSpace(siNetConnStr))
                {
                    Log.Warning("SiNetDatabase connection string not found in vault or appsettings.");
                }
                else
                {
                    // Connection string exists — verify actual database connectivity
                    try
                    {
                        using var testConn = new Microsoft.Data.SqlClient.SqlConnection(siNetConnStr);
                        testConn.Open();
                        return true; // ✔ Connected successfully — proceed with startup
                    }
                    catch (Exception connEx)
                    {
                        Log.Warning(connEx, "Database connection test failed for SiNetDatabase.");
                        MessageBox.Show(
                            $"לא ניתן להתחבר למסד הנתונים.\n\n" +
                            $"שגיאה: {connEx.Message}\n\n" +
                            "נא לעדכן את הגדרות החיבור.",
                            "שגיאת חיבור למסד נתונים",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }

                // Either missing or unreachable — open SecretSetupWindow to fix
                var connSetupWindow = new WPF_Window.SecretSetupWindow();
                if (connSetupWindow.ShowDialog() != true)
                {
                    Log.Fatal("SiNetDatabase connection is missing or unreachable and user cancelled setup. Shutting down.");
                    MessageBox.Show(
                        "לא ניתן להפעיל את האפליקציה ללא חיבור למסד הנתונים.",
                        "חסר הגדרת חיבור",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return false;
                }
                // Loop back — re-read from vault and re-test connection
            }
        }

        /// <summary>
        /// Configures AppLogger with user settings and wires ReportLogger.
        /// </summary>
        private static void ConfigureLoggingAndSettings()
        {
            // Initialize AppLogger with user settings (default: OFF)
            AppLogger.Configure(
                AppSettings!.LoggingEnabled,
                string.IsNullOrEmpty(AppSettings.LogDirectory) ? null : AppSettings.LogDirectory);

            // Wire ReportLogger to use AppLogger (for GoogleConnector)
            ReportLogger.Instance = AppLoggerReportAdapter.Instance;
        }

        /// <summary>
        /// Loads admin-level settings from the DB (SystemSettings table) and configures DefaultProjectService.
        /// Must be called after DI is configured (SystemSettingsService is registered as singleton).
        /// </summary>
        private static void LoadManagementSettingsFromDb()
        {
            var settingsService = ServiceProvider.GetRequiredService<SystemSettingsService>();
            var title = Task.Run(() => settingsService.GetOrDefaultAsync(
                SystemSettingKeys.DefaultProjectTitle, string.Empty).AsTask()).GetAwaiter().GetResult();

            SiNetSQL.Services.DefaultProjectService.ConfiguredTitle =
                string.IsNullOrWhiteSpace(title)
                    ? SiNetSQL.Services.DefaultProjectService.FallbackDefaultProjectTitle
                    : title;
        }

        /// <summary>
        /// Wires DI-resolved services into legacy static locators for backward compatibility.
        /// </summary>
        private static void WireLegacyLocators()
        {
            var dialogService = ServiceProvider.GetRequiredService<SiNetSQL.MVVM.IDialogService>();
            DialogServiceLocator.Instance = dialogService;
            SiNetSQL.MVVM.DialogServiceLocator.Instance = dialogService;

            // ValidationRules can't use DI — wire via static locator
            ProjectNameValidationServiceLocator.DbContextFactory =
                ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQL.Data.SiNetSQLDbContext>>();
        }

        /// <summary>
        /// Schedules WebView2 PDF renderer initialization on the UI dispatcher.
        /// Non-fatal — PDF generation is optional.
        /// </summary>
        private void SchedulePdfRendererInit()
        {
            try
            {
                var pdfRenderer = ServiceProvider.GetRequiredService<WebView2PdfRenderer>();

                // Initialize WebView2 asynchronously but don't block the UI during init
                Dispatcher.BeginInvoke(async () =>
                {
                    await pdfRenderer.InitializeAsync();

                    // Wire up the PDF renderer to the EmailIngestionServiceFactory
                    var factory = ServiceProvider.GetRequiredService<IEmailIngestionServiceFactory>();
                    factory.SetPdfRenderer(pdfRenderer);
                }, DispatcherPriority.Background);
            }
            catch (Exception pdfEx)
            {
                // Non-fatal: PDF generation is optional
                Log.Warning(pdfEx, "PDF renderer initialization failed. Email body PDFs will not be generated.");
            }
        }

        /// <summary>
        /// Starts ACC user provisioning in background. Cancelled via <see cref="_appShutdownCts"/>.
        /// </summary>
        private static void StartAccUserBootstrap()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var bootstrapService = ServiceProvider.GetRequiredService<IAccUserBootstrapService>();
                    await bootstrapService.ProvisionUsersAsync(_appShutdownCts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Expected on shutdown — don't log as error
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "ACC User Bootstrap failed unexpectedly.");
                }
            }, _appShutdownCts.Token);
        }

        /// <summary>
        /// Validates database schema, seeds lookup data, and checks the default project.
        /// <para>
        /// <b>Migration strategy:</b> Schema changes are applied via EF Migration Bundle
        /// (<c>scripts\build_efbundle.ps1</c> / <c>scripts\run_efbundle.ps1</c>).
        /// Automatic <c>Database.Migrate()</c> is intentionally DISABLED for production safety.
        /// </para>
        /// </summary>
        /// <param name="defaultProjectError">
        /// Set if the default project check fails — deferred to post-auth for role-based handling.
        /// </param>
        /// <returns>True if schema is valid and DB accessible, false if app should shutdown.</returns>
        private static bool ValidateDatabaseSchema(out Exception? defaultProjectError)
        {
            defaultProjectError = null;
            try
            {
                var dbContextFactory = ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQL.Data.SiNetSQLDbContext>>();

                // Schema validation (fresh context, disposed at end of block)
                using (var context = dbContextFactory.CreateDbContext())
                {
                    var schemaValidator = new SiNetSQL.Services.DatabaseSchemaValidator(context);

                    // First check if we can connect to the database
                    if (!schemaValidator.CanConnect())
                    {
                        Log.Fatal("Cannot connect to database.");
                        MessageBox.Show(
                            "לא ניתן להתחבר למסד הנתונים.\nנא לוודא שהשרת זמין.",
                            "שגיאת חיבור",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        return false;
                    }

                    // Check if Task Management schema exists
                    if (!schemaValidator.IsTaskManagementSchemaPresent())
                    {
                        var missingTables = schemaValidator.GetMissingTables();
                        var tableList = string.Join(", ", missingTables);

                        Log.Fatal("Database schema is outdated. Missing tables: {Tables}", tableList);
                        MessageBox.Show(
                            $"מבנה מסד הנתונים אינו עדכני.\n\n" +
                            $"טבלאות חסרות: {tableList}\n\n" +
                            $"יש להריץ את efbundle.exe לעדכון המבנה.\n" +
                            $"פרטים נוספים: scripts\\README.md",
                            "נדרש עדכון מסד נתונים",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return false;
                    }
                }

                // Seed Task Management data (separate context = separate transaction)
                using (var context = dbContextFactory.CreateDbContext())
                {
                    try
                    {
                        var seedService = new SiNetSQL.Services.TaskManagementSeedService(context);
                        seedService.EnsureStaticLookupData();
                    }
                    catch (Exception seedEx)
                    {
                        // Log seeding error but don't crash — seed data is not critical
                        Log.Error(seedEx, "Task Management seed data initialization failed.");
                    }
                }

                // Seed Workflow definitions in background (idempotent — skips existing definitions)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var workflowSeedService = ServiceProvider.GetRequiredService<SiNetSQL.Services.Workflow.WorkflowSeedService>();
                        await workflowSeedService.SeedAllAsync(_appShutdownCts.Token);

                        var behaviorSeedService = ServiceProvider.GetRequiredService<SiNetSQL.Services.TaskLifecycle.TaskBehaviorSeedService>();
                        await behaviorSeedService.SeedAllAsync(_appShutdownCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected on shutdown — don't log as error
                    }
                    catch (Exception wfSeedEx)
                    {
                        // Non-fatal: workflow definitions can be created later via admin UI
                        Log.Error(wfSeedEx, "Workflow seed data initialization failed.");
                    }
                }, _appShutdownCts.Token);

                // Ensure Default Project exists (required for Email Inbox FK integrity).
                // Auto-created ONLY on fresh install (zero projects in DB).
                // Failure is deferred to after user authorization for role-based handling.
                using (var context = dbContextFactory.CreateDbContext())
                {
                    try
                    {
                        var defaultProjectService = new SiNetSQL.Services.DefaultProjectService(context);
                        defaultProjectService.EnsureDefaultProjectExists();
                    }
                    catch (Exception defaultProjectEx)
                    {
                        Log.Error(defaultProjectEx, "Default project lookup failed for title '{Title}'.",
                            SiNetSQL.Services.DefaultProjectService.ConfiguredTitle);
                        defaultProjectError = defaultProjectEx;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Database initialization failed.");
                MessageBox.Show(
                    "אירעה שגיאה באתחול מסד הנתונים.\nנא לפנות לתמיכה.",
                    "שגיאה קריטית",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// Authenticates the current Windows user against the SIUser table.
        /// </summary>
        /// <returns>True if authorized, false if access denied (app should shutdown).</returns>
        private static bool AuthorizeCurrentUser()
        {
            try
            {
                var dbContextFactory = ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQL.Data.SiNetSQLDbContext>>();

                using var context = dbContextFactory.CreateDbContext();
                var userContext = SiNetSQL.Services.CurrentUserContext.Instance;

                if (!userContext.Initialize(context))
                {
                    // User not found in SIUser table — deny access
                    var windowsLogin = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
                    Log.Fatal("User authorization failed. WindowsLogin={Login} not found in SIUser table.", windowsLogin);

                    MessageBox.Show(
                        $"המשתמש '{windowsLogin}' אינו מורשה להשתמש באפליקציה.\n\n" +
                        $"נא לפנות למנהל המערכת להוספת הרשאות.",
                        "גישה נדחתה",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return false;
                }

                // Log successful authorization
                Log.Information(
                    "User authorized. UserId={UserId}, LoginName={LoginName}, IsDomainGroup={IsDomainGroup}, AccessLevel={AccessLevel}",
                    userContext.CurrentUserId,
                    userContext.DatabaseLoginName,
                    userContext.IsFullAccess,
                    userContext.IsFullAccess ? "Full" : "Limited");

                return true;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "User authorization check failed.");
                MessageBox.Show(
                    "אירעה שגיאה בבדיקת הרשאות המשתמש.\nנא לפנות לתמיכה.",
                    "שגיאה קריטית",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// Initializes the StatusColorService cache for the current user.
        /// Non-fatal — colors degrade gracefully to gray fallback.
        /// </summary>
        private static void InitializeStatusColors()
        {
            try
            {
                var colorService = ServiceProvider.GetRequiredService<SiNetSQL.Services.StatusColorService>();
                SiNetSQL.MVVM.StatusColorServiceLocator.Instance = colorService;

                var currentUserId = SiNetSQL.Services.CurrentUserContext.Instance.CurrentUserId;
                if (currentUserId.HasValue)
                {
                    colorService.LoadColors(currentUserId.Value);
                    AppLogger.Debug($"[StatusColor] Cache loaded for UserId={currentUserId.Value}");
                }
            }
            catch (Exception colorEx)
            {
                // Non-fatal: colors degrade gracefully to gray fallback
                Log.Warning(colorEx, "StatusColorService initialization failed. Colors will use fallback.");
            }
        }

        /// <summary>
        /// Enforces single-instance mode if configured in user settings.
        /// </summary>
        /// <returns>True to continue, false if another instance is running (app should shutdown).</returns>
        private static bool EnforceSingleInstance()
        {
            bool singleInstance = !(AppSettings?.AllowMultipleInstances ?? true);
            if (!singleInstance)
                return true;

            _mutex = new Mutex(true, "Global\\SiNetProjectManagerV2_UniqueApp", out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show("האפליקציה כבר פתוחה.", "שגיאה", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Shows the splash screen then transitions to the main window after a brief delay.
        /// Once MainWindow is established, switches ShutdownMode to OnMainWindowClose.
        /// </summary>
        private void ShowSplashThenMainWindow()
        {
            var splash = new SplashWindow();
            splash.Show();

            Task.Delay(2000).ContinueWith(_ =>
            {
                splash.Dispatcher.Invoke(() =>
                {
                    var main = new MainWindow();
                    Current.MainWindow = main;

                    // CRITICAL: Switch shutdown mode to normal behavior now that MainWindow exists.
                    // This allows the app to close properly when MainWindow is closed.
                    // Prior to this point, ShutdownMode was OnExplicitShutdown to prevent
                    // premature shutdown during setup dialogs (credential vault, database connection).
                    Current.ShutdownMode = ShutdownMode.OnMainWindowClose;

                    main.Show();
                    splash.Close();
                });
            });
        }

        #endregion

        /// <summary>
        /// Handles default project lookup failure based on user role.
        /// Admin: opens Management Settings to fix the title, then retries.
        /// Non-admin: shows a blocking error message.
        /// </summary>
        /// <returns>True if recovered successfully (admin fixed settings), false to shutdown.</returns>
        private bool HandleDefaultProjectFailure()
        {
            var configuredTitle = SiNetSQL.Services.DefaultProjectService.ConfiguredTitle;
            var userContext = SiNetSQL.Services.CurrentUserContext.Instance;

            if (userContext.IsFullAccess)
            {
                // Admin: open Management Settings with notification
                var notification =
                    $"שם הפרויקט '{configuredTitle}' לא נמצא במסד הנתונים.\n" +
                    "נא לוודא ולהדביק את שם הפרויקט המדויק כאן.";

                var settingsWindow = new WPF_Window.ManagementSettingsWindow(notification);
                var saved = settingsWindow.ShowDialog() == true;

                if (!saved)
                {
                    Log.Fatal("Admin cancelled default project configuration. Shutting down.");
                    return false;
                }

                // Reload settings from DB and retry with the updated title
                var settingsService = ServiceProvider.GetRequiredService<SystemSettingsService>();
                settingsService.InvalidateCache();
                var newTitle = Task.Run(() => settingsService.GetOrDefaultAsync(
                    SystemSettingKeys.DefaultProjectTitle, string.Empty).AsTask()).GetAwaiter().GetResult();
                if (string.IsNullOrWhiteSpace(newTitle))
                    newTitle = SiNetSQL.Services.DefaultProjectService.FallbackDefaultProjectTitle;

                SiNetSQL.Services.DefaultProjectService.ConfiguredTitle = newTitle;
                SiNetSQL.Services.DefaultProjectService.ResetCache();

                try
                {
                    var dbContextFactory = ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQL.Data.SiNetSQLDbContext>>();
                    using var context = dbContextFactory.CreateDbContext();
                    var retryService = new SiNetSQL.Services.DefaultProjectService(context);
                    retryService.EnsureDefaultProjectExists();
                    return true;
                }
                catch (Exception retryEx)
                {
                    Log.Fatal(retryEx, "Default project retry failed after admin settings update. Title='{Title}'.", newTitle);
                    MessageBox.Show(
                        $"הפרויקט '{newTitle}' עדיין לא נמצא במסד הנתונים.\n\n" +
                        $"פרטים: {retryEx.Message}\n\n" +
                        "האפליקציה תיסגר.",
                        "שגיאת אתחול",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return false;
                }
            }
            else
            {
                // Non-admin: blocking error
                Log.Fatal("Default project '{Title}' not found. User is not admin — cannot fix.", configuredTitle);
                MessageBox.Show(
                    "שגיאת תצורה: פרויקט ברירת המחדל לא נמצא במסד הנתונים.\n\n" +
                    "נא לפנות למנהל המערכת לפתרון הבעיה.",
                    "שגיאת תצורה",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }
        }

        private void ConfigureGlobalHandlers()
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            HandleException(e.Exception, "UI Thread", isFatal: false);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception ?? new Exception("Unknown domain unhandled exception");
            HandleException(ex, "AppDomain", isFatal: e.IsTerminating);
            if (e.IsTerminating)
            {
                Log.CloseAndFlush();
            }
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            HandleException(e.Exception, "TaskScheduler", isFatal: false);
            e.SetObserved();
        }

        private volatile bool _isShowingError; // re-entrancy guard for layout exceptions

        private void HandleException(Exception ex, string source, bool isFatal)
        {
            var errorId = Guid.NewGuid().ToString("N");
            if (isFatal)
            {
                Log.Fatal(ex, "[{Source}] Unhandled fatal exception {ErrorId}", source, errorId);
            }
            else
            {
                Log.Error(ex, "[{Source}] Unhandled exception {ErrorId}", source, errorId);
            }
            try
            {
                if (!_isShowingError)
                {
                    _isShowingError = true;
                    Current?.Dispatcher?.BeginInvoke(() =>
                    {
                        try
                        {
                            var owner = Current.MainWindow;
                            var msg = $"אירעה שגיאה בלתי צפויה. (Code: {errorId})\nניתן להמשיך לעבוד אך ייתכנו בעיות.";
                            if (owner != null)
                                MessageBox.Show(owner, msg, "שגיאה", MessageBoxButton.OK, MessageBoxImage.Error);
                            else
                                MessageBox.Show(msg, "שגיאה", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        finally
                        {
                            _isShowingError = false;
                        }
                    });
                }
            }
            catch { }
            if (isFatal)
            {
                Log.CloseAndFlush();
            }
        }

        /// <summary>
        /// Admin-only: checks SiData.dbo.Sync_RunFailures for recent failures (last 7 days).
        /// If any exist, shows a non-modal floating popup listing them.
        /// Non-blocking — never crashes the app and doesn't interfere with application lifecycle.
        /// </summary>
        private void ShowSyncFailureAlertIfAdmin()
        {
            try
            {
                var userContext = SiNetSQL.Services.CurrentUserContext.Instance;
                if (!userContext.IsFullAccess)
                    return;

                var dbContextFactory = ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQL.Data.SiNetSQLDbContext>>();
                using var context = dbContextFactory.CreateDbContext();

                var cutoff = DateTime.UtcNow.AddDays(-7);
                var failures = context.SyncRunFailures
                    .Where(f => f.FailedAt >= cutoff)
                    .OrderByDescending(f => f.FailedAt)
                    .Take(50)
                    .Select(f => new Dialogs.SyncFailureDisplayItem
                    {
                        FailedAt = f.FailedAt,
                        ErrorStage = f.ErrorStage,
                        ErrorType = f.ErrorType,
                        ErrorMessage = f.ErrorMessage,
                        StackTrace = f.StackTrace
                    })
                    .ToList();

                if (failures.Count == 0)
                    return;

                // Show as a non-modal, floating window (doesn't block main window)
                var window = new Dialogs.SyncFailuresWindow(failures);
                window.Show(); // Changed from ShowDialog() to Show()
            }
            catch (Exception ex)
            {
                // Non-fatal: never crash the app over a diagnostic popup
                Log.Error(ex, "Failed to check Sync_RunFailures for admin alert.");
            }
        }

        private static void EnableBindingTracing()
        {
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error | SourceLevels.Critical;
            Trace.Listeners.Add(new SerilogTraceListener());
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Lifecycle marker — Warning level so the central log share records
            // every app close alongside the matching "opened" line in OnStartup.
            try
            {
                Log.Warning(
                    "SiNetProjectManagerV2 closing — exit code {ExitCode}, session {Session}.",
                    e.ApplicationExitCode,
                    SessionId);
            }
            catch { /* never block shutdown on a log write */ }

            // Cancel background tasks (ACC User Bootstrap, etc.)
            try
            {
                _appShutdownCts.Cancel();
                _appShutdownCts.Dispose();
            }
            catch { /* Best effort cleanup */ }

            base.OnExit(e);
            Log.CloseAndFlush();
        }

        private static string GetLogDirectory()
        {
            try
            {
                var entry = Assembly.GetEntryAssembly();
                var company = entry?.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;
                if (string.IsNullOrWhiteSpace(company)) company = "SiNet";
                var product = entry?.GetName().Name ?? "SiNetProjectManagerV2";
                var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(root, company, product, "Logs");
            }
            catch
            {
                var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(root, "SiNet", "SiNetProjectManagerV2", "Logs");
            }
        }
    }

    /// <summary>
    /// Lightweight Serilog enricher that adds <c>ThreadId</c> to every log event.
    /// Replaces the need for the <c>Serilog.Enrichers.Thread</c> NuGet package.
    /// </summary>
    internal sealed class ThreadIdEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty("ThreadId", Environment.CurrentManagedThreadId));
        }
    }

    internal sealed class SerilogTraceListener : TraceListener
    {
        public override void Write(string? message) { }
        public override void WriteLine(string? message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            // Do not emit non-error logs; warnings are acceptable if truly important
            // Here we skip forwarding trace to keep logs concise
        }
    }

#if DEBUG
    /// <summary>
    /// Custom Serilog sink that writes ALL log events to the VS Output window via <see cref="System.Diagnostics.Debug"/>.
    /// Uses the same output template as the file sink for consistent formatting.
    /// Active only in DEBUG builds — stripped from Release.
    /// </summary>
    internal sealed class DebugOutputSink : Serilog.Core.ILogEventSink
    {
        private readonly Serilog.Formatting.ITextFormatter _formatter;

        public DebugOutputSink(string outputTemplate)
        {
            _formatter = new Serilog.Formatting.Display.MessageTemplateTextFormatter(outputTemplate);
        }

        public void Emit(LogEvent logEvent)
        {
            using var writer = new StringWriter();
            _formatter.Format(logEvent, writer);
            System.Diagnostics.Debug.Write(writer.ToString());
        }
    }
#endif
}
