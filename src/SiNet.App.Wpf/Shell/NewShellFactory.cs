using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shared.Projects;
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
        var currentUserDisplay = ResolveCurrentUserDisplay();
        var currentProject = _services.GetService<ICurrentProjectContext>();

        var menu = BuildMigratedOnlyMenu();
        var viewModel = new NewShellViewModel(menu, currentUserDisplay);

        var selectorView = TryCreateProjectSelector();

        return new NewShellWindow(viewModel, selectorView, currentProject);
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

        // Email visual clone — opened via the shared factory so it binds the app-wide current project.
        if (_services.GetService<IEmailWindowFactory>() is { } emailFactory
            && CanAccessFeature(AppFeatureCodes.ShellOpenEmailSurface))
        {
            items.Add(new NewShellMenuItem(
                "דוא\"ל (שכפול חזותי)",
                () => ShowWindow(emailFactory.Create()),
                "פתיחת מסך הדוא\"ל החדש (שכפול חזותי)"));
        }

        // Inspection shell — the InspectionShellView is a UserControl; host it in a plain window
        // exactly like the legacy host's preview entry point (no task navigation here).
        if (CanAccessFeature(AppFeatureCodes.ShellOpenInspectionSurface))
        {
            items.Add(new NewShellMenuItem(
                "ביקורת (מעטפת)",
                OpenInspectionShell,
                "פתיחת מעטפת הביקורת החדשה"));
        }

        // Action permissions / user management — NOT wired here. Legacy admin windows belong only on the
        // Legacy startup path. New System will get native App.Wpf surfaces + Infrastructure.Sql (see
        // docs/NEW_SYSTEM_BOUNDARY.md).

        // Settings — surface not implemented yet; show disabled. When implemented, gate with
        // AppFeatureCodes.SystemSettingsWrite (Administrator).
        const bool settingsSurfaceImplemented = false;
        var settingsAuthorized = CanAccessFeature(AppFeatureCodes.SystemSettingsWrite);
        items.Add(new NewShellMenuItem(
            "הגדרות",
            static () => { },
            settingsSurfaceImplemented
                ? "הגדרות מערכת"
                : "בקרוב — ראה docs/APP_SHELL.md §11",
            isAvailable: settingsSurfaceImplemented && settingsAuthorized));

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

    private void OpenInspectionShell()
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
