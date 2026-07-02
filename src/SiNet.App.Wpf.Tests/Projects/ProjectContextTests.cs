using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.App.Wpf.Surfaces.Email;
using SiNet.Application.Projects;
using Xunit;

namespace SiNet.App.Wpf.Tests.Projects;

/// <summary>
/// Unit tests for the first (fake/in-memory) Project Context slice (see <c>docs/PROJECTS.md</c> §4/§5
/// and <c>docs/PROJECT_CONTEXT_MIGRATION.md</c>). They lock in the runtime-context guarantees that the
/// rest of the app relies on: the shared <see cref="ICurrentProjectContext"/> de-dupes by
/// <c>ProjectId</c> and only broadcasts real changes, the shared <see cref="ProjectSelectorViewModel"/>
/// loads through <see cref="IProjectQueryService"/> and publishes selection to the context, and the
/// Email window merely <i>observes</i> the Current Project without owning it.
/// </summary>
public sealed class ProjectContextTests
{
    private static ProjectSummaryDto Project(int id, string number = "1000", string name = "Project")
        => new(
            ProjectId: id,
            ProjectNumber: number,
            ProjectName: name,
            PlaceName: null,
            CompanyName: null,
            JobType: null,
            Status: null,
            AssignedUserName: null,
            IsActive: true);

    /// <summary>
    /// Minimal deterministic <see cref="IProjectQueryService"/> that returns a fixed set (ignoring
    /// filters) so selector-load assertions are stable and independent of the fake sample data.
    /// </summary>
    private sealed class StubProjectQueryService(IReadOnlyList<ProjectSummaryDto> projects) : IProjectQueryService
    {
        public Task<IReadOnlyList<ProjectSummaryDto>> SearchProjectsAsync(
            ProjectSearchQuery query,
            CancellationToken cancellationToken = default)
            => Task.FromResult(projects);

        public Task<ProjectSummaryDto?> GetProjectAsync(int projectId, CancellationToken cancellationToken = default)
            => Task.FromResult(projects.FirstOrDefault(p => p.ProjectId == projectId));
    }

    [Fact]
    public async Task CurrentProjectContext_dedupes_by_project_id()
    {
        // Setting the SAME project id twice must be a no-op the second time: the event fires once.
        var context = new InMemoryCurrentProjectContext();
        var raised = 0;
        context.CurrentProjectChanged += (_, _) => raised++;

        await context.SetCurrentProjectAsync(Project(42, "1042", "A"));
        await context.SetCurrentProjectAsync(Project(42, "9999", "different fields, same id"));

        Assert.Equal(1, raised);
        Assert.NotNull(context.CurrentProject);
        Assert.Equal(42, context.CurrentProject!.ProjectId);
    }

    [Fact]
    public async Task CurrentProjectChanged_fires_only_when_project_changes()
    {
        // Distinct ids each raise; clearing to null raises; clearing again (already null) does not.
        var context = new InMemoryCurrentProjectContext();
        var raised = 0;
        context.CurrentProjectChanged += (_, _) => raised++;

        await context.SetCurrentProjectAsync(Project(1));   // null -> 1  : raise
        await context.SetCurrentProjectAsync(Project(2));   // 1 -> 2     : raise
        await context.SetCurrentProjectAsync(null);         // 2 -> null  : raise
        await context.SetCurrentProjectAsync(null);         // null->null : no raise

        Assert.Equal(3, raised);
        Assert.Null(context.CurrentProject);
    }

    [Fact]
    public async Task ProjectSelector_loads_projects_from_query_service()
    {
        var projects = new[] { Project(1, "1001", "Alpha"), Project(2, "1002", "Beta") };
        var sut = new ProjectSelectorViewModel(new StubProjectQueryService(projects), new InMemoryCurrentProjectContext());

        await sut.LoadAsync();

        Assert.Equal(2, sut.Projects.Count);
        Assert.Contains(sut.Projects, p => p.ProjectId == 1);
        Assert.Contains(sut.Projects, p => p.ProjectId == 2);
    }

    [Fact]
    public async Task Selecting_a_project_updates_current_project_context()
    {
        var projects = new[] { Project(7, "1007", "Gamma") };
        var context = new InMemoryCurrentProjectContext();
        var sut = new ProjectSelectorViewModel(new StubProjectQueryService(projects), context);
        await sut.LoadAsync();

        sut.SelectProjectCommand.Execute(sut.Projects.Single());

        Assert.NotNull(context.CurrentProject);
        Assert.Equal(7, context.CurrentProject!.ProjectId);
    }

    [Fact]
    public async Task EmailWindowViewModel_observes_current_project_changes()
    {
        // The Email window hosts the selector but does not own selection: changing the shared context
        // updates its display strip. Publishing through the selector (as the UI would) must be reflected.
        var projects = new[] { Project(1042, "1042", "North Towers") };
        var context = new InMemoryCurrentProjectContext();
        var sut = new EmailWindowViewModel(new StubProjectQueryService(projects), context);
        await sut.ProjectSelector.LoadAsync();

        sut.ProjectSelector.SelectProjectCommand.Execute(sut.ProjectSelector.Projects.Single());

        Assert.Contains("1042", sut.ActiveProjectDisplay);
        Assert.Contains("North Towers", sut.ActiveProjectDisplay);
    }

    [Fact]
    public async Task Two_selectors_sharing_one_context_observe_the_same_selection()
    {
        // The core of the DI-sharing slice: when two selectors share ONE ICurrentProjectContext,
        // selecting a project in one is observed by the other (this is what DI singleton guarantees).
        var projects = new[] { Project(3, "1003", "Delta"), Project(4, "1004", "Echo") };
        var sharedContext = new InMemoryCurrentProjectContext();

        var selectorA = new ProjectSelectorViewModel(new StubProjectQueryService(projects), sharedContext);
        var selectorB = new ProjectSelectorViewModel(new StubProjectQueryService(projects), sharedContext);
        await selectorA.LoadAsync();
        await selectorB.LoadAsync();

        selectorA.SelectProjectCommand.Execute(selectorA.Projects.First(p => p.ProjectId == 4));

        Assert.NotNull(selectorB.SelectedProject);
        Assert.Equal(4, selectorB.SelectedProject!.ProjectId);
        Assert.Equal(4, sharedContext.CurrentProject!.ProjectId);
    }

    [Fact]
    public async Task EmailWindowViewModel_observes_external_context_change()
    {
        // The Email VM must react to a change made DIRECTLY on the shared context by some other
        // surface (not through its own selector) — proving it is an observer, not the owner.
        var projects = new[] { Project(5, "1005", "Foxtrot") };
        var sharedContext = new InMemoryCurrentProjectContext();
        var sut = new EmailWindowViewModel(new StubProjectQueryService(projects), sharedContext);
        await sut.ProjectSelector.LoadAsync();

        await sharedContext.SetCurrentProjectAsync(Project(5, "1005", "Foxtrot"));

        Assert.Contains("1005", sut.ActiveProjectDisplay);
        Assert.Contains("Foxtrot", sut.ActiveProjectDisplay);
    }

    [Fact]
    public async Task Selecting_the_same_project_twice_does_not_fire_duplicate_events()
    {
        // Re-selecting the same project (via the selector path) must not re-publish: the context
        // de-dupes by ProjectId, so subscribers are not notified a second time.
        var projects = new[] { Project(6, "1006", "Golf") };
        var sharedContext = new InMemoryCurrentProjectContext();
        var raised = 0;
        sharedContext.CurrentProjectChanged += (_, _) => raised++;

        var sut = new ProjectSelectorViewModel(new StubProjectQueryService(projects), sharedContext);
        await sut.LoadAsync();

        sut.SelectProjectCommand.Execute(sut.Projects.Single()); // first select -> raises once
        sut.SelectProjectCommand.Execute(sut.Projects.Single()); // same project -> no new event

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Di_registration_shares_one_current_project_context_singleton()
    {
        // End-to-end DI check: AddSiNetProjectContextFake registers ICurrentProjectContext as a
        // singleton, so every resolution (and every EmailWindowViewModel) shares the same instance.
        using var provider = new ServiceCollection()
            .AddSiNetProjectContextFake()
            .BuildServiceProvider();

        var context1 = provider.GetRequiredService<ICurrentProjectContext>();
        var context2 = provider.GetRequiredService<ICurrentProjectContext>();
        Assert.Same(context1, context2);

        // Transient view models each resolve, but both observe the SAME singleton context.
        using var vmA = provider.GetRequiredService<EmailWindowViewModel>();
        using var vmB = provider.GetRequiredService<EmailWindowViewModel>();
        Assert.NotSame(vmA, vmB);

        // The factory is available and resolves the shared services.
        Assert.NotNull(provider.GetRequiredService<IEmailWindowFactory>());
    }

    [Fact]
    public async Task Singleton_is_scoped_per_di_container_not_across_containers()
    {
        // Documents the scope contract (see docs/PROJECTS.md §4 "Scope"): the singleton lifetime is
        // per DI container/process, NOT global. Two separate containers stand in for two separate
        // running app instances — they get independent contexts, and a selection in one must not
        // leak into the other. No cross-process persistence exists.
        using var appInstanceA = new ServiceCollection().AddSiNetProjectContextFake().BuildServiceProvider();
        using var appInstanceB = new ServiceCollection().AddSiNetProjectContextFake().BuildServiceProvider();

        var contextA = appInstanceA.GetRequiredService<ICurrentProjectContext>();
        var contextB = appInstanceB.GetRequiredService<ICurrentProjectContext>();
        Assert.NotSame(contextA, contextB);

        await contextA.SetCurrentProjectAsync(Project(7, "1007", "Hotel"));

        // Instance A has its own Current Project; instance B is unaffected (independent by design).
        Assert.Equal(7, contextA.CurrentProject!.ProjectId);
        Assert.Null(contextB.CurrentProject);
    }
}
