using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Surfaces.Email;
using SiNet.App.Wpf.WorkSurfaces;
using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Shared.Projects;

/// <summary>
/// DI registration for the shell's Project Context (see <c>docs/PROJECTS.md</c> §4/§5).
/// <para>
/// The Project Context makes the shell's Current Project <b>shared application-wide</b> instead of
/// being recreated per window. <see cref="ICurrentProjectContext"/> is registered as a <b>singleton</b>
/// so every window/surface (and the shell title/header) observes the same <c>CurrentProject</c>.
/// </para>
/// <para>
/// The read side (<see cref="IProjectQueryService"/>) is split from the runtime Current Project so the
/// source can vary by host:
/// <list type="bullet">
/// <item><description><see cref="AddSiNetProjectContext"/> — <b>runtime</b>. Registers only the shell
/// pieces and relies on the composition root (<c>AddSiNet()</c> &#8594; <c>AddSiNetProjectQuerySql()</c>)
/// to provide the <b>real, read-only</b> <see cref="IProjectQueryService"/> backed by SQL.</description></item>
/// <item><description><see cref="AddSiNetProjectContextFake"/> — <b>design-time / tests</b>. Also
/// registers the in-memory <see cref="FakeProjectQueryService"/> so the selector has sample data with no
/// DB.</description></item>
/// </list>
/// WPF still binds only to <see cref="ProjectSummaryDto"/>, never to EF entities.
/// </para>
/// </summary>
public static class ProjectContextServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shell Project Context for <b>runtime</b> hosts. Registers the shared runtime
    /// Current Project and the Email window plumbing, but <b>not</b> <see cref="IProjectQueryService"/>:
    /// the real read-only implementation is supplied by the composition root
    /// (<c>AddSiNet()</c> &#8594; <c>AddSiNetProjectQuerySql()</c>). Call this <b>after</b>
    /// <c>AddSiNet(...)</c> so the real read side is in place.
    /// <list type="bullet">
    /// <item><description><see cref="ICurrentProjectContext"/> &#8594; <see cref="InMemoryCurrentProjectContext"/> (<b>singleton</b> — one Current Project for the whole app).</description></item>
    /// <item><description><see cref="EmailWindowViewModel"/> (transient — a fresh view model per window, all sharing the singleton context).</description></item>
    /// <item><description><see cref="IEmailWindowFactory"/> &#8594; <see cref="EmailWindowFactory"/> (singleton — builds wired <see cref="EmailWindowView"/> instances).</description></item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddSiNetProjectContext(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        AddSharedProjectContext(services);

        return services;
    }

    /// <summary>
    /// Registers the shell Project Context for <b>design-time / tests</b>: the same shell pieces as
    /// <see cref="AddSiNetProjectContext"/> plus the in-memory <see cref="FakeProjectQueryService"/> so
    /// the selector shows sample projects without a database.
    /// <list type="bullet">
    /// <item><description><see cref="ICurrentProjectContext"/> &#8594; <see cref="InMemoryCurrentProjectContext"/> (<b>singleton</b>).</description></item>
    /// <item><description><see cref="IProjectQueryService"/> &#8594; <see cref="FakeProjectQueryService"/> (singleton; the fake is stateless).</description></item>
    /// <item><description><see cref="EmailWindowViewModel"/> (transient).</description></item>
    /// <item><description><see cref="IEmailWindowFactory"/> &#8594; <see cref="EmailWindowFactory"/> (singleton).</description></item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddSiNetProjectContextFake(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        AddSharedProjectContext(services);

        // Fake read side (stateless) — safe as a singleton. Only for design-time/tests; runtime uses the
        // real read-only IProjectQueryService from the composition root.
        services.AddSingleton<IProjectQueryService, FakeProjectQueryService>();
        services.AddSingleton<IProjectFilterOptionsService, FakeProjectFilterOptionsService>();

        return services;
    }

    /// <summary>
    /// Shared shell registrations common to both the runtime and fake paths: the singleton runtime
    /// Current Project plus the Email window view model and factory. Deliberately excludes
    /// <see cref="IProjectQueryService"/> so the read side can be chosen per host.
    /// </summary>
    private static void AddSharedProjectContext(IServiceCollection services)
    {
        // Shared runtime Current Project: exactly ONE instance app-wide.
        services.AddSingleton<ICurrentProjectContext, InMemoryCurrentProjectContext>();

        // Each Email window gets its own view model, but they all resolve the singleton context above,
        // so selecting a project in one window is observed by the others.
        services.AddTransient<EmailWindowViewModel>();

        // Small factory so hosts open the window through DI instead of constructing an isolated context.
        services.AddSingleton<IEmailWindowFactory, EmailWindowFactory>();
        services.AddSingleton<IEmailWorkItemWindowFactory, EmailWorkItemWindowFactory>();
        // Shell-hosted singleton inbox (create-once, reuse in main content — legacy cache pattern).
        services.AddSingleton<IEmailSurfaceHost, EmailSurfaceHost>();

        services.AddTransient<ProjectCreateDialogViewModel>();
        services.AddTransient<IProjectCreateDialogFactory, ProjectCreateDialogFactory>();

        services.AddSiNetWorkSurfaces();
    }
}
