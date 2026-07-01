using System.Diagnostics;
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
        var sw = Stopwatch.StartNew();

        var currentUser = _services.GetService<ICurrentUserContext>();
        var currentProject = _services.GetService<ICurrentProjectContext>();

        var menu = BuildMigratedOnlyMenu();
        var viewModel = new NewShellViewModel(menu, DescribeUser(currentUser));

        var selectorView = TryCreateProjectSelector();
        var selectorReadyMs = sw.ElapsedMilliseconds;

        var window = new NewShellWindow(viewModel, selectorView, currentProject);

        Debug.WriteLine(
            $"[PERF] NewShellFactory.CreateShell: selector built at {selectorReadyMs} ms, window constructed at " +
            $"{sw.ElapsedMilliseconds} ms (projects load lazily after the window is shown).");

        return window;
    }

    /// <summary>
    /// Builds the shell menu from surfaces that ALREADY exist in the refactored stack. Nothing legacy
    /// is scanned or copied; each item opens its surface through the same DI/factory the legacy host
    /// uses, so behavior matches the reviewed clones.
    /// </summary>
    private IReadOnlyList<NewShellMenuItem> BuildMigratedOnlyMenu()
    {
        var items = new List<NewShellMenuItem>();

        // Email visual clone — opened via the shared factory so it binds the app-wide current project.
        if (_services.GetService<IEmailWindowFactory>() is { } emailFactory)
        {
            items.Add(new NewShellMenuItem(
                "דוא\"ל (שכפול חזותי)",
                () => ShowWindow(emailFactory.Create()),
                "פתיחת מסך הדוא\"ל החדש (שכפול חזותי)"));
        }

        // Inspection shell — the InspectionShellView is a UserControl; host it in a plain window
        // exactly like the legacy host's preview entry point (no task navigation here).
        items.Add(new NewShellMenuItem(
            "ביקורת (מעטפת)",
            OpenInspectionShell,
            "פתיחת מעטפת הביקורת החדשה"));

        // Settings — documented placeholder only for this slice (see docs/APP_SHELL.md §11); shown
        // disabled so the menu communicates what is coming without opening anything.
        items.Add(new NewShellMenuItem(
            "הגדרות",
            static () => { },
            "בקרוב — ראה docs/APP_SHELL.md §11",
            isAvailable: false));

        return items;
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
        var currentProject = _services.GetService<ICurrentProjectContext>();
        if (projectQuery is null || currentProject is null)
        {
            return null;
        }

        var selectorViewModel = new ProjectSelectorViewModel(projectQuery, currentProject);
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

    private static string? DescribeUser(ICurrentUserContext? currentUser)
    {
        // The clean port only carries an id; show it when present so the shell reflects who is signed
        // in without inventing a name. The host may bind a richer display later.
        var userId = currentUser?.UserId;
        return userId.HasValue ? $"משתמש #{userId.Value}" : null;
    }
}
