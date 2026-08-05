using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Autodesk;
using SiNet.App.Wpf.Admin.Diagnostics;
using SiNet.App.Wpf.Admin.FileCatalog;
using SiNet.App.Wpf.Admin.MasterPlan;
using SiNet.App.Wpf.Admin.MasterPlan.Reports;
using SiNet.App.Wpf.Admin.Permissions;
using SiNet.App.Wpf.Admin.ProjectTypeWorkflowPolicy;
using SiNet.App.Wpf.Admin.Security;
using SiNet.App.Wpf.Admin.Settings;
using SiNet.App.Wpf.Admin.SystemStatus;
using SiNet.App.Wpf.Admin.UserGroups;
using SiNet.App.Wpf.Admin.Users;
using SiNet.App.Wpf.Admin.WorkflowOps;
using SiNet.App.Wpf.DevTools;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Projects.Dashboard;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.App.Wpf.Surfaces.Email;
using SiNet.App.Wpf.Surfaces.Inspection;
using SiNet.App.Wpf.Surfaces.ProjectWork;
using SiNet.App.Wpf.Surfaces.Tasks;
using SiNet.App.Wpf.Surfaces.Workflow;
using SiNet.App.Wpf.Theme;
using SiNet.Application.Identity;
using SiNet.Application.DevTools;
using SiNet.Application.Diagnostics; // TEMP WF-DEBUG
using SiNet.Application.Projects;
using SiNet.Application.Runtime;
using SiNet.Application.Workflow;

namespace SiNet.App.Wpf.Shell;

/// <summary>
/// Builds the clean New System shell (<see cref="NewShellWindow"/>) and its <b>migrated-only</b> menu
/// (see <c>docs/APP_SHELL.md</c> §6/§7). The host calls <see cref="CreateShellAsync"/> in New system
/// mode instead of opening the legacy main window.
/// </summary>
public interface INewShellFactory
{
    /// <summary>
    /// Creates a fully wired <see cref="NewShellWindow"/>: header + current user + shared Project
    /// Selector + a top menu whose items open migrated surfaces. Email is hosted in-shell via
    /// <see cref="IEmailSurfaceHost"/>; other surfaces may still open as windows. No legacy menu.
    /// <para>
    /// Asynchronous because building the menu needs the current user profile and one authorization
    /// decision per migrated surface. Awaiting them keeps the startup thread free instead of blocking
    /// it on the authorization port.
    /// </para>
    /// </summary>
    Task<Window> CreateShellAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="INewShellFactory"/>. Resolves migrated surfaces lazily from the application
/// <see cref="IServiceProvider"/> so opening the shell does not construct legacy windows/menus.
/// </summary>
public sealed class NewShellFactory(IServiceProvider services) : INewShellFactory
{
    private readonly IServiceProvider _services = services ?? throw new ArgumentNullException(nameof(services));

    /// <inheritdoc />
    public async Task<Window> CreateShellAsync(CancellationToken cancellationToken = default)
    {
        ThemeResourceLoader.EnsureApplicationResourcesMerged();

        var currentUserDisplay = await ResolveCurrentUserDisplayAsync(cancellationToken).ConfigureAwait(true);
        var currentProject = _services.GetService<ICurrentProjectContext>();

        var menu = await BuildMigratedOnlyMenuAsync(cancellationToken).ConfigureAwait(true);
        Action? openNewProject = null;
        if (_services.GetService<IProjectCreateDialogFactory>() is { } projectCreateFactory
            && await CanAccessFeatureAsync(AppFeatureCodes.ProjectCreate, cancellationToken).ConfigureAwait(true))
        {
            openNewProject = () => _ = OpenNewProjectAsync(projectCreateFactory, cancellationToken);
        }

        var runtimeStatus = _services.GetService<IRuntimeSubsystemStatusService>();
        // Kick the first full probe (and the 5-minute loop) as soon as the shell exists — do not wait
        // for the user to open «מצב מערכת» (docs/SYSTEM_HEALTH.md §2.6).
        runtimeStatus?.StartPeriodicRefresh();

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

        var emailSurfaceHost = _services.GetService<IEmailSurfaceHost>();
        return new NewShellWindow(viewModel, emailSurfaceHost);
    }

    /// <summary>
    /// Builds a hierarchical shell menu (top groups + submenus), mirroring the legacy
    /// <c>MainWindow</c> layout. Only groups that have at least one available child are included.
    /// </summary>
    private async Task<IReadOnlyList<NewShellMenuItem>> BuildMigratedOnlyMenuAsync(
        CancellationToken cancellationToken = default)
    {
        var top = new List<NewShellMenuItem>();

        // ── פרויקטים ותבניות ──────────────────────────────────────────────
        var projects = new List<NewShellMenuItem>();
        if (_services.GetService<IProjectCreateDialogFactory>() is { } projectCreateFactory
            && await CanAccessFeatureAsync(AppFeatureCodes.ProjectCreate, cancellationToken).ConfigureAwait(true))
        {
            projects.Add(new NewShellMenuItem(
                "פתיחת פרויקט חדש",
                () => _ = OpenNewProjectAsync(projectCreateFactory, cancellationToken),
                "יצירת פרויקט חדש עם מקום, חברה, איש קשר וסוגי פרויקט"));
        }

        if (await CanAccessFeatureAsync(AppFeatureCodes.ShellOpenProjectsDashboard, cancellationToken).ConfigureAwait(true))
        {
            projects.Add(new NewShellMenuItem(
                "ריכוז פרויקטים",
                OpenNativeProjectsDashboard,
                "טבלת סקירה: סטטוס, סוגי פרויקט, תהליכים ומשימות פתוחים"));
        }

        if (_services.GetService<IEmailSurfaceHost>() is { } emailSurfaceHost
            && await CanAccessFeatureAsync(AppFeatureCodes.ShellOpenEmailSurface, cancellationToken).ConfigureAwait(true))
        {
            projects.Add(new NewShellMenuItem(
                "מיילים",
                () => emailSurfaceHost.Show(),
                "פתיחת מסך דוא\"ל בתוך האפליקציה (נשמר בזיכרון)"));
        }

        if (_services.GetService<ProjectWorkSurfaceHost>() is { } projectWorkHost
            && await CanAccessFeatureAsync(AppFeatureCodes.ShellOpenProjectWorkSurface, cancellationToken).ConfigureAwait(true))
        {
            projects.Add(new NewShellMenuItem(
                "בעבודה 2",
                () => _ = OpenProjectWorkBrowseAsync(projectWorkHost),
                "סביבת קבצי פרויקט בתוך האפליקציה (נשמרת בזיכרון)"));
        }

        AddGroupIfAny(top, "פרויקטים ותבניות", projects);

        // ── משימות ────────────────────────────────────────────────────────
        var tasks = new List<NewShellMenuItem>();
        // Task Workbench (לוח משימות) — personal Quick/Medium/Long queues
        if (_services.GetService<ITaskPanelReadOnlyWindowFactory>() is { } taskPanelFactory
            && await CanAccessFeatureAsync(AppFeatureCodes.ShellOpenTaskPanelReadOnly, cancellationToken).ConfigureAwait(true))
        {
            tasks.Add(new NewShellMenuItem(
                "לוח משימות",
                () => taskPanelFactory.ShowOrActivate(),
                "תורים אישיים Quick / Medium / Long"));
        }

        if (_services.GetService<IInspectionWindowFactory>() is { } inspectionFactory
            && await CanAccessFeatureAsync(AppFeatureCodes.ShellOpenInspectionSurface, cancellationToken).ConfigureAwait(true))
        {
            tasks.Add(new NewShellMenuItem(
                "דוחות ביקורת",
                () => ShowWindow(inspectionFactory.Create()),
                "חלון בדיקת דוח (מערכת חדשה)"));
        }

        if (_services.GetService<IWorkflowClosedViewerWindowFactory>() is { } workflowViewerFactory
            && await CanAccessFeatureAsync(AppFeatureCodes.ShellOpenWorkflowClosedViewer, cancellationToken).ConfigureAwait(true))
        {
            tasks.Add(new NewShellMenuItem(
                "צפייה בתהליכים (סגור)",
                () => ShowWindow(workflowViewerFactory.Create()),
                "קנבס ויזואלי לקריאה בלבד — מקרא + תבניות, ללא שמירה"));
        }

#if DEBUG
        if (await CanAccessFeatureAsync(AppFeatureCodes.ShellOpenInspectionSurface, cancellationToken).ConfigureAwait(true))
        {
            tasks.Add(new NewShellMenuItem(
                "ביקורת (מעטפת — DEBUG)",
                OpenInspectionShell,
                "Developer harness — not for production users"));
        }
#endif

        AddGroupIfAny(top, "משימות", tasks);

        // ── משתמשים והרשאות ───────────────────────────────────────────────
        var users = new List<NewShellMenuItem>();
        if (await CanAccessFeatureAsync(AppFeatureCodes.UsersManage, cancellationToken).ConfigureAwait(true))
        {
            users.Add(new NewShellMenuItem(
                "ניהול משתמשים",
                OpenNativeUserList,
                "רשימת משתמשים (מערכת חדשה)"));
            users.Add(new NewShellMenuItem(
                "הוספת משתמש",
                OpenNativeAddUser,
                "הוספת משתמש חדש (מערכת חדשה)"));
        }

        if (await CanAccessFeatureAsync(AppFeatureCodes.ActionPermissionsManage, cancellationToken).ConfigureAwait(true))
        {
            users.Add(new NewShellMenuItem(
                "הרשאות פעולה",
                OpenNativeActionPermissions,
                "ניהול הרשאות פעולה (מערכת חדשה)"));
        }

        AddGroupIfAny(top, "משתמשים והרשאות", users);

        // ── דוחות MasterPlan (R01–R03) ───────────────────────────────────
        // R03 in-app preview is available to every authenticated user (self-only when not management).
        // R01/R02 + Sheets export remain ReportsManagement.
        var reports = new List<NewShellMenuItem>();
        if (HasAuthenticatedUser())
        {
            reports.Add(new NewShellMenuItem(
                "R03 — השוואת נוכחות",
                OpenNativeR03Report,
                "נוכחות מול דיווח — טבלה באפליקציה / Google Sheets להנהלה"));
        }

        if (await CanAccessFeatureAsync(AppFeatureCodes.ReportsManagement, cancellationToken).ConfigureAwait(true))
        {
            reports.Add(new NewShellMenuItem(
                "R01 — סיכום שעות",
                OpenNativeR01Report,
                "תיק פרויקטים → Google Sheets"));
            reports.Add(new NewShellMenuItem(
                "R02 — שעות עבודה",
                OpenNativeR02Report,
                "שעות עבודה → Google Sheets"));
        }

        AddGroupIfAny(top, "דוחות", reports);

        // ── מנהלה (הגדרות + כלי ניהול + מצב מערכת) ────────────────────────
        var admin = new List<NewShellMenuItem>();
        if (HasAuthenticatedUser())
        {
            admin.Add(new NewShellMenuItem(
                "הגדרות אישיות",
                OpenNativePersonalSettings,
                "הגדרות אישיות (JSON מקומי)"));
        }

        if (await CanAccessFeatureAsync(AppFeatureCodes.SystemSettingsWrite, cancellationToken).ConfigureAwait(true))
        {
            admin.Add(new NewShellMenuItem(
                "הגדרות מערכת",
                OpenNativeSystemSettings,
                "הגדרות מערכת / שרת (Administrator)"));
            admin.Add(new NewShellMenuItem(
                "הקצאות משתמשים / קבוצות",
                OpenNativeUserGroups,
                "ניהול קבוצות מבצעים ומשתמש ברירת מחדל ל-workflow"));
            admin.Add(new NewShellMenuItem(
                "מפתחות וסודות",
                OpenNativeSecretSetup,
                "הגדרת מפתחות וסודות (Credential Vault)"));
            admin.Add(new NewShellMenuItem(
                "מיפוי MasterPlan",
                OpenNativeMasterPlanMapping,
                "מיפוי חברות ואנשי קשר MasterPlan ↔ SiNet"));
            admin.Add(new NewShellMenuItem(
                "סטטוס ACC",
                OpenNativeAccControlPlaneStatus,
                "מצב ריצה / browse / reconciliation של ACC"));
        }

        if (await CanAccessFeatureAsync(AppFeatureCodes.ShellOpenFileCatalogAdmin, cancellationToken).ConfigureAwait(true))
        {
            admin.Add(new NewShellMenuItem(
                "ניהול קבצים",
                OpenNativeFileCatalog,
                "קטלוג הגדרות קבצים ותיקיות (אדמין)"));
        }

        if (await CanAccessFeatureAsync(AppFeatureCodes.ShellOpenWorkflowOpsDashboard, cancellationToken).ConfigureAwait(true))
        {
            admin.Add(new NewShellMenuItem(
                "בריאות תהליכים",
                OpenNativeWorkflowOpsDashboard,
                "דשבורד תפעולי למופעי workflow (צפייה + קידום/ביטול/הפעלה)"));
        }

        if (await CanAccessFeatureAsync(AppFeatureCodes.ShellOpenProjectTypeWorkflowPolicy, cancellationToken).ConfigureAwait(true))
        {
            admin.Add(new NewShellMenuItem(
                "מדיניות סוג↔תהליך",
                OpenNativeProjectTypeWorkflowPolicy,
                "מיפוי סוג פרויקט (JobType) להגדרת תהליך ברירת מחדל"));
        }

        if (HasAuthenticatedUser())
        {
            admin.Add(new NewShellMenuItem(
                "מצב מערכת",
                OpenNativeSystemStatus,
                "מצב מערכות־משנה ועבודת רקע (מערכת חדשה)"));

            // DEV-010: a user whose machine keeps crashing must be able to produce the report himself.
            admin.Add(new NewShellMenuItem(
                "דוח קריסות תחנה",
                OpenNativeWorkstationCrashReport,
                "דוח קריסות Civil 3D ואירועי מכונה מיומן האירועים המקומי"));
        }

#if DEBUG
        var devTools = await BuildDevToolsMenuItemsAsync(cancellationToken).ConfigureAwait(true);
        if (devTools.Count > 0)
        {
            admin.Add(NewShellMenuItem.Group("כלי פיתוח", devTools));
        }
#endif

        AddGroupIfAny(top, "מנהלה", admin);

        return top;
    }

    private static void AddGroupIfAny(List<NewShellMenuItem> top, string title, List<NewShellMenuItem> children)
    {
        if (children.Count == 0)
        {
            return;
        }

        top.Add(NewShellMenuItem.Group(title, children));
    }

    /// <summary>
    /// Fail-closed feature check via the Application authorization port (host supplies legacy adapter).
    /// </summary>
    private async Task<bool> CanAccessFeatureAsync(string featureCode, CancellationToken cancellationToken)
    {
        var authorization = _services.GetService<IAuthorizationQueryService>();
        if (authorization is null)
        {
            return false;
        }

        try
        {
            return await authorization
                .CanCurrentUserAccessFeatureAsync(featureCode, cancellationToken)
                .ConfigureAwait(true);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private async Task OpenNewProjectAsync(
        IProjectCreateDialogFactory factory,
        CancellationToken cancellationToken)
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

            var summary = await query.GetProjectAsync(projectId, cancellationToken).ConfigureAwait(true);
            if (summary is not null)
            {
                await context.SetCurrentProjectAsync(summary, cancellationToken).ConfigureAwait(true);
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

    private static async Task OpenProjectWorkBrowseAsync(ProjectWorkSurfaceHost host)
    {
        try
        {
            var opened = await host.TryOpenBrowseAsync().ConfigureAwait(true);
            if (!opened)
            {
                MessageBox.Show(
                    "לא ניתן לפתוח את סביבת העבודה בתוך המעטפת.",
                    "בעבודה 2",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            MessageBox.Show(
                $"שגיאה בפתיחת בעבודה 2: {ex.Message}",
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

    private void OpenNativeMasterPlanMapping()
    {
        ThemeResourceLoader.EnsureApplicationResourcesMerged();
        try
        {
            var window = _services.GetRequiredService<MasterPlanMappingWindow>();
            ShowWindow(window);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            MessageBox.Show(
                $"שגיאה בפתיחת מיפוי MasterPlan: {ex.Message}",
                "שגיאה",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenNativeR01Report()
    {
        ThemeResourceLoader.EnsureApplicationResourcesMerged();
        try
        {
            ShowWindow(_services.GetRequiredService<R01ReportWindow>());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בפתיחת R01: {ex.Message}", "שגיאה", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenNativeR02Report()
    {
        ThemeResourceLoader.EnsureApplicationResourcesMerged();
        try
        {
            ShowWindow(_services.GetRequiredService<R02ReportWindow>());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בפתיחת R02: {ex.Message}", "שגיאה", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenNativeR03Report()
    {
        ThemeResourceLoader.EnsureApplicationResourcesMerged();
        try
        {
            ShowWindow(_services.GetRequiredService<R03ReportWindow>());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"שגיאה בפתיחת R03: {ex.Message}", "שגיאה", MessageBoxButton.OK, MessageBoxImage.Error);
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

    private void OpenNativeFileCatalog()
    {
        try
        {
            var window = _services.GetRequiredService<FileCatalogWindow>();
            ShowWindow(window);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            MessageBox.Show(
                $"שגיאה בפתיחת ניהול קבצים: {ex.Message}",
                "שגיאה",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            throw;
        }
    }

    private void OpenNativeProjectTypeWorkflowPolicy()
    {
        try
        {
            var window = _services.GetRequiredService<ProjectTypeWorkflowPolicyWindow>();
            ShowWindow(window);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            MessageBox.Show(
                $"שגיאה בפתיחת מדיניות סוג↔תהליך: {ex.Message}",
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

    private void OpenNativeWorkstationCrashReport()
    {
        ThemeResourceLoader.EnsureApplicationResourcesMerged();
        try
        {
            var window = _services.GetRequiredService<WorkstationCrashReportWindow>();
            ShowWindow(window);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            MessageBox.Show(
                $"שגיאה בפתיחת דוח קריסות תחנה: {ex.Message}",
                "שגיאה",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            throw;
        }
    }

    private void OpenNativeWorkflowOpsDashboard()
    {
        ThemeResourceLoader.EnsureApplicationResourcesMerged();
        try
        {
            var window = _services.GetRequiredService<WorkflowOpsDashboardWindow>();
            ShowWindow(window);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            MessageBox.Show(
                $"שגיאה בפתיחת בריאות תהליכים: {ex.Message}",
                "שגיאה",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            throw;
        }
    }

    private void OpenNativeProjectsDashboard()
    {
        ThemeResourceLoader.EnsureApplicationResourcesMerged();
        try
        {
            var window = _services.GetRequiredService<ProjectsDashboardWindow>();
            ShowWindow(window);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            MessageBox.Show(
                $"שגיאה בפתיחת ריכוז פרויקטים: {ex.Message}",
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

    private void OpenNativeUserGroups()
    {
        ThemeResourceLoader.EnsureApplicationResourcesMerged();
        try
        {
            var factory = _services.GetRequiredService<IUserGroupsWindowFactory>();
            ShowDialog(factory.Create());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            MessageBox.Show(
                $"שגיאה בפתיחת הקצאות משתמשים: {ex.Message}",
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
            ThemeWindowChrome.ApplyThemedWindowBackground(window);
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

    private async Task<List<NewShellMenuItem>> BuildDevToolsMenuItemsAsync(CancellationToken cancellationToken)
    {
        var items = new List<NewShellMenuItem>();
        if (_services.GetService<IDevDataResetService>() is null
            && _services.GetService<IStaticSeedService>() is null)
        {
            return items;
        }

        if (!await CanAccessFeatureAsync(AppFeatureCodes.DevToolsReset, cancellationToken).ConfigureAwait(true))
        {
            return items;
        }

        var coordinator = new DevToolsCoordinator(_services);
        Window? Owner() => System.Windows.Application.Current?.MainWindow;

        items.Add(new NewShellMenuItem(
            "איפוס נתוני פיתוח",
            () => _ = coordinator.RunResetWithDialogAsync(Owner()),
            "מוחק נתוני migration ומריץ seed (New System — לא legacy DevDataResetService)"));

        items.Add(new NewShellMenuItem(
            "טעינת Seed בסיסי",
            () => _ = coordinator.RunCoreSeedAsync(Owner()),
            "Task static + mappings + workflow seed"));

        items.Add(new NewShellMenuItem(
            "בדיקת Seed",
            () => _ = coordinator.RunSeedBaselineVerifyAsync(Owner()),
            "בדיקה לקריאה בלבד: האם Codes הנדרשים של Seed בסיסי עדיין קיימים"));

        items.Add(new NewShellMenuItem(
            "טעינת משימות דמו",
            () => _ = coordinator.RunDemoTaskSeedAsync(Owner()),
            "משימות DEBUG בשלושה buckets ל-Task Panel read-only"));

        // TEMP WF-DEBUG — manual watchdog trigger. Remove with the rest of WF-DEBUG instrumentation.
        items.Add(new NewShellMenuItem(
            "הרץ Watchdog עכשיו",
            () => _ = RunWatchdogNowAsync(),
            "סורק Workflows תקועים ומנסה שחזור"));

        return items;
    }

    // TEMP WF-DEBUG
    private async Task RunWatchdogNowAsync()
    {
        if (_services.GetService<IWorkflowRecoveryService>() is not { } workflowRecovery)
        {
            MessageBox.Show(
                "IWorkflowRecoveryService אינו רשום ב-DI.",
                "Watchdog",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var userId = _services.GetService<ICurrentUserContext>()?.UserId ?? 0;

        try
        {
            var stalled = await workflowRecovery
                .DetectStalledAsync(CancellationToken.None)
                .ConfigureAwait(true);
            var recoveredCount = await workflowRecovery
                .AttemptRecoveryAsync(stalled, userId, CancellationToken.None)
                .ConfigureAwait(true);

            WorkflowDebugTrace.Step("Watchdog.DevTrigger",
                $"user={userId} detectedStalled={stalled.Count} recovered={recoveredCount}");

            MessageBox.Show(
                $"Watchdog הושלם.\n\nזוהו תקועים: {stalled.Count}\nשוחזרו: {recoveredCount}\n\n" +
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

    private async Task<string?> ResolveCurrentUserDisplayAsync(CancellationToken cancellationToken)
    {
        var profileService = _services.GetService<ICurrentUserProfileService>();
        if (profileService is null)
        {
            return null;
        }

        var profile = await profileService.GetCurrentUserAsync(cancellationToken).ConfigureAwait(true);
        return CurrentUserProfileDisplay.Format(profile);
    }
}
