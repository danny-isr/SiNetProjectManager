using SiNet.Infrastructure.Autodesk;
using SiNet.Application.Common;
using SiNetProjectManagerV2.Services;
using SiNetProjectManagerV2.Services.Composition;
using SiNetProjectManagerV2.WPF;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
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
using SiNetSQL.Services.EmailOutbound;
using SiNet.Infrastructure.Logging;
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
            try
            {
                Directory.CreateDirectory(_logDir);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Failed to create log directory: {ex.Message}");
            }

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

            // Wire TokenProvider diagnostics (Gap 18B/18C) to AppLogger so its [TokenProvider]
            // lines reach the central log on the client side. Static delegates are process-wide
            // and apply to every `new TokenProvider(...)` instance (DI + direct construction).
            MyOffice.AutodeskConnector.TokenProvider.LogInfo = msg => AppLogger.Info(msg);
            MyOffice.AutodeskConnector.TokenProvider.LogWarn = msg => AppLogger.Warn(msg);
            MyOffice.AutodeskConnector.TokenProvider.LogError = msg => AppLogger.Error(msg);
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

            // Composition adoption (Phase 2 / SQL diagnostics gate): the DbContextFactory registration
            // is delegated to the modular SiNet.Infrastructure.Sql module. The connection string is
            // still sourced from the host's vault (Windows Credential Manager) and passed in — the
            // source is unchanged. AddSiNetSql applies the same UseSqlServer + UseCompatibilityLevel(120)
            // as before; the #if DEBUG opt-in below reproduces the previous inline EnableSensitiveDataLogging()
            // + EnableDetailedErrors() exactly. In Release the flag stays false, so SQL/runtime behavior
            // is identical. SiNetSQL.Data.SiNetSQLDbContext is the single shared context type, so consumers
            // resolve the same IDbContextFactory<SiNetSQLDbContext>.
            SiNet.Infrastructure.Sql.SqlServiceCollectionExtensions.AddSiNetSql(services, connectionString, options =>
            {
#if DEBUG
                options.EnableEfDebugDiagnostics = true;
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
            services.AddSingleton<SiNetSQL.Services.EmailIngestion.IEmailPdfRenderer>(sp =>
                sp.GetRequiredService<WebView2PdfRenderer>());
            services.AddSingleton<SiNet.Application.Email.Acc.IGoogleIngestSessionEnsurer, GoogleServiceSessionEnsurer>();
            services.AddSingleton<SiNet.Application.Email.Acc.IEmailAccClosePrompt, EmailAccClosePrompt>();
            services.AddTransient<SiNet.Application.Email.Acc.IEmailAccIngestionExecutor, LegacyEmailAccIngestionExecutor>();
            services.AddTransient<SiNet.Application.Email.Acc.IEmailAccRecoveryExecutor, LegacyEmailAccRecoveryExecutor>();
            // Native MoveToProject executor (Phase 3b) — replaces the legacy process-action bridge.
            // Files tagged inbox attachments via the native IProjectFileFilingService and stamps ACC
            // Move/Lock metadata; task completion routes through the native ITaskCompletionService.
            services.AddTransient<SiNet.Application.Email.Acc.IEmailMoveToProjectExecutor,
                SiNet.Infrastructure.Sql.Services.Email.Acc.NativeEmailMoveToProjectExecutor>();
            services.AddTransient<SiNet.Application.Email.Acc.IEmailExternalDownloadExecutor, LegacyEmailExternalDownloadExecutor>();
            services.AddSingleton<SiNet.Application.Email.Acc.IEmailExternalDownloadBrowserHost, V2EmailExternalDownloadBrowserHost>();
            services.AddTransient<IEmailRelevanceService, EmailRelevanceService>();

            // PDF Renderer: Singleton (reused for all PDF generations)
            services.AddSingleton<WebView2PdfRenderer>();
            // Transient: each email surface (shell / window / work-item) gets its own WebView2.
            // Singleton caused reparent of a single WebView2 across hosts → blank body panes.
            services.AddTransient<SiNet.Application.Email.Detail.IEmailBodyRenderer, SiNetProjectManagerV2.Services.Email.WebView2EmailBodyRenderer>();
            // Embedded ACC document viewer for the ProjectWork surface (host-seam; WebView2 lives here).
            services.AddSingleton<SiNet.Application.ProjectWork.IAccViewerHost, SiNetProjectManagerV2.Services.ProjectWork.WebView2AccViewerHost>();
            // After native task completion, refresh floating/task-panel lists via ActiveProjectContext.
            services.AddSingleton<SiNet.Application.Tasks.ITaskListChangeNotifier,
                SiNetProjectManagerV2.Services.ActiveProjectTaskListChangeNotifier>();
            services.AddSingleton<SiNet.App.Wpf.WorkSurfaces.ITaskFamilyWindowGate,
                SiNetProjectManagerV2.Services.MainWindowTaskFamilyWindowGate>();
            services.AddTransient<SiNet.Application.Email.Detail.IEmailAttachmentProjectFilePickerHost,
                SiNetProjectManagerV2.Services.Email.EmailAttachmentProjectFilePickerHost>();
            services.AddTransient<SiNet.Application.Email.Detail.IEmailFilingProjectPickerHost,
                SiNetProjectManagerV2.Services.Email.EmailFilingProjectPickerHost>();
            services.AddTransient<SiNet.Application.Email.Detail.IEmailAlternativeNamePromptHost,
                SiNetProjectManagerV2.Services.Email.EmailAlternativeNamePromptHost>();

            // DISABLED LEGACY — Gap 8 (DocumentationVsImplementationGaps-2026-05-26.md).
            // GmailVisibleAttachmentsDomExtractor is commented out (its source is
            // parked behind `#if false`). Candidate for physical deletion in a
            // future approved cleanup round. Do not re-enable without explicit
            // approval — Gmail DOM is not a source of truth for attachments.
            // services.AddSingleton<GmailVisibleAttachmentsDomExtractor>();

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
            services.AddSingleton<GoogleService>(sp =>
            {
                var throttle = sp.GetRequiredService<IGmailThrottleService>();
                var gs = new GoogleService(throttle)
                {
                    // Single source of truth: same token store + application name used
                    // by GoogleAuthService above and read by GoogleHealthCheck.
                    TokenStorePath = AppConfiguration.GoogleTokenStorePath,
                    ApplicationName = AppConfiguration.GoogleApplicationName,
                    ClientSecretsPath = AppConfiguration.GetGoogleClientSecretsPath(),
                };
                AppLogger.Info(
                    $"[Health][google] GoogleService DI-singleton built. " +
                    $"instance#{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(gs)} " +
                    $"TokenStorePath='{gs.TokenStorePath}' AppName='{gs.ApplicationName}'");
                return gs;
            });
            // Strangler seam: lets the new clean-architecture stack read the mailbox through the
            // same authenticated GoogleService singleton without depending on the legacy connector.
            services.AddSingleton<SiNet.LegacyBridge.Email.ILegacyEmailSource,
                Services.GoogleServiceLegacyEmailSource>();

            // Strangler seam: lets the new Inspection screen read inspection series through the
            // legacy IInspectionReportService. Transient to match the report service lifetime; the
            // LegacyBridge adapter degrades to an empty list when this seam is absent (new app host).
            services.AddTransient<SiNet.LegacyBridge.Inspection.ILegacyInspectionSource,
                Services.ReportServiceLegacyInspectionSource>();

            // Process backbone (Workflow reads + native Task/Action ports in Infrastructure.Sql).
            // Replaces the temporary ILegacyTaskNavigationSource / ILegacyTaskCompletionSource seams
            // for New System Work Surfaces. Legacy TaskNavigationResolver / TaskCompletionCoordinator
            // remain registered below for legacy UI only.
            SiNet.Infrastructure.Sql.ProcessBackboneServiceCollectionExtensions.AddSiNetProcessBackbone(services);

            // Current-user port: binds the new clean ICurrentUserContext to the legacy authenticated
            // CurrentUserContext singleton so feature screens (e.g. the Inspection Work Surface) can
            // record the acting user without inventing an id. Singleton to match the underlying
            // CurrentUserContext.Instance lifetime; the new SiNet.App.Wpf preview harness leaves this
            // unbound, in which case the shell falls back to an explicit dev input. Read-only adapter:
            // it exposes only the user id and makes no authorization decisions.
            services.AddSiNetIdentityLegacyAdapters();

            // ITaskCompletionMetadataResolver is registered by AddSiNetProcessBackbone()
            // (SqlTaskCompletionMetadataResolver in Infrastructure.Sql).

            // Inspection migration (Phase 5): register the clean Application port adapter and the new
            // Inspection shell graph so the additive "Inspection (Preview)" developer entry point can
            // resolve InspectionShellView from this host's DI. Because ILegacyInspectionSource is bound
            // above, the LegacyInspectionWorkspace gets the live legacy seam injected here (unlike the
            // SiNet.App.Wpf preview harness, which leaves it unbound and shows empty data). Transient so
            // each preview-window open gets a fresh, independent shell graph. This is preview-only and
            // does not replace or alter the legacy floating Inspection window.
            // IInspectionWorkspace -> LegacyInspectionWorkspace is registered via the bridge's own
            // public extension because LegacyInspectionWorkspace is internal to SiNet.LegacyBridge.
            // HostMode = V2Hybrid: LegacyBridge is opt-in for the strangler host only (not StandaloneNew).
            SiNet.LegacyBridge.LegacyBridgeServiceCollectionExtensions.AddSiNetLegacyBridge(services);
            services.AddTransient<SiNet.App.Wpf.Inspection.InspectionTreeViewModel>();
            services.AddTransient<SiNet.App.Wpf.Inspection.InspectionNotesViewModel>();
            services.AddTransient<SiNet.App.Wpf.Inspection.InspectionDrawingsViewModel>();
            services.AddTransient<SiNet.App.Wpf.Inspection.InspectionReviewedPlanViewModel>();
            services.AddTransient<SiNet.App.Wpf.Inspection.InspectionReportViewModel>();
            services.AddTransient<SiNet.App.Wpf.Inspection.InspectionShellViewModel>();
            services.AddTransient<SiNet.App.Wpf.Inspection.InspectionShellView>();

            services.AddSingleton<IOutboundMailService, GmailOutboundMailService>();

            // ── LEGACY WORKFLOW ENGINE — RETIRED FROM THE NEW SYSTEM HOST (Phase 6) ──────────────
            // The SiNetSQL workflow engine graph (WorkflowEngine / WorkflowTransitionEvaluator /
            // WorkflowActionExecutor / WorkflowStageTaskProvisioningService / WorkflowTaskOrchestrator /
            // WorkflowActionCompletedHandler / WorkflowValidationService) and the legacy dispatcher
            // factory (Func<IProcessActionDispatcher>) are NO LONGER registered here.
            //
            // The single New System workflow command path is the native NativeWorkflowCommandService
            // (registered by AddSiNetProcessBackbone() above); workflow-stage transitions run through
            // the native IProcessActionService, and TaskLifecycle / StartWorkflow use the native
            // IWorkflowCommandService. The legacy AddSiNetWorkflowCommands() (WorkflowCommandServiceAdapter)
            // is likewise intentionally NOT registered — one engine, native only.
            //
            // The legacy IProcessActionDispatcher (AddProcessActions below) is still live for the legacy
            // email UI / Suggested Actions (ActionExecutor) / typed continuations / FileImportDialog, but
            // it no longer pulls the legacy engine: its ONLY engine-dependent handler,
            // StartSubWorkflowProcessActionHandler, is not registered (sub-workflow starts run through the
            // native engine). Verified: no live V2 consumer resolves any of the types listed above.
            // Composition adoption (Phase 1): the Workflow READ slice is delegated to the modular
            // SiNet.Infrastructure.Sql module. This registers WorkflowQueryService / IWorkflowQueryService
            // and ProjectWorkflowPolicyService / IProjectWorkflowPolicyService with identical Transient
            // lifetimes and port-forwarding, replacing the previous inline duplicates here. The
            // write/engine services above intentionally stay in the host (no port equivalent).
            SiNet.Infrastructure.Sql.WorkflowServiceCollectionExtensions.AddSiNetWorkflowReads(services);

            // Task Lifecycle Services: Transient (auto-create/auto-close tasks based on behavior definitions)
            services.AddTransient<SiNetSQL.Services.TaskLifecycle.TaskLifecycleService>();
            services.AddTransient<SiNetSQL.Services.TaskLifecycle.TaskBehaviorSeedService>();

            // Task Navigation Resolver: Transient (read-only resolver for opening tasks via the registry)
            services.AddTransient<SiNetSQL.Services.Tasks.TaskNavigationResolver>();

            // Task Completion Coordinator: Transient. Single decision point for UI-originated
            // completion events (e.g. ReviewMaterialFiled after a successful MoveToProject run).
            // UI components MUST go through this service rather than closing tasks directly.
            services.AddTransient<SiNetSQL.Services.Tasks.ITaskCompletionCoordinator,
                SiNetSQL.Services.Tasks.TaskCompletionCoordinator>();

            // Inspection-report task linking: Transient. Idempotently links a review task
            // (e.g. PerformProfessionalReview) to its concrete InspectionReport via the
            // existing TaskLink, so the floating inspection window can open the exact report
            // and complete the task through ITaskCompletionCoordinator. No new link table.
            services.AddTransient<SiNetSQL.Services.Tasks.InspectionReportTaskLinkService>();

            // Task Status Service: Transient. Owns regular (non-completion) status updates from UI.
            // Completing a task must instead go through ITaskCompletionCoordinator.
            services.AddTransient<SiNetSQL.Services.Tasks.TaskStatusService>();
            services.AddTransient<SiNetSQL.Services.IStatusMappingService, SiNetSQL.Services.StatusMappingService>();
            services.AddTransient<SiNetSQL.Services.Projects.IProjectService, SiNetSQL.Services.Projects.ProjectService>();

            // Task Workflow Resolver: Transient (single source of truth for "is this task workflow-bound?",
            // process context lookup, and guard predicates used by Task* commands).
            services.AddTransient<SiNetSQL.Services.Tasks.TaskWorkflowResolver>();

            // Smart Tasks: Transient (work-target completion + parent-task aggregation)
            services.AddTransient<SiNetSQL.Services.SmartTasks.SmartTaskService>();

            // Email Context Services: Transient (analyze email → business context → actions)
            services.AddTransient<SiNetSQL.Services.EmailContext.EmailContextAnalyzer>();
            services.AddTransient<SiNetSQL.Services.EmailContext.SuggestedActionsBuilder>();
            services.AddTransient<SiNetSQL.Services.EmailContext.ActionExecutor>();

            // Shared Email Business Services: Transient (single source of truth for
            // email filing/unfiling and Pending/Personal/Irrelevant status updates).
            // Both the WPF context-menu path (EmailManagementViewModel) and the
            // Suggested Actions path (ActionExecutor) call into these services so
            // both routes produce identical Gmail label / DB / lifecycle behavior.
            services.AddTransient<SiNetSQL.Services.Email.EmailFilingService>();
            services.AddTransient<SiNetSQL.Services.Email.EmailStatusService>();

            // EmailFilingService depends on TaskLifecycleService only at notify-time. Some legacy
            // IProcessActionDispatcher handlers file emails via EmailFilingService, which can close a
            // constructor cycle back through TaskLifecycleService. Resolving TaskLifecycleService lazily
            // via a factory breaks that cycle without changing any business behavior. (TaskLifecycleService
            // itself now uses the native IWorkflowCommandService — it no longer touches the legacy engine.)
            services.AddTransient<Func<SiNetSQL.Services.TaskLifecycle.TaskLifecycleService?>>(sp =>
                () => sp.GetService<SiNetSQL.Services.TaskLifecycle.TaskLifecycleService>());

            // Email attachment ProjectFile picker (shared FileTreePickerWindow UI, DB-driven).
            // Used by EmailManagementViewModel.TagAttachmentPickCommand. Does not affect
            // Inspection / ReviewedPlans pickers.
            services.AddTransient<SiNetSQL.Services.EmailIngestion.IAttachmentProjectFilePicker,
                SiNetProjectManagerV2.Services.EmailIngestion.AttachmentProjectFilePicker>();
            // Action lifecycle reporter: composite fan-out so ActionExecutor's call sites
            // stay unchanged. Inner reporters:
            //   • NoOpActionLifecycleReporter — preserves the safe baseline.
            //   • NativeWorkflowActionLifecycleReporter — bridges Completed events to the SINGLE
            //     native workflow engine via IWorkflowCommandService.CheckAndAdvanceOnActionCompletedAsync,
            //     but only when explicit safety conditions are met (WorkflowInstanceId present,
            //     ActionDefinition CanAdvanceWorkflow == true, etc.). It never infers a workflow
            //     from ProjectId / EmailMessageId. This replaces the legacy
            //     WorkflowActionLifecycleReporter → WorkflowActionCompletedHandler → legacy orchestrator
            //     path (plan Phase 3d — one engine, native only).
            services.AddSingleton<SiNetSQL.Domain.Actions.NoOpActionLifecycleReporter>(
                _ => SiNetSQL.Domain.Actions.NoOpActionLifecycleReporter.Instance);
            services.AddTransient<SiNetProjectManagerV2.Services.NativeWorkflowActionLifecycleReporter>();
            services.AddTransient<SiNetSQL.Domain.Actions.IActionLifecycleReporter>(sp =>
                new SiNetSQL.Domain.Actions.CompositeActionLifecycleReporter(
                    new SiNetSQL.Domain.Actions.IActionLifecycleReporter[]
                    {
                        sp.GetRequiredService<SiNetSQL.Domain.Actions.NoOpActionLifecycleReporter>(),
                        sp.GetRequiredService<SiNetProjectManagerV2.Services.NativeWorkflowActionLifecycleReporter>(),
                    }));

            // Process Actions runtime scaffolding: registers the legacy IProcessActionDispatcher used by
            // the legacy email UI / Suggested Actions (ActionExecutor) / typed continuations. The handler
            // registrations follow below. (The legacy WorkflowActionExecutor transition path has been
            // retired; workflow-stage transitions run through the native IProcessActionService.)
            SiNetSQL.Domain.Actions.ProcessActionsServiceCollectionExtensions.AddProcessActions(services);

            // Stage 2A pilot: register the first IProcessActionHandler — AssociateToExistingProject.
            // ActionExecutor routes this single ActionCode through the dispatcher when present,
            // and falls back to the legacy LinkToProjectAsync path otherwise.
            services.AddTransient<SiNetSQL.Domain.Actions.IProcessActionHandler,
                SiNetSQL.Domain.Actions.Handlers.LinkToProjectProcessActionHandler>();

            // Stage 2B: same filing pipeline for the backend-executed LinkToProject Suggested Action.
            services.AddTransient<SiNetSQL.Domain.Actions.IProcessActionHandler,
                SiNetSQL.Domain.Actions.Handlers.LinkToProjectBackendProcessActionHandler>();

            // Stage 2C: route SuggestedActionType.CreateTask through the dispatcher.
            // ActionExecutor falls back to the legacy CreateTaskDirectAsync path
            // when the handler is unavailable, so behavior parity is preserved.
            services.AddTransient<SiNetSQL.Domain.Actions.IProcessActionHandler,
                SiNetSQL.Domain.Actions.Handlers.CreateTaskProcessActionHandler>();

            // Stage 2D: route low-risk Suggested Actions through the dispatcher.
            // ActionExecutor.DispatchOrFallbackAsync wraps each call so the legacy
            // direct path runs when the handler is missing, returns NotSupported /
            // Deferred / NoOp, or throws.
            services.AddTransient<SiNetSQL.Domain.Actions.IProcessActionHandler,
                SiNetSQL.Domain.Actions.Handlers.FileOnlyProcessActionHandler>();
            services.AddTransient<SiNetSQL.Domain.Actions.IProcessActionHandler,
                SiNetSQL.Domain.Actions.Handlers.ApproveOrCloseProcessActionHandler>();
            services.AddTransient<SiNetSQL.Domain.Actions.IProcessActionHandler,
                SiNetSQL.Domain.Actions.Handlers.CloseOpinionProcessActionHandler>();

            // Strict dispatcher-only: SuggestedActionType.StartWorkflow runs through
            // StartWorkflowProcessActionHandler with NO legacy fallback. Missing handler /
            // dispatcher exception / unexpected status → ActionResult.Failed in the executor.
            services.AddTransient<SiNetSQL.Domain.Actions.IProcessActionHandler,
                SiNetSQL.Domain.Actions.Handlers.StartWorkflowProcessActionHandler>();

            // Legacy WorkflowTransitionActionType handlers. The legacy transition path
            // (WorkflowActionExecutor → IProcessActionDispatcher → these handlers) has been RETIRED;
            // workflow-stage transitions now run through the native IProcessActionService. These
            // registrations are retained only because the legacy dispatcher is still used by the legacy
            // email UI / Suggested Actions / continuations, and constructing them is inert (none touch
            // the removed legacy engine).
            //
            // EXCEPTION: StartSubWorkflowProcessActionHandler is intentionally NOT registered — it is the
            // sole legacy handler whose ctor requires the (now removed) WorkflowEngine +
            // WorkflowStageTaskProvisioningService. Registering it would re-introduce a transitive
            // dependency on the retired legacy engine. Sub-workflow starts run through the native engine.
            services.AddTransient<SiNetSQL.Domain.Actions.IProcessActionHandler,
                SiNetSQL.Domain.Actions.Handlers.CreateStageTasksProcessActionHandler>();
            services.AddTransient<SiNetSQL.Domain.Actions.IProcessActionHandler,
                SiNetSQL.Domain.Actions.Handlers.ClosePreviousStageTasksProcessActionHandler>();
            services.AddTransient<SiNetSQL.Domain.Actions.IProcessActionHandler,
                SiNetSQL.Domain.Actions.Handlers.SendNotificationProcessActionHandler>();
            services.AddTransient<SiNetSQL.Domain.Actions.IProcessActionHandler,
                SiNetSQL.Domain.Actions.Handlers.SetProjectStatusProcessActionHandler>();
            services.AddTransient<SiNetSQL.Domain.Actions.IProcessActionHandler,
                SiNetSQL.Domain.Actions.Handlers.RecordTaskResultProcessActionHandler>();
            services.AddTransient<SiNetSQL.Domain.Actions.IProcessActionHandler,
                SiNetSQL.Domain.Actions.Handlers.SetBillingPendingProcessActionHandler>();
            services.AddTransient<SiNetSQL.Domain.Actions.IProcessActionHandler,
                SiNetSQL.Domain.Actions.Handlers.CloseProjectProcessActionHandler>();

            // Phase 3: filing-related Suggested Actions routed through the dispatcher.
            //  - AddMaterialToProject (Phase 3c): NATIVE handler — files through the native
            //    SiNet.Infrastructure.Sql IProjectFileFilingService (FileServer + ACC) instead of the
            //    legacy SiNetSQL filing service. Same legacy IProcessActionHandler contract, so the
            //    FileImportContinuation / ActionExecutor trigger points are unchanged. Returns
            //    Deferred/RequiresUi when ProjectFile/source are missing.
            //  - MoveToProject: backend equivalent of EmailManagementViewModel.MoveToProjectAsync,
            //    files every tagged inbox attachment via IProjectFileFilingService.
            services.AddTransient<SiNetSQL.Domain.Actions.IProcessActionHandler,
                SiNetProjectManagerV2.Services.NativeAddMaterialToProjectProcessActionHandler>();
            services.AddTransient<SiNetSQL.Domain.Actions.IProcessActionHandler,
                SiNetSQL.Domain.Actions.Handlers.MoveToProjectProcessActionHandler>();

            services.AddSingleton<ProjectRecipientCacheService>();
            services.AddTransient<IEmailComposerService, EmailComposerService>();
            services.AddTransient<IInspectionReportEmailBuilder, InspectionReportEmailBuilder>();
            services.AddTransient<IInspectionReportEmailWorkflow, InspectionReportEmailWorkflow>();

            // ACC File Sync: Transient (copies tagged attachments from ACC Inbox → ACC project folders)
            services.AddTransient<SiNetSQL.Services.Coordinators.AccFileSyncService>();

            // ---------------------------------------------------------------------
            // Phase 2B: Centralized project-file filing service.
            //
            // ProjectFileFilingService is the single end-to-end path for placing a
            // file into a project slot (FileServer + ACC + ProjectFileInstance upsert
            // + ACC auto-provision via IAccProjectProvisioningService). UI callers
            // (EmailManagementViewModel.MoveToProjectAsync) are thin bridges over
            // this service.
            //
            // ITokenProvider is registered as a factory that reads Autodesk client
            // credentials from CredentialProvider, mirroring the pattern used in
            // AccProjectProvisioningService and the legacy VM code.
            // ---------------------------------------------------------------------
            services.AddSingleton<MyOffice.AutodeskConnector.ITokenProvider>(_ =>
            {
                var clientId = SiNetSQL.Services.CredentialProvider.AutodeskClientId ?? string.Empty;
                var clientSecret = SiNetSQL.Services.CredentialProvider.AutodeskClientSecret ?? string.Empty;
                return new MyOffice.AutodeskConnector.TokenProvider(clientId, clientSecret);
            });
            services.AddTransient<SiNetSQL.Services.Files.IBim360ServiceFactory, SiNetSQL.Services.Files.Bim360ServiceFactory>();
            services.AddTransient<SiNetSQL.Services.Files.IAccFileClient, SiNetSQL.Services.Files.Bim360AccFileClient>();
            services.AddTransient<SiNetSQL.Services.Files.IFolderPathResolver, SiNetSQL.Services.Files.FolderPathResolver>();
            services.AddTransient<SiNetSQL.Services.Files.IFileServerMetadataStore, SiNetSQL.Services.Files.FileServerMetadataStore>();
            services.AddTransient<SiNetSQL.Services.Files.IFileServerVersionArchiver, SiNetSQL.Services.Files.FileServerVersionArchiver>();
            services.AddTransient<SiNetSQL.Services.Files.IFileServerRootResolver, SiNetSQL.Services.Files.FileServerRootResolver>();
            services.AddTransient<SiNetSQL.Services.Files.IProjectFileFilingService, SiNetSQL.Services.Files.ProjectFileFilingService>();
            services.AddTransient<SiNetSQL.Services.Files.IProjectFileRefileService, SiNetSQL.Services.Files.ProjectFileRefileService>();

            // Phase 3 Step 1: MoveToProject application service. Owns the
            // business flow previously embedded in EmailManagementViewModel.
            // The VM still owns WPF feedback (StatusMessage, MessageBox,
            // CommandManager, Dispatcher refresh) and maps MoveToProjectResult
            // to that feedback. The MoveToProjectProcessActionHandler is
            // intentionally untouched in Step 1.
            services.AddTransient<
                SiNetSQL.Services.MoveToProject.IEmailMoveToProjectApplicationService,
                SiNetSQL.Services.MoveToProject.EmailMoveToProjectApplicationService>();

            // Phase 5: typed-continuation application services for workflow-advance
            // actions. Each owns its dispatcher loop + lifecycle reporting and is the
            // VM-facing entry point for the typed Continuation Request / Result contract.
            services.AddTransient<
                SiNetSQL.Services.ApproveOrClose.IApproveOrCloseApplicationService,
                SiNetSQL.Services.ApproveOrClose.ApproveOrCloseApplicationService>();
            services.AddTransient<
                SiNetSQL.Services.CloseOpinion.ICloseOpinionApplicationService,
                SiNetSQL.Services.CloseOpinion.CloseOpinionApplicationService>();

            // Phase 5 extension: typed-continuation application service for the
            // TaskCreationDialog family (RequestCompletion / PrepareResponse /
            // InternalReview / HandleComments / UpdateDesign / PrepareSubmission /
            // CoordinateWithConsultants / SendUpdatedMaterial / PerformReview /
            // WriteComments / SendComments / TrackCorrections / AnalyzeDocuments /
            // RequestMissingMaterial / PrepareDraftOpinion / UpdateOpinion /
            // SendOpinion). The service owns the Start/Continue loop and persists
            // confirmed drafts through TaskFactory.CreateAsync. ActionExecutor still
            // emits the legacy ActionResult.RequiresUI fallback so existing UI flows
            // keep working until callers migrate to the typed path.
            services.AddTransient<
                SiNetSQL.Services.TaskCreation.ITaskCreationContinuationApplicationService,
                SiNetSQL.Services.TaskCreation.TaskCreationContinuationApplicationService>();

            // Typed-continuation application service for the FileImportDialog
            // family (UploadNewVersion / ReceiveSupplementaryMaterial /
            // ReceiveMaterialForReview / ReceiveCorrectedVersion /
            // ReceiveMaterialForOpinion). The service owns the Start/Continue
            // loop and dispatches one AddMaterialToProject per selection
            // through IProcessActionDispatcher → AddMaterialToProjectProcessActionHandler
            // → IProjectFileFilingService. ActionExecutor refuses these action
            // codes via FileImportTypedRequired() — there is no legacy fallback.
            services.AddTransient<
                SiNetSQL.Services.FileImport.IFileImportContinuationApplicationService,
                SiNetSQL.Services.FileImport.FileImportContinuationApplicationService>();

            // Phase 5: typed ProjectPicker continuation. Owns the typed
            // Start/Continue loop for migrated ProjectPicker-family actions
            // (AssociateToExistingProject, StartWorkflow, CreateNewReview,
            // CreateOpinionProject, OpenReviewRound). The service re-dispatches
            // the original action through ActionExecutor.ExecuteAsync with the
            // user-selected ProjectId; ActionExecutor refuses these action
            // codes (missing ProjectId) via ProjectPickerTypedRequired() — there
            // is no legacy fallback.
            services.AddTransient<
                SiNetSQL.Services.ProjectPicker.IProjectPickerContinuationApplicationService,
                SiNetSQL.Services.ProjectPicker.ProjectPickerContinuationApplicationService>();

            // Phase 5: typed NewProject continuation. Owns the typed
            // Start/Continue loop for migrated NewProjectDialog-family actions
            // (currently only CreateNewProject). The host opens the existing
            // CreateProjectUserControl and captures the CreatedProjectId from
            // CreateProjectViewModel.ProjectCreated. ActionExecutor refuses
            // these action codes via NewProjectTypedRequired() — there is no
            // legacy fallback.
            services.AddTransient<
                SiNetSQL.Services.NewProject.INewProjectContinuationApplicationService,
                SiNetSQL.Services.NewProject.NewProjectContinuationApplicationService>();

            // Phase 5: typed Decision continuation. Owns the typed
            // Start/Continue loop for migrated DecisionDialog-family actions
            // (currently only ForwardToDecision). The host opens the existing
            // ProjectDecisionsWindow, which persists decisions internally via
            // ProjectDecisionService; the typed result carries only the
            // lifecycle outcome. ActionExecutor refuses these action codes via
            // DecisionTypedRequired() — there is no legacy fallback.
            services.AddTransient<
                SiNetSQL.Services.Decision.IDecisionContinuationApplicationService,
                SiNetSQL.Services.Decision.DecisionContinuationApplicationService>();

            // Phase 5: typed Discipline continuation. Owns the typed
            // Start/Continue loop for migrated DisciplineDialog-family actions
            // (currently only AddNewDiscipline). There is no dedicated WPF
            // surface today; the host shows a confirmation prompt. The typed
            // result carries only the lifecycle outcome. ActionExecutor refuses
            // these action codes via DisciplineTypedRequired() — there is no
            // legacy fallback.
            services.AddTransient<
                SiNetSQL.Services.Discipline.IDisciplineContinuationApplicationService,
                SiNetSQL.Services.Discipline.DisciplineContinuationApplicationService>();

            // Phase 5: WPF UI continuation host
            // application services and WPF dialogs. Pilot scope:
            // ContinuationUiKind.WorkflowAdvanceDialog only. Other UI kinds
            // remain on the legacy ActionFollowUp / PrefilledData path.
            services.AddSingleton<
                SiNetSQL.Domain.Actions.Continuation.IActionContinuationUiHost,
                SiNetProjectManagerV2.Services.WpfActionContinuationUiHost>();

            // ACC Metadata Status Reporter: Singleton (shared collector of Custom-Attribute
            // failures — surfaced as a badge in ProjectWorkView so the user sees when
            // their ACC role / license can't read/write metadata).
            services.AddSingleton<SiNetSQL.FileIndex.IAccMetadataStatusReporter, SiNetSQL.FileIndex.AccMetadataStatusReporter>();
            services.AddSingleton<SiNetSQL.FileIndex.IAccItemMetadataService, SiNetSQL.FileIndex.AccItemMetadataService>();
            services.AddSingleton<SiNet.Application.Abstractions.Autodesk.IAccInboxReconciliationService, SiNetSQL.Services.EmailIngestion.AccInboxReconciliationService>();
            services.AddSingleton<SiNetSQL.Services.EmailIngestion.IAccInboxRecoveryService, SiNetSQL.Services.EmailIngestion.AccInboxRecoveryService>();

            // System Health: lightweight in-memory aggregator + per-service safe checks.
            // Read-only probes only (no writes, no OAuth popups). Reuses AppLogger for transitions.
            services.AddSingleton<SiNetSQL.Services.Health.ISystemHealthService, SiNetSQL.Services.Health.SystemHealthService>();
            services.AddSingleton<Lazy<SiNetSQL.Services.Health.ISystemHealthService>>(sp =>
                new Lazy<SiNetSQL.Services.Health.ISystemHealthService>(
                    () => sp.GetRequiredService<SiNetSQL.Services.Health.ISystemHealthService>()));
            services.AddSingleton<SiNetSQL.Services.Health.IServiceHealthCheck, SiNetSQL.Services.Health.Checks.DatabaseHealthCheck>();
            services.AddSingleton<SiNetSQL.Services.Health.IServiceHealthCheck, SiNetSQL.Services.Health.Checks.OllamaHealthCheck>();
            services.AddSingleton<SiNetSQL.Services.Health.IServiceHealthCheck, SiNetSQL.Services.Health.Checks.GoogleHealthCheck>();
            services.AddSingleton<SiNetSQL.Services.Health.IServiceHealthCheck, SiNetSQL.Services.Health.Checks.AutodeskAccHealthCheck>();
            // AutodeskAccHealthCheck takes Func<ITokenProvider> — DI doesn't synthesize Func<T> automatically.
            services.AddSingleton<Func<MyOffice.AutodeskConnector.ITokenProvider>>(sp =>
                () => sp.GetRequiredService<MyOffice.AutodeskConnector.ITokenProvider>());
            services.AddSingleton<SiNetSQL.Services.Health.IServiceHealthCheck, SiNetSQL.Services.Health.Checks.FileServerHealthCheck>();
            services.AddSingleton<SiNetSQL.Services.Health.IServiceHealthCheck, SiNetSQL.Services.Health.Checks.WorkflowHealthCheck>();
            services.AddSingleton<SiNetSQL.Services.Health.IServiceHealthCheck, SiNetProjectManagerV2.Services.Health.InternalAccServiceHealthCheck>();
            
            services.AddSingleton<SiNetProjectManagerV2.Services.GoogleDriveFolderDiagnosticService>();

            // Google Drive Diagnostics Health Checks
            services.AddSingleton<SiNetSQL.Services.Health.IServiceHealthCheck, SiNetProjectManagerV2.Services.Health.GoogleAuthConfigHealthCheck>();
            services.AddSingleton<SiNetSQL.Services.Health.IServiceHealthCheck, SiNetProjectManagerV2.Services.Health.GoogleAccountHealthCheck>();
            services.AddSingleton<SiNetSQL.Services.Health.IServiceHealthCheck, SiNetProjectManagerV2.Services.Health.GoogleTemplatesFolderHealthCheck>();
            services.AddSingleton<SiNetSQL.Services.Health.IServiceHealthCheck, SiNetProjectManagerV2.Services.Health.GoogleReportsFolderHealthCheck>();

            // File Index: unified scan/open/upload abstraction over FileServer / ACC / GoogleDrive.
            // Stores are registered as IFileStore so FileIndexService can enumerate them all.
            services.AddSingleton<SiNetSQL.FileIndex.IFileStore, SiNetSQL.FileIndex.Stores.FileServerStore>();
            services.AddSingleton<SiNetSQL.FileIndex.IFileStore, SiNetSQL.FileIndex.Stores.AccFileStore>();

            // Google Drive: settings + lazy auth provider. Provider returns null when
            // SharedDriveId / ProjectsRootFolderId / auth are unavailable — the Drive
            // store then fails explicitly (no fallback to FileServer / ACC).
            services.AddSingleton(sp => new SiNetSQL.FileIndex.Stores.GoogleDriveSettings
            {
                SharedDriveId = AppConfiguration.GoogleDriveSharedDriveId,
                ProjectsRootFolderId = AppConfiguration.GoogleDriveProjectsRootFolderId,
            });
            services.AddSingleton<SiNetSQL.FileIndex.Stores.IGoogleDriveServiceProvider,
                                  SiNetProjectManagerV2.Services.GoogleDriveServiceProvider>();
            services.AddSingleton<SiNetSQL.FileIndex.IFileStore, SiNetSQL.FileIndex.Stores.GoogleDriveStore>();
            services.AddSingleton<SiNetSQL.FileIndex.FileIndexService>();

            // Stage 9E.1 — Runtime File Resolver / Session Cache.
            // In-memory only; never persisted. Replaces ProjectFileInstance as the
            // runtime answer to "does this expected file currently exist at its
            // storage destination?". Authoritative state still lives in ACC / Drive
            // / File Server — this is only a cache of recent checks.
            services.AddSingleton<SiNetSQL.FileIndex.Resolution.IProjectFileLocationResolver,
                                  SiNetSQL.FileIndex.Resolution.ProjectFileLocationResolver>();

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

                // Apply DB overrides if available (DB is already cached by this point). This blocks on
                // Task.Run because IServiceProvider factory delegates are synchronous by contract; the
                // detour through the thread pool keeps it off the UI SynchronizationContext.
                var settings = sp.GetRequiredService<SystemSettingsService>();
                var dbUrl = Task.Run(() => settings.GetAsync(SystemSettingKeys.OllamaBaseUrl).AsTask()).GetAwaiter().GetResult();
                if (!string.IsNullOrWhiteSpace(dbUrl))
                    ollama.BaseUrl = dbUrl;
                var dbModel = Task.Run(() => settings.GetAsync(SystemSettingKeys.OllamaModel).AsTask()).GetAwaiter().GetResult();
                if (!string.IsNullOrWhiteSpace(dbModel))
                    ollama.Model = dbModel;

                return ollama;
            });

            // Level-based AI router: resolves model+provider per AiModelLevel from
            // SystemSettings and dispatches to OllamaService (and future cloud providers).
            services.AddSingleton<SiNetSQL.Services.AI.AiService>(sp =>
                new SiNetSQL.Services.AI.AiService(
                    sp.GetRequiredService<SystemSettingsService>(),
                    sp.GetRequiredService<SiNetSQL.Services.OllamaService>(),
                    sp.GetService<ILoggerFactory>()?.CreateLogger<SiNetSQL.Services.AI.AiService>()));

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

                // ─── [AccService] Diagnostic logging for remote mode ───────────────
                // Logs safe metadata to help diagnose connectivity issues without exposing secrets.
                string clientUser;
                try { clientUser = Environment.UserDomainName + "\\" + Environment.UserName; }
                catch { clientUser = "(unknown)"; }
                // Presence only. Key length and hash prefixes are secret fingerprints and must not
                // reach the central log (see docs/OPS-P0-SECRET-ROTATION.md).
                var hasApiKey = !string.IsNullOrWhiteSpace(apiKey);
                Log.Warning(
                    "[AccService] DI registration — mode=REMOTE, baseUrl={BaseUrl}, " +
                    "clientUser={ClientUser}, hasApiKey={HasApiKey}, " +
                    "services=[RemoteAccProjectProvisioningService, RemoteAccInboxProvisioner].",
                    accServiceBaseUrl, clientUser, hasApiKey);

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
                            SiOffice.AccService.Contracts.AccServiceContracts.ApiKeyHeader,
                            apiKey);
                    }
                    else
                    {
                        Log.Warning("[AccService] HttpClient configured WITHOUT X-AccService-Key header — API key is missing from vault.");
                    }
                }

                // ─── SSL Certificate Validation for AccService ───────────────────
                // SiOffice.AccService may present a self-signed cert. Accept chain errors
                // only for loopback or when the server cert thumbprint is explicitly pinned.
                var pinnedThumbprints = AccServiceControlPlaneConfiguration
                    .ReadPinnedCertificateThumbprints(AppConfiguration.Configuration);
                var tlsOptions = new AccServiceControlPlaneOptions
                {
                    PinnedCertificateThumbprints = pinnedThumbprints,
                };
                var accServiceUri = new Uri(accServiceBaseUrl.TrimEnd('/') + "/");
                var accServiceHost = accServiceUri.Host;

                if (accServiceUri.IsLoopback)
                {
                    Log.Warning(
                        "[AccService] SSL: loopback host '{Host}' — chain errors accepted for local development.",
                        accServiceHost);
                }
                else if (pinnedThumbprints.Count > 0)
                {
                    Log.Information(
                        "[AccService] SSL: thumbprint pinning enabled for host '{Host}' ({Count} pin(s)).",
                        accServiceHost, pinnedThumbprints.Count);
                }
                else
                {
                    Log.Warning(
                        "[AccService] SSL: no thumbprint pins configured for host '{Host}'. " +
                        "Only CA-trusted certificates will be accepted unless the host is loopback.",
                        accServiceHost);
                }

                Action<HttpClient> configure = ConfigureAccServiceClient;
                Action<IHttpClientBuilder> configureHandler = b =>
                {
                    b.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = (message, certificate, _, errors) =>
                            AccServiceHttpClientConfigurator.ValidateServerCertificate(
                                message, certificate, errors, tlsOptions),
                    });
                };

                configureHandler(services.AddHttpClient<IAccProjectProvisioningService, RemoteAccProjectProvisioningService>(configure));
                configureHandler(services.AddHttpClient<SiNetSQL.Services.AccBootstrap.IAccInboxProvisioner, RemoteAccInboxProvisioner>(configure));
                Log.Information(
                    "ACC Provisioning: using REMOTE SiOffice.AccService at {Url} (host={Host}, loopback={IsLoopback}, pinnedThumbprints={PinCount}).",
                    accServiceBaseUrl, accServiceHost, accServiceUri.IsLoopback, pinnedThumbprints.Count);
            }
            else
            {
                services.AddTransient<IAccProjectProvisioningService, AccProjectProvisioningService>();
                services.AddTransient<SiNetSQL.Services.AccBootstrap.IAccInboxProvisioner,
                                      SiNetSQL.Services.AccBootstrap.LocalAccInboxProvisioner>();
                Log.Warning(
                    "[AccService] DI registration — mode=LOCAL/InProcess, baseUrl=<empty>, " +
                    "services=[AccProjectProvisioningService, LocalAccInboxProvisioner].");
            }

            // Application port used by SqlProjectCreateService after DB insert (best-effort).
            services.AddTransient<SiNet.Application.Projects.IProjectAccMappingProvisioner,
                ProjectAccMappingProvisionerAdapter>();

            // ACC Membership Reconciler: Singleton (single background worker, debounced Channel).
            // Re-syncs every ACC project's members with the local Siuser table when triggered.
            services.AddSingleton<IAccMembershipReconciler, AccMembershipReconciler>();

            // ═══════════════════════════════════════════════════════════════════
            // VIEWMODELS: Register all ViewModels that use IDbContextFactory
            // Transient lifetime ensures each request gets a fresh instance.
            // ═══════════════════════════════════════════════════════════════════
            services.AddTransient<TaskPanelViewModel>();
            services.AddTransient<FloatingProjectTasksViewModel>();
            services.AddTransient<SiNetSQL.Services.InspectionSync.IInspectionReportService, SiNetSQL.Services.InspectionSync.InspectionReportService>();
            services.AddTransient<SiNetSQL.Services.InspectionSync.IInspectionDrawingManagementService, SiNetSQL.Services.InspectionSync.InspectionDrawingManagementService>();
            services.AddTransient<FloatingInspectionViewModel>();
            services.AddTransient<ProjectTypeRulesViewModel>();
            services.AddTransient<SiNetSQL.Services.ProjectTypes.IProjectTypeService, SiNetSQL.Services.ProjectTypes.ProjectTypeService>();
            services.AddTransient<SiNetSQL.Services.InspectionSync.TemplateSyncService>();
            services.AddTransient<ProjectTypeViewModel>();
            // Company persistence is owned by CompanyService (ViewModel → Service
            // boundary, gap register Gap 11 / pilot). The ViewModel holds UI state only.
            services.AddTransient<SiNetSQL.Services.Companies.ICompanyService, SiNetSQL.Services.Companies.CompanyService>();
            services.AddTransient<CompanyViewModel>();
            services.AddTransient<SiNetSQL.Services.Contacts.IContactService, SiNetSQL.Services.Contacts.ContactService>();
            services.AddTransient<ContactViewModel>();
            services.AddTransient<SiNetSQL.Services.Places.IPlaceService, SiNetSQL.Services.Places.PlaceService>();
            services.AddTransient<PlaceViewModel>();
            services.AddTransient<CreateProjectViewModel>();
            services.AddTransient<EditProjectViewModel>();
            services.AddTransient<SiNetSQL.Services.Users.IUserService, SiNetSQL.Services.Users.UserService>();
            services.AddTransient<SiNetSQL.Services.Authorization.IActionPermissionService, SiNetSQL.Services.Authorization.ActionPermissionService>();
            services.AddTransient<AddUserViewModel>();
            services.AddTransient<UserManagementViewModel>();
            services.AddTransient<MasterPlanMappingViewModel>();
            services.AddTransient<EmailContextViewModel>();
            services.AddTransient<WorkflowDashboardViewModel>();
            services.AddTransient<WorkflowInstanceViewModel>();
            services.AddTransient<WorkflowDesignerViewModel>();
            services.AddTransient<WorkflowClosedViewerViewModel>();
            services.AddTransient<SiNetSQL.Services.IProjectDecisionService, SiNetSQL.Services.ProjectDecisionService>();
            services.AddTransient<ProjectDecisionsViewModel>();

            // Shared, application-wide Project Context for the new WPF surfaces.
            // First register the REAL read-only IProjectQueryService (SQL-backed, DTO-only) — it reuses
            // the IDbContextFactory<SiNetSQLDbContext> registered by AddSiNetSql above. Then register the
            // shell pieces (a single ICurrentProjectContext + Email window factory) via the runtime path,
            // so the Email selector loads real projects while every surface observes the SAME Current
            // Project. Read-only: no DB writes, no EF entities in WPF, no email filtering, no workflow mutation.
            services.AddSiNetNewSystemGraph();
        services.AddSingleton<SiNet.Application.Projects.IProjectFolderBootstrapper, LegacyProjectFolderBootstrapper>();

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

                // Resolved log targets — always emitted so the local log states
                // exactly which network folder/file the central log is being
                // written to. Makes "central folder is empty" trivial to diagnose.
                Log.Warning(
                    "SiNetProjectManagerV2 log targets — local file: {LocalFile}, central file: {CentralFile}, central enabled: {CentralEnabled}.",
                    CentralLoggingBuilder.LocalSinkTargetFile ?? "(none)",
                    CentralLoggingBuilder.CentralSinkTargetFile ?? "(disabled — Logging.CentralLogPath empty)",
                    CentralLoggingBuilder.CentralSinkEnabled);

                if (CentralLoggingBuilder.CentralSinkBootstrapError is { } centralErr)
                {
                    Log.Warning("SiNetProjectManagerV2: {Detail}", centralErr);
                }

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

                Log.Information("[STARTUP] Step 1: Startup mode selection (first visible UI)...");
                var selectedMode = SiNet.App.Wpf.Shell.StartupModeSelectionWindow.TryPromptForMode();
                if (selectedMode is null)
                {
                    Log.Information("[STARTUP] Startup mode selection cancelled. Shutting down.");
                    Shutdown();
                    return;
                }

                if (SiNet.App.Wpf.Shell.StartupModeRouter.OpensNewShell(selectedMode.Value))
                {
                    // The New System pipeline awaits async ports (system settings, saved theme, shell
                    // authorization), so it is queued on the dispatcher and owns the same fatal-error
                    // handling as this method rather than running inside this try block.
                    _ = Dispatcher.InvokeAsync(() => RunNewSystemStartupAsync(e));
                    return;
                }

                RunLegacyStartup(e);
                Log.Information("[STARTUP] ═══ Application startup completed successfully ═══");
            }
            catch (Exception ex)
            {
                HandleFatalStartupFailure(ex);
            }
        }

        /// <summary>
        /// Last line of defense before a silent startup crash: logs, tells the user where the logs are,
        /// and shuts down. Shared by <see cref="OnStartup"/> and <see cref="RunNewSystemStartupAsync"/>.
        /// </summary>
        private void HandleFatalStartupFailure(Exception ex)
        {
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

        #region Startup Pipeline Steps

        /// <summary>
        /// New System startup (see <c>docs/APP_SHELL.md</c> §3): credential vault → DB → DI → optional
        /// debug role selector → <see cref="AuthorizeCurrentUser"/> → <see cref="NewShellWindow"/>.
        /// No legacy schema gate or <see cref="MainWindow"/>. No silent fallback to Legacy on failure.
        /// </summary>
        private async Task RunNewSystemStartupAsync(StartupEventArgs e)
        {
            try
            {
                await RunNewSystemStartupCoreAsync(e).ConfigureAwait(true);
                Log.Information("[STARTUP] ═══ Application startup completed (New System) ═══");
            }
            catch (Exception ex)
            {
                HandleFatalStartupFailure(ex);
            }
        }

        private async Task RunNewSystemStartupCoreAsync(StartupEventArgs e)
        {
            Log.Warning(
                "[STARTUP][NewSystem] DEPRECATED: New System hosted inside SiNetProjectManagerV2.exe. " +
                "Prefer SiNet.App.Wpf.exe as the standalone New System host " +
                "(see docs/STANDALONE_NEW_SYSTEM_HOST.md). This V2 path remains for pilot fallback only.");

            Log.Information("[STARTUP][NewSystem] Setting up credential vault (connection string)...");
            SetupCredentialVaultForNewSystem();

            Log.Information("[STARTUP][NewSystem] Ensuring database connection...");
            if (!EnsureDatabaseConnectionForNewSystem())
            {
                Log.Warning("[STARTUP][NewSystem] Database connection failed. Shutting down.");
                Shutdown();
                return;
            }

            Log.Information("[STARTUP][NewSystem] Configuring logging...");
            ConfigureLoggingAndSettings();

            Log.Information("[STARTUP][NewSystem] Configuring DI services (HostMode=V2Hybrid)...");
            ServiceProvider = ConfigureServices();
            WireLegacyLocators();
            SiNetSQL.Services.ServiceLocator.Initialize(ServiceProvider);
            try { WireGoogleHealthAuthRefresh(); }
            catch (Exception ex) { AppLogger.Info($"[Health][google] failed to wire AuthStateChanged refresh: {ex.Message}"); }
            StartNewSystemConnectorAuthRestore();

#if DEBUG
            Log.Information("[STARTUP][NewSystem] Debug Authorization Role Selector (when enabled)...");
            RunDebugAuthorizationRoleSelector();
#endif

            Log.Information("[STARTUP][NewSystem] Authorizing current user...");
            if (!AuthorizeCurrentUser())
            {
                Log.Warning("[STARTUP][NewSystem] User authorization failed. Shutting down.");
                Shutdown();
                return;
            }

            Log.Information("[STARTUP][NewSystem] Schema gate (Task Management / connectivity)...");
            if (!ValidateDatabaseSchema(out _))
            {
                Log.Warning("[STARTUP][NewSystem] Schema validation failed. Shutting down.");
                Shutdown();
                return;
            }

            Log.Information("[STARTUP][NewSystem] Loading management settings + default project cache...");
            await LoadManagementSettingsFromDbAsync().ConfigureAwait(true);
            WarmDefaultProjectCacheForNewSystem();

            Log.Information("[STARTUP][NewSystem] Initializing status colors...");
            InitializeStatusColors();

            await ApplyNewSystemThemeFromSavedSettingsAsync().ConfigureAwait(true);
            SchedulePdfRendererInit();

            base.OnStartup(e);
            await LaunchNewSystemShellAsync().ConfigureAwait(true);
        }

        /// <summary>
        /// Refreshes System Health Google rows when Gmail or Reports/Inspection auth changes.
        /// Folder diagnostics use <see cref="GoogleAuthService"/>; Gmail rows use <see cref="GoogleService"/>.
        /// </summary>
        private static void WireGoogleHealthAuthRefresh()
        {
            if (ServiceProvider is null)
                return;

            var healthSvc = ServiceProvider.GetRequiredService<SiNetSQL.Services.Health.ISystemHealthService>();

            var googleSvc = ServiceProvider.GetRequiredService<GoogleService>();
            googleSvc.AuthStateChanged += (_, _) =>
            {
                AppLogger.Info("[Health][google] AuthStateChanged -> refreshing all Google rows");
                _ = healthSvc.RefreshAsync("google", System.Threading.CancellationToken.None);
                _ = healthSvc.RefreshAsync("google_account", System.Threading.CancellationToken.None);
                _ = healthSvc.RefreshAsync(SystemSettingKeys.InspectionTemplatesFolderId, System.Threading.CancellationToken.None);
                _ = healthSvc.RefreshAsync(SystemSettingKeys.InspectionReportsFolderId, System.Threading.CancellationToken.None);
            };

            var googleAuth = ServiceProvider.GetRequiredService<GoogleAuthService>();
            googleAuth.AuthStateChanged += (_, _) =>
            {
                AppLogger.Info("[Health][google-auth] AuthStateChanged -> refreshing Inspection folder rows");
                _ = healthSvc.RefreshAsync(SystemSettingKeys.InspectionTemplatesFolderId, System.Threading.CancellationToken.None);
                _ = healthSvc.RefreshAsync(SystemSettingKeys.InspectionReportsFolderId, System.Threading.CancellationToken.None);
            };
        }

        /// <summary>
        /// Warms DefaultProjectService cache so the first ACC ingest does not rely on a cold static cache.
        /// Schema validation for New System runs in <see cref="RunNewSystemStartup"/> before shell launch.
        /// </summary>
        private static void WarmDefaultProjectCacheForNewSystem()
        {
            try
            {
                var dbContextFactory =
                    ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQL.Data.SiNetSQLDbContext>>();
                var defaultProjectService = new SiNetSQL.Services.DefaultProjectService(dbContextFactory);
                var projectId = defaultProjectService.EnsureDefaultProjectExists();

                Log.Information(
                    "[STARTUP][NewSystem] Default project ready. ProjectId={ProjectId}",
                    projectId);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[STARTUP][NewSystem] Default project cache warmup failed; ingest will retry on demand.");
            }
        }

        /// <summary>
        /// Opens the clean New System shell. On failure shows an error and shuts down — no Legacy fallback.
        /// Awaited on the UI thread: shell construction resolves the current user profile and the
        /// per-surface authorization decisions through async ports.
        /// </summary>
        private static async Task LaunchNewSystemShellAsync()
        {
            try
            {
                var factory = ServiceProvider.GetRequiredService<SiNet.App.Wpf.Shell.INewShellFactory>();
                var shell = await factory.CreateShellAsync(_appShutdownCts.Token).ConfigureAwait(true);

                Current.MainWindow = shell;
                Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
                shell.Show();

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var runtime = ServiceProvider.GetService<SiNet.Application.Runtime.IRuntimeSubsystemStatusService>();
                        if (runtime is not null)
                            await runtime.RefreshAsync(_appShutdownCts.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Log.Debug(ex, "[STARTUP][NewSystem] Initial runtime status refresh failed.");
                    }
                }, _appShutdownCts.Token);

                Log.Information("[STARTUP][NewSystem] NewShell opened (legacy MainWindow not loaded).");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[STARTUP][NewSystem] Failed to launch New System shell.");
                MessageBox.Show(
                    $"לא ניתן לפתוח את המערכת החדשה:\n\n{ex.Message}",
                    "שגיאת הפעלה",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Current.Shutdown();
            }
        }

        /// <summary>
        /// Legacy production startup path — unchanged gates ending in splash + <see cref="MainWindow"/>.
        /// </summary>
        private void RunLegacyStartup(StartupEventArgs e)
        {
            Log.Information("[STARTUP][Legacy] Step 1: Setting up credential vault...");
            SetupCredentialVault();

            Log.Information("[STARTUP][Legacy] Step 2: Ensuring database connection...");
            if (!EnsureDatabaseConnection())
            {
                Log.Warning("[STARTUP][Legacy] Database connection failed. Shutting down.");
                Shutdown();
                return;
            }

            Log.Information("[STARTUP][Legacy] Step 3: Configuring logging...");
            ConfigureLoggingAndSettings();

            Log.Information("[STARTUP][Legacy] Step 4: Configuring DI services...");
            ServiceProvider = ConfigureServices();
            WireLegacyLocators();
            SiNetSQL.Services.ServiceLocator.Initialize(ServiceProvider);

            try
            {
                WireGoogleHealthAuthRefresh();
            }
            catch (Exception ex)
            {
                AppLogger.Info($"[Health][google] failed to wire AuthStateChanged refresh: {ex.Message}");
            }

            Log.Information("[STARTUP][Legacy] Step 4b: Loading management settings from DB...");
            // The Legacy pipeline is still synchronous end to end (RunLegacyStartup is called inline from
            // OnStartup). Only the New System path was converted to await this; making Legacy async is
            // tracked in docs/P2-TECH-DEBT-BACKLOG.md rather than done here.
            Task.Run(LoadManagementSettingsFromDbAsync).GetAwaiter().GetResult();

            Log.Information("[STARTUP][Legacy] Step 5: Scheduling background services...");
            SchedulePdfRendererInit();
            StartAccUserBootstrap();

            Log.Information("[STARTUP][Legacy] Step 6: Validating database schema...");
            if (!ValidateDatabaseSchema(out var defaultProjectError))
            {
                Log.Warning("[STARTUP][Legacy] Database schema validation failed. Shutting down.");
                Shutdown();
                return;
            }

#if DEBUG
            Log.Information("[STARTUP][Legacy] Step 6b: Debug Authorization Role Selector...");
            RunDebugAuthorizationRoleSelector();
#endif

            Log.Information("[STARTUP][Legacy] Step 7: Authorizing current user...");
            if (!AuthorizeCurrentUser())
            {
                Log.Warning("[STARTUP][Legacy] User authorization failed. Shutting down.");
                Shutdown();
                return;
            }

            Log.Information("[STARTUP][Legacy] Step 8: Post-auth initialization...");
            if (defaultProjectError is not null && !HandleDefaultProjectFailure())
            {
                Log.Warning("[STARTUP][Legacy] Default project handling failed. Shutting down.");
                Shutdown();
                return;
            }

            Log.Information("[STARTUP][Legacy] Step 8: Initializing status colors...");
            InitializeStatusColors();

            Log.Information("[STARTUP][Legacy] Step 9: Enforcing single instance...");
            if (!EnforceSingleInstance())
            {
                Log.Information("[STARTUP][Legacy] Another instance is already running. Shutting down.");
                Shutdown();
                return;
            }

            Log.Information("[STARTUP][Legacy] Step 10: Launching legacy main window...");
            base.OnStartup(e);
            ShowSplashThenMainWindow();

            Dispatcher.BeginInvoke(ShowSyncFailureAlertIfAdmin, DispatcherPriority.Background);
        }

        /// <summary>
        /// Wires the credential bridge and auto-imports provisioning file if available.
        /// Non-fatal — continues even if user skips vault setup.
        /// </summary>
        private static void SetupCredentialVault()
            => SetupCredentialVaultCore(allowLegacyDialogs: true, newSystemPath: false);

        /// <summary>
        /// New System vault setup: prefer silent vault; legacy dialogs only as explicit deprecated fallback.
        /// <para>
        /// The provisioning password prompt is served by the native
        /// <see cref="SiNet.App.Wpf.Admin.Security.ProvisioningPasswordWindow"/>. The vault/DB setup
        /// surface still falls back to <c>WPF_Window.SecretSetupWindow</c>: the native window is
        /// DI-resolved and its ACC status presenter needs <c>ILocalAccProjectService</c>, which needs a
        /// DbContext factory that only exists once the vault has produced the connection string. See
        /// <c>docs/APP_SHELL.md</c> §"Legacy dialogs in the New System startup path".
        /// </para>
        /// </summary>
        private static void SetupCredentialVaultForNewSystem()
            => SetupCredentialVaultCore(allowLegacyDialogs: true, newSystemPath: true);

        private static void SetupCredentialVaultCore(bool allowLegacyDialogs, bool newSystemPath)
        {
            CredentialProvider.GetSecret = CredentialVaultService.GetSecret;

            // Auto-detect encrypted provisioning file next to the exe
            var provisioningPath = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
                "SiNet.secrets");

            if (!CredentialVaultService.IsVaultConfigured()
                && SecretProvisioningService.IsProvisioningFile(provisioningPath))
            {
                if (!allowLegacyDialogs)
                {
                    Log.Error(
                        "[STARTUP][NewSystem] Provisioning file found but UI import is disabled. " +
                        "Provision vault offline (Install-OnServer.ps1) before launch.");
                }
                else
                {
                    // The password prompt has no container dependencies, so the New System path uses the
                    // native window and the legacy dialog stays on the Legacy path only.
                    var enteredPassword = newSystemPath
                        ? PromptProvisioningPasswordNative()
                        : PromptProvisioningPasswordLegacy();

                    if (enteredPassword is not null)
                    {
                        try
                        {
                            var imported = SecretProvisioningService.ImportFromFile(
                                provisioningPath, enteredPassword);
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
            }

            // Check if vault is provisioned — if not, open setup dialog (Legacy path) or fail closed (New System prefer)
            if (!CredentialVaultService.IsVaultConfigured())
            {
                if (newSystemPath && !allowLegacyDialogs)
                {
                    Log.Fatal("[STARTUP][NewSystem] Credential vault is not configured. Fail closed.");
                    MessageBox.Show(
                        "כספת הסודות אינה מוגדרת. יש להגדיר את ה־Credential Vault לפני הפעלת New System.",
                        "חסרה כספת סודות",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                if (newSystemPath)
                {
                    Log.Warning(
                        "DEPRECATED: legacy dialog on New System path — Stage 4 partial " +
                        "(SecretSetupWindow). Native secret setup is preferred.");
                }

                var setupWindow = new WPF_Window.SecretSetupWindow();
                if (setupWindow.ShowDialog() != true)
                {
                    // User cancelled setup — warn but continue (fallback to config files)
                    Log.Warning("Credential vault setup was skipped. Falling back to configuration files.");
                }
            }
        }

        /// <summary>
        /// Native provisioning password prompt used by the New System startup path.
        /// Returns <c>null</c> when the user cancels.
        /// </summary>
        private static string? PromptProvisioningPasswordNative()
        {
            var dialog = new SiNet.App.Wpf.Admin.Security.ProvisioningPasswordWindow(
                requireConfirmation: false,
                title: "נמצא קובץ הגדרות — הזן סיסמה לייבוא");

            return dialog.ShowDialog() == true ? dialog.EnteredPassword : null;
        }

        /// <summary>
        /// Legacy provisioning password prompt. Deprecated for the New System path (replaced by
        /// <see cref="PromptProvisioningPasswordNative"/>); retained for the Legacy startup path.
        /// Returns <c>null</c> when the user cancels.
        /// </summary>
        private static string? PromptProvisioningPasswordLegacy()
        {
            var dialog = new WPF_Window.ProvisioningPasswordDialog
            {
                RequireConfirmation = false,
                Title = "נמצא קובץ הגדרות — הזן סיסמה לייבוא",
            };

            return dialog.ShowDialog() == true ? dialog.EnteredPassword : null;
        }

        /// <summary>
        /// Verifies database connectivity in a retry loop, opening SecretSetupWindow if needed.
        /// </summary>
        /// <returns>True if connected successfully, false if user cancelled (app should shutdown).</returns>
        private static bool EnsureDatabaseConnection()
            => EnsureDatabaseConnectionCore(newSystemPath: false);

        private static bool EnsureDatabaseConnectionForNewSystem()
            => EnsureDatabaseConnectionCore(newSystemPath: true);

        private static bool EnsureDatabaseConnectionCore(bool newSystemPath)
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

                if (newSystemPath)
                {
                    Log.Warning(
                        "DEPRECATED: legacy dialog on New System path — Stage 4 partial " +
                        "(SecretSetupWindow for DB connection).");
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
        private static async Task LoadManagementSettingsFromDbAsync()
        {
            var settingsService = ServiceProvider.GetRequiredService<SystemSettingsService>();
            var title = await settingsService
                .GetOrDefaultAsync(SystemSettingKeys.DefaultProjectTitle, string.Empty)
                .ConfigureAwait(true);

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
            var services = ServiceProvider;
            if (services is null)
                return;

            var startupTasks = services.GetService<SiNet.Application.Runtime.IStartupTaskRegistry>();
            try
            {
                startupTasks?.Begin("pdf-renderer", "מנוע PDF");
                var pdfRenderer = services.GetRequiredService<WebView2PdfRenderer>();

                // Initialize WebView2 asynchronously but don't block the UI during init
                Dispatcher.BeginInvoke(async () =>
                {
                    try
                    {
                        await pdfRenderer.InitializeAsync();

                        // Wire up the PDF renderer to the EmailIngestionServiceFactory
                        var factory = services.GetRequiredService<IEmailIngestionServiceFactory>();
                        factory.SetPdfRenderer(pdfRenderer);
                        startupTasks?.Complete("pdf-renderer", succeeded: true, "מוכן");
                    }
                    catch (Exception ex)
                    {
                        startupTasks?.Complete("pdf-renderer", succeeded: false, ex.Message);
                        Log.Warning(ex, "PDF renderer initialization failed. Email body PDFs will not be generated.");
                    }
                }, DispatcherPriority.Background);
            }
            catch (Exception pdfEx)
            {
                // Non-fatal: PDF generation is optional
                startupTasks?.Complete("pdf-renderer", succeeded: false, pdfEx.Message);
                Log.Warning(pdfEx, "PDF renderer initialization failed. Email body PDFs will not be generated.");
            }
        }

        /// <summary>
        /// Attempts silent restore of connector auth sessions (native Gmail/Google) for New System startup.
        /// Uses the same <see cref="IConnectorAuthService"/> port and off-UI-thread pattern as the
        /// standalone harness (<c>src/SiNet.App.Wpf/App.xaml.cs</c>). Failures are logged only; no
        /// interactive login, no retry loop, and no new fallback path.
        /// </summary>
        private static void StartNewSystemConnectorAuthRestore()
        {
            var services = ServiceProvider;
            if (services is null)
                return;

            var startupTasks = services.GetService<SiNet.Application.Runtime.IStartupTaskRegistry>();
            startupTasks?.Begin("gmail-restore", "שחזור Gmail");
            _ = Task.Run(async () =>
            {
                try
                {
                    var connectorAuthServices = services.GetServices<IConnectorAuthService>().ToArray();
                    if (connectorAuthServices.Length == 0)
                    {
                        Log.Debug("[STARTUP][NewSystem] No IConnectorAuthService registered; skipping silent restore.");
                        startupTasks?.Complete("gmail-restore", succeeded: true, "אין שירות אימות — דולג");
                        return;
                    }

                    Log.Information(
                        "[STARTUP][NewSystem] Attempting silent connector auth restore ({Count} service(s))...",
                        connectorAuthServices.Length);

                    var anyRestored = false;
                    foreach (var authService in connectorAuthServices)
                    {
                        var restored = await authService
                            .TryRestoreSessionAsync(_appShutdownCts.Token)
                            .ConfigureAwait(false);
                        anyRestored |= restored;
                        Log.Information("[STARTUP][NewSystem] Connector auth silent restore result: {Restored}", restored);
                    }

                    startupTasks?.Complete(
                        "gmail-restore",
                        succeeded: true,
                        anyRestored ? "סשן שוחזר" : "אין סשן לשחזור");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    startupTasks?.Complete("gmail-restore", succeeded: false, ex.Message);
                    Log.Warning(ex, "[STARTUP][NewSystem] Silent connector auth restore failed; continuing without session.");
                }
                catch (OperationCanceledException)
                {
                    startupTasks?.Complete("gmail-restore", succeeded: false, "בוטל");
                    // Expected on shutdown — don't log as error
                }
            }, _appShutdownCts.Token);
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
                    var accServiceBaseUrl = AppConfiguration.Configuration["AccService:BaseUrl"];
                    var inboxProvisioner = ServiceProvider.GetRequiredService<IAccInboxProvisioner>();
                    Log.Information("Starting Office Inbox ensure before ACC user bootstrap.");
                    var (accProjectId, accInboxFolderId) = await inboxProvisioner.EnsureAsync(_appShutdownCts.Token);
                    Log.Information(
                        "Office Inbox ensure completed. AccProjectId={AccProjectId}, AccInboxFolderId={AccInboxFolderId}",
                        accProjectId,
                        accInboxFolderId);

                    if (!string.IsNullOrWhiteSpace(accServiceBaseUrl))
                    {
                        Log.Information("Skipping local ACC user bootstrap because remote ACC service is configured.");
                        return;
                    }

                    var bootstrapService = ServiceProvider.GetRequiredService<IAccUserBootstrapService>();
                    await bootstrapService.ProvisionUsersAsync(_appShutdownCts.Token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.Error(ex, "Office Inbox ensure or ACC User Bootstrap failed unexpectedly.");
                }
                catch (OperationCanceledException)
                {
                    // Expected on shutdown — don't log as error
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

                // Seed Workflow definitions in background (idempotent — skips existing definitions).
                // Native seed (SqlWorkflowSeedService) is now the single source of workflow-definition
                // truth, matching the native engine that runs them. The legacy WorkflowSeedService is
                // no longer registered. Task-lifecycle behaviors still seed via the legacy behavior
                // seeder until a native behavior seeder is introduced.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var workflowSeedService = ServiceProvider.GetRequiredService<SiNet.Infrastructure.Sql.Services.DevTools.SqlWorkflowSeedService>();
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

                // ═══════════════════════════════════════════════════════════════════
                // STALLED WORKFLOW WATCHDOG (native): periodic background safety net.
                // Runs every 5 minutes after a 2-minute initial delay. Detects active
                // workflows with all tasks closed but no stage advance, then re-invokes
                // the native IWorkflowCommandService to unstick them. Uses the native
                // StalledWorkflowWatchdog registered by AddSiNetProcessBackbone().
                // ═══════════════════════════════════════════════════════════════════
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromMinutes(2), _appShutdownCts.Token);

                        while (!_appShutdownCts.Token.IsCancellationRequested)
                        {
                            try
                            {
                                var watchdog = ServiceProvider.GetRequiredService<SiNet.Infrastructure.Sql.Services.Workflow.StalledWorkflowWatchdog>();
                                var stalled = await watchdog.DetectStalledAsync(_appShutdownCts.Token);

                                if (stalled.Count > 0)
                                {
                                    Log.Information("[Watchdog] Detected {Count} stalled workflow(s). Attempting recovery...", stalled.Count);
                                    var systemUserId = SiNetSQL.Services.CurrentUserContext.Instance.CurrentUserId ?? 0;
                                    var recovered = await watchdog.AttemptRecoveryAsync(stalled, systemUserId, _appShutdownCts.Token);
                                    Log.Information("[Watchdog] Recovery complete: {Recovered}/{Total} workflows unstuck.", recovered, stalled.Count);
                                }
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception sweepEx)
                            {
                                Log.Error(sweepEx, "[Watchdog] Sweep iteration failed (non-fatal).");
                            }

                            await Task.Delay(TimeSpan.FromMinutes(5), _appShutdownCts.Token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected on shutdown
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "[Watchdog] Background watchdog loop terminated unexpectedly.");
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

#if DEBUG
        /// <summary>
        /// DEBUG-only: shows <see cref="Dialogs.DebugTools.DebugAuthorizationRoleSelectorWindow"/> when
        /// <c>EnableAuthorizationTestMode</c> is true in app settings. Temporarily mutates the current
        /// user's <c>SIUser</c> row for manual role testing — not compiled in Release builds.
        /// </summary>
        private static void RunDebugAuthorizationRoleSelector()
        {
            if (AppSettings?.EnableAuthorizationTestMode != true)
            {
                return;
            }

            try
            {
                var dbContextFactory = ServiceProvider.GetRequiredService<IDbContextFactory<SiNetSQL.Data.SiNetSQLDbContext>>();
                using var context = dbContextFactory.CreateDbContext();

                var selectorWindow = new SiNetProjectManagerV2.Dialogs.DebugTools.DebugAuthorizationRoleSelectorWindow(context);
                selectorWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to run Debug Authorization Role Selector.");
            }
        }
#endif

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

        private static async Task ApplyNewSystemThemeFromSavedSettingsAsync()
        {
            try
            {
                var initializer = ServiceProvider.GetRequiredService<SiNet.App.Wpf.Theme.ThemeStartupInitializer>();
                await initializer.ApplySavedThemeAsync().ConfigureAwait(true);
                AppLogger.Debug("[Theme] Applied saved user appearance to Application resources.");
            }
            catch (Exception themeEx)
            {
                Log.Warning(themeEx, "Theme startup apply failed. Default theme resources remain active.");
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
        /// Legacy launch only: splash then <see cref="MainWindow"/>. Mode selection happens earlier in
        /// <see cref="SiNet.App.Wpf.Shell.StartupModeSelectionWindow"/> (see <c>docs/APP_SHELL.md</c> §3).
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

                // Reload settings from DB and retry with the updated title. Still synchronous: this
                // recovery path is reached from the Legacy default-project flow, which has not been
                // converted to async (see docs/P2-TECH-DEBT-BACKLOG.md).
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
