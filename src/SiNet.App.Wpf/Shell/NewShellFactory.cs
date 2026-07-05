using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Autodesk;
using SiNet.App.Wpf.Admin.Permissions;
using SiNet.App.Wpf.Admin.Security;
using SiNet.App.Wpf.Admin.Settings;
using SiNet.App.Wpf.Admin.Users;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.App.Wpf.Surfaces.Tasks;
using SiNet.App.Wpf.Theme;
using SiNet.Application.Identity;
using SiNet.Application.Projects;

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
    /// Selector + a menu whose items open only migrated surfaces (Email clone, Inspection shell) via
    /// DI/factories. No legacy menu or window is loaded.
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
        var viewModel = new NewShellViewModel(menu, currentUserDisplay, currentProject);

        var selectorView = TryCreateProjectSelector();

        return new NewShellWindow(viewModel, selectorView);
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

        // Email — limited production pilot (read-only). Opened via the shared factory + app-wide project context.
        if (_services.GetService<IEmailWindowFactory>() is { } emailFactory
            && CanAccessFeature(AppFeatureCodes.ShellOpenEmailSurface))
        {
            items.Add(new NewShellMenuItem(
                "דוא\"ל — קריאה בלבד",
                () => ShowWindow(emailFactory.Create()),
                "פתיחת מסך דוא\"ל (Gmail read-only — production pilot)"));
        }

        // Task Panel — read-only pilot (three personal bucket queues via ITaskQueryService).
        if (_services.GetService<ITaskPanelReadOnlyWindowFactory>() is { } taskPanelFactory
            && CanAccessFeature(AppFeatureCodes.ShellOpenTaskPanelReadOnly))
        {
            items.Add(new NewShellMenuItem(
                "משימות — קריאה בלבד",
                () => ShowWindow(taskPanelFactory.Create()),
                "תורים אישיים Quick / Medium / Long — קריאה בלבד"));
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
            return authorization
                .CanCurrentUserAccessFeatureAsync(featureCode)
                .GetAwaiter()
                .GetResult();
        }
        catch (ArgumentException)
        {
            return false;
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
        var profile = profileService.GetCurrentUserAsync().GetAwaiter().GetResult();
        return CurrentUserProfileDisplay.Format(profile);
    }
}
