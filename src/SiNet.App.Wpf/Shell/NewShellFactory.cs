using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Autodesk;
using SiNet.App.Wpf.Admin.Permissions;
using SiNet.App.Wpf.Admin.Security;
using SiNet.App.Wpf.Admin.Settings;
using SiNet.App.Wpf.Admin.SystemStatus;
using SiNet.App.Wpf.Admin.Users;
using SiNet.App.Wpf.DevTools;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.App.Wpf.Surfaces.Email;
using SiNet.App.Wpf.Surfaces.Inspection;
using SiNet.App.Wpf.Surfaces.Tasks;
using SiNet.App.Wpf.Surfaces.Workflow;
using SiNet.App.Wpf.Theme;
using SiNet.Application.Identity;
using SiNet.Application.DevTools;
using SiNet.Application.Diagnostics; // TEMP WF-DEBUG
using SiNet.Application.Projects;
using SiNet.Application.Runtime;
using SiNet.Infrastructure.Sql.Services.Workflow; // TEMP WF-DEBUG (Run Watchdog now dev trigger)

namespace SiNet.App.Wpf.Shell;

/// <summary>
/// Builds the clean New System shell (<see cref="NewShellWindow"/>) and its <b>migrated-only</b> menu
/// (see <c>docs/APP_SHELL.md</c> §6/§7). The host calls <see cref="CreateShell"/> in New system mode
/// instead of opening the legacy main window.
/// </summary>
public interface INewShellFactory
{
    /// <summary>
    /// Creates a fully wired <see cref="NewShellWindow"/>: header + current user + shared Project
    /// Selector + a top menu whose items open migrated surfaces. Email is hosted in-shell via
    /// <see cref="IEmailSurfaceHost"/>; other surfaces may still open as windows. No legacy menu.
    /// </summary>
    Window CreateShell();
}

/// <summary>
/// Default <see cref="INewShellFactory"/>. Resolves migrated surfaces lazily from the application
/// <see cref="IServiceProvider"/> so opening the shell does not construct legacy windows/menus.
/// </summary>
public sealed class NewShellFactory(IServiceProvider services) : INewShellFactory
{
    private readonly IServiceProvider _services = services ?? throw new ArgumentNullException(nameof(services));

    /// <inheritdoc />
    public Window CreateShell()
    {
        ThemeResourceLoader.EnsureApplicationResourcesMerged();

        var currentUserDisplay = ResolveCurrentUserDisplay();
        var currentProject = _services.GetService<ICurrentProjectContext>();

        var menu = BuildMigratedOnlyMenu();
        Action? openNewProject = null;
        if (_services.GetService<IProjectCreateDialogFactory>() is { } projectCreateFactory
            && CanAccessFeature(AppFeatureCodes.ProjectCreate))
        {
            openNewProject = () => OpenNewProject(projectCreateFactory);
        }

        var runtimeStatus = _services.GetService<IRuntimeSubsystemStatusService>();
        Action? openSystemStatus = HasAuthenticatedUser()
            ? OpenNativeSystemStatus
            : null;

        var viewModel = new NewShellViewModel(
            menu,
            currentUserDisplay,
            currentProject,
            openNewProject: openNewProject,
            runtimeStatus: runtimeStatus,
            openSystemStatus: openSystemStatus);

        // Attach shell content navigation so hosted surfaces (email) can NavigateTo the content host.
        if (_services.GetService<IShellContentHost>() is { } contentHost)
        {
            contentHost.Attach(viewModel);
        }

        var selectorView = TryCreateProjectSelector();
        var emailSurfaceHost = _services.GetService<IEmailSurfaceHost>();

        return new NewShellWindow(viewModel, selectorView, emailSurfaceHost);
    }

    /// <summary>
    /// Builds the shell menu from surfaces that ALREADY exist in the refactored stack. Nothing legacy
    /// is scanned or copied; each item opens its surface through the same DI/factory the legacy host
    /// uses. Menu availability is resolved via <see cref="IAuthorizationQueryService"/> (not legacy
    /// singletons in this assembly).
    /// </summary>
    private IReadOnlyList<NewShellMenuItem> BuildMigratedOnlyMenu()
    {
        var items = new List<NewShellMenuItem>();

        // Email — hosted inside the main shell as a singleton surface (legacy EmailManagementView cache).
        if (_services.GetService<IEmailSurfaceHost>() is { } emailSurfaceHost
            && CanAccessFeature(AppFeatureCodes.ShellOpenEmailSurface))
        {
            items.Add(new NewShellMenuItem(
                "דוא\"ל",
                () => emailSurfaceHost.Show(),
                "פתיחת מסך דוא\"ל בתוך האפליקציה (נשמר בזיכרון)"));
        }

        // Task Panel — read-only pilot (three personal bucket queues via ITaskQueryService).
        if (_services.GetService<ITaskPanelReadOnlyWindowFactory>() is { } taskPanelFactory
            && CanAccessFeature(AppFeatureCodes.ShellOpenTaskPanelReadOnly))
        {
            items.Add(new NewShellMenuItem(
                "משימות — Task Workbench",
                () => ShowWindow(taskPanelFactory.Create()),
                "תורים אישיים Quick / Medium / Long — צפייה, הוספה ומחיקה בסיסית"));
        }

        // New Project — native create dialog (place / company / job types).
        if (_services.GetService<IProjectCreateDialogFactory>() is { } projectCreateFactory
            && CanAccessFeature(AppFeatureCodes.ProjectCreate))
        {
            items.Add(new NewShellMenuItem(
                "פתיחת פרויקט חדש",
                () => OpenNewProject(projectCreateFactory),
                "יצירת פרויקט חדש עם מקום, חברה, איש קשר וסוגי פרויקט"));
        }

        // Inspection report window (visual clone) — production target surface.
        if (_services.GetService<IInspectionWindowFactory>() is { } inspectionFactory
            && CanAccessFeature(AppFeatureCodes.ShellOpenInspectionSurface))
        {
            items.Add(new NewShellMenuItem(
                "דוחות ביקורת",
                () => ShowWindow(inspectionFactory.Create()),
                "חלון בדיקת דוח (מערכת חדשה)"));
        }

        // Workflow closed-world viewer — native App.Wpf surface (catalog-bound, no save).
        if (_services.GetService<IWorkflowClosedViewerWindowFactory>() is { } workflowViewerFactory
            && CanAccessFeature(AppFeatureCodes.ShellOpenWorkflowClosedViewer))
        {
            items.Add(new NewShellMenuItem(
                "תהליכים — קנבס (צפייה)",
                () => ShowWindow(workflowViewerFactory.Create()),
                "קנבס ויזואלי לקריאה בלבד — מקרא + תבניות, ללא שמירה"));
        }

#if DEBUG
        // InspectionShellView is a developer harness only — NOT part of the limited production pilot.
        // Release builds must not expose it in the shell menu. Dev entry points remain in V2 legacy
        // MainWindow admin preview (OpenInspectionFromTask_Click) and standalone harness.
        if (CanAccessFeature(AppFeatureCodes.ShellOpenInspectionSurface))
        {
            items.Add(new NewShellMenuItem(
                "ביקורת (מעטפת — DEBUG)",
                OpenInspectionShell,
                "Developer harness — not for production users"));
        }
#endif

#if DEBUG
        AppendDevToolsMenuItems(items);
#endif

        // Native user admin — App.Wpf surfaces + Infrastructure.Sql (see docs/NEW_SYSTEM_BOUNDARY.md).
        if (CanAccessFeature(AppFeatureCodes.UsersManage))
        {
            items.Add(new NewShellMenuItem(
                "ניהול משתמשים",
                OpenNativeUserList,
                "רשימת משתמשים (מערכת חדשה)"));

            items.Add(new NewShellMenuItem(
                "הוספת משתמש",
                OpenNativeAddUser,
                "הוספת משתמש חדש (מערכת חדשה)"));
        }

        if (CanAccessFeature(AppFeatureCodes.ActionPermissionsManage))
        {
            items.Add(new NewShellMenuItem(
                "הרשאות פעולה",
                OpenNativeActionPermissions,
                "ניהול הרשאות פעולה (מערכת חדשה)"));
        }

        if (CanAccessFeature(AppFeatureCodes.SystemSettingsWrite))
        {
            items.Add(new NewShellMenuItem(
                "מפתחות וסודות",
                OpenNativeSecretSetup,
                "הגדרת מפתחות וסודות (Credential Vault)"));

            items.Add(new NewShellMenuItem(
                "סטטוס ACC",
                OpenNativeAccControlPlaneStatus,
                "מצב ריצה / browse / reconciliation של ACC"));
        }

        if (HasAuthenticatedUser())
        {
            items.Add(new NewShellMenuItem(
                "מצב מערכת",
                OpenNativeSystemStatus,
                "מצב מערכות־משנה ועבודת רקע (מערכת חדשה)"));

            items.Add(new NewShellMenuItem(
                "הגדרות אישיות",
                OpenNativePersonalSettings,
                "הגדרות אישיות (JSON מקומי)"));
        }

        if (CanAccessFeature(AppFeatureCodes.SystemSettingsWrite))
        {
            items.Add(new NewShellMenuItem(
                "הגדרות מערכת",
                OpenNativeSystemSettings,
                "הגדרות מערכת / שרת (Administrator)"));
        }

        return items;
    }

    /// <summary>
    /// Fail-closed feature check via the Application authorization port (host supplies legacy adapter).
    /// </summary>
    private bool CanAccessFeature(string featureCode)
    {
        var authorization = _services.GetService<IAuthorizationQueryService>();
        if (authorization is null)
        {
            return false;
        }

        try
        {
            return RunSync(() => authorization.CanCurrentUserAccessFeatureAsync(featureCode));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private void OpenNewProject(IProjectCreateDialogFactory factory)
    {
        try
        {
            var owner = System.Windows.Application.Current?.MainWindow;
            var result = factory.ShowDialog(owner);
            if (!result.Confirmed || result.ProjectId is not int projectId)
            {
                return;
            }

            var context = _services.GetService<ICurrentProjectContext>();
            var query = _services.GetService<IProjectQueryService>();
            if (context is null || query is null)
            {
                return;
            }

            var summary = RunSync(() => query.GetProjectAsync(projectId));
            if (summary is not null)
            {
                RunSync(() => context.SetCurrentProjectAsync(summary));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            MessageBox.Show(
                $"שגיאה בפתיחת פרויקט חדש: {ex.Message}",
                "שגיאה",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenNativeUserList()
    {
        ThemeResourceLoader.EnsureApplicationResourcesMerged();
        try
        {
            var window = _services.GetRequiredService<UserListWindow>();
            ShowWindow(window);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            MessageBox.Show(
                $"שגיאה בפתיחת ניהול משתמשים: {ex.Message}",
                "שגיאה",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            throw;
        }
    }

    private void OpenNativeAddUser()
    {
        try
        {
            var window = _services.GetRequiredService<AddUserDialogWindow>();
            ShowDialog(window);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            MessageBox.Show(
                $"שגיאה בפתיחת הוספת משתמש: {ex.Message}",
                "שגיאה",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            throw;
        }
    }

    private void OpenNativeActionPermissions()
    {
        try
        {
            var window = _services.GetRequiredService<ActionPermissionsWindow>();
            ShowWindow(window);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            MessageBox.Show(
                $"שגיאה בפתיחת הרשאות פעולה: {ex.Message}",
                "שגיאה",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            throw;
        }
    }

    private void OpenNativeSecretSetup()
    {
        try
        {
            var window = _services.GetRequiredService<SecretSetupWindow>();
            ShowDialog(window);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            MessageBox.Show(
                $"שגיאה בפתיחת מפתחות וסודות: {ex.Message}",
                "שגיאה",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            throw;
        }
    }

    private void OpenNativeAccControlPlaneStatus()
    {
        try
        {
            var window = _services.GetRequiredService<AccControlPlaneStatusWindow>();
            ShowWindow(window);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            MessageBox.Show(
                $"שגיאה בפתיחת סטטוס ACC: {ex.Message}",
                "שגיאה",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            throw;
        }
    }

    private void OpenNativeSystemStatus()
    {
        ThemeResourceLoader.EnsureApplicationResourcesMerged();
        try
        {
            var window = _services.GetRequiredService<SystemStatusWindow>();
            ShowWindow(window);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            MessageBox.Show(
                $"שגיאה בפתיחת מצב מערכת: {ex.Message}",
                "שגיאה",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            throw;
        }
    }

    private void OpenNativePersonalSettings()
    {
        ThemeResourceLoader.EnsureApplicationResourcesMerged();
        try
        {
            var factory = _services.GetRequiredService<ISettingsWindowFactory>();
            ShowDialog(factory.CreatePersonal());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            MessageBox.Show(
                $"שגיאה בפתיחת הגדרות אישיות: {ex.Message}",
                "שגיאה",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            throw;
        }
    }

    private void OpenNativeSystemSettings()
    {
        try
        {
            var factory = _services.GetRequiredService<ISettingsWindowFactory>();
            ShowDialog(factory.CreateSystemAdmin());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            MessageBox.Show(
                $"שגיאה בפתיחת הגדרות מערכת: {ex.Message}",
                "שגיאה",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            throw;
        }
    }

    private bool HasAuthenticatedUser()
        => _services.GetService<ICurrentUserContext>()?.UserId is not null;

    private void OpenInspectionShell()
    {
        try
        {
            var shellView = _services.GetRequiredService<InspectionShellView>();
            var window = new Window
            {
                Title = "ביקורת (מעטפת) — מערכת חדשה",
                Content = shellView,
                Width = 900,
                Height = 620,
                FlowDirection = FlowDirection.RightToLeft,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            ShowWindow(window);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            MessageBox.Show(
                $"שגיאה בפתיחת מעטפת הביקורת: {ex.Message}",
                "שגיאה",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            throw;
        }
    }

    private ProjectSelectorView? TryCreateProjectSelector()
    {
        // The selector VM is constructed the same way the Email surface does it (it is not a DI type):
        // resolve the read side + shared context and bind a fresh view model to the reusable control.
        var projectQuery = _services.GetService<IProjectQueryService>();
        var filterOptions = _services.GetService<IProjectFilterOptionsService>();
        var currentProject = _services.GetService<ICurrentProjectContext>();
        if (projectQuery is null || filterOptions is null || currentProject is null)
        {
            return null;
        }

        var selectorViewModel = new ProjectSelectorViewModel(projectQuery, filterOptions, currentProject);
        return new ProjectSelectorView { DataContext = selectorViewModel };
    }

    private void AppendDevToolsMenuItems(List<NewShellMenuItem> items)
    {
        if (_services.GetService<IDevDataResetService>() is null
            && _services.GetService<IStaticSeedService>() is null)
        {
            return;
        }

        if (!CanAccessFeature(AppFeatureCodes.DevToolsReset))
            return;

        var coordinator = new DevToolsCoordinator(_services);
        Window? Owner() => System.Windows.Application.Current?.MainWindow;

        items.Add(new NewShellMenuItem(
            "כלי פיתוח — איפוס נתוני פיתוח",
            () => _ = coordinator.RunResetWithDialogAsync(Owner()),
            "מוחק נתוני migration ומריץ seed (New System — לא legacy DevDataResetService)"));

        items.Add(new NewShellMenuItem(
            "כלי פיתוח — טעינת Seed בסיסי",
            () => _ = coordinator.RunCoreSeedAsync(Owner()),
            "Task static + mappings + workflow seed"));

        items.Add(new NewShellMenuItem(
            "כלי פיתוח — טעינת משימות דמו",
            () => _ = coordinator.RunDemoTaskSeedAsync(Owner()),
            "משימות DEBUG בשלושה buckets ל-Task Panel read-only"));

        // TEMP WF-DEBUG — manual watchdog trigger. The StalledWorkflowWatchdog background loop does NOT
        // run on the New System startup path, so this lets the manual test exercise orphan detection /
        // recovery on demand. Remove together with the rest of the WF-DEBUG instrumentation.
        items.Add(new NewShellMenuItem(
            "כלי פיתוח — הרץ Watchdog עכשיו",
            RunWatchdogNow,
            "סורק Workflows תקועים ומנסה שחזור (StalledWorkflowWatchdog) — כתיבה ל-workflow-manual-debug.log"));
    }

    // TEMP WF-DEBUG
    private void RunWatchdogNow()
    {
        if (_services.GetService<StalledWorkflowWatchdog>() is not { } watchdog)
        {
            MessageBox.Show(
                "StalledWorkflowWatchdog אינו רשום ב-DI.",
                "Watchdog",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var userId = _services.GetService<ICurrentUserContext>()?.UserId ?? 0;

        try
        {
            var (stalledCount, recoveredCount) = RunSync(async () =>
            {
                var stalled = await watchdog.DetectStalledAsync(CancellationToken.None).ConfigureAwait(false);
                var recovered = await watchdog
                    .AttemptRecoveryAsync(stalled, userId, CancellationToken.None)
                    .ConfigureAwait(false);
                return (stalled.Count, recovered);
            });

            WorkflowDebugTrace.Step("Watchdog.DevTrigger",
                $"user={userId} detectedStalled={stalledCount} recovered={recoveredCount}");

            MessageBox.Show(
                $"Watchdog הושלם.\n\nזוהו תקועים: {stalledCount}\nשוחזרו: {recoveredCount}\n\n" +
                $"פירוט מלא: {WorkflowDebugTrace.FilePath}",
                "Watchdog",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WorkflowDebugTrace.Step("Watchdog.DevTrigger", $"FAILED: {ex.Message}");
            MessageBox.Show(
                $"Watchdog נכשל: {ex.Message}",
                "Watchdog",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static void ShowWindow(Window window)
    {
        // "Application" is fully qualified: this project references the SiNet.Application namespace, so
        // an unqualified "Application" would bind there instead of System.Windows.Application.
        if (System.Windows.Application.Current?.MainWindow is { } owner && !ReferenceEquals(owner, window))
        {
            window.Owner = owner;
        }

        window.Show();
    }

    private static void ShowDialog(Window window)
    {
        if (System.Windows.Application.Current?.MainWindow is { } owner && !ReferenceEquals(owner, window))
        {
            window.Owner = owner;
        }

        window.ShowDialog();
    }

    private string? ResolveCurrentUserDisplay()
    {
        var profileService = _services.GetService<ICurrentUserProfileService>();
        if (profileService is null)
        {
            return null;
        }

        // Shell construction is synchronous; profile is in-memory after startup auth.
        var profile = RunSync(() => profileService.GetCurrentUserAsync());
        return CurrentUserProfileDisplay.Format(profile);
    }

    /// <summary>
    /// Bridges an async port into the synchronous shell/menu construction path (menu handlers are
    /// <see cref="Action"/> by contract). Running the operation via <see cref="Task.Run{TResult}(Func{Task{TResult}})"/>
    /// detaches it from the UI <see cref="System.Threading.SynchronizationContext"/>, so blocking on the
    /// result cannot deadlock regardless of whether the callee uses <c>ConfigureAwait(false)</c> internally.
    /// </summary>
    private static T RunSync<T>(Func<Task<T>> asyncOperation) =>
        Task.Run(asyncOperation).GetAwaiter().GetResult();

    private static void RunSync(Func<Task> asyncOperation) =>
        Task.Run(asyncOperation).GetAwaiter().GetResult();
}
