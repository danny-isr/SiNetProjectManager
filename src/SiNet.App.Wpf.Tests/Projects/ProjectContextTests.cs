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

        sut.SelectedProject = sut.Projects.Single();

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

        sut.ProjectSelector.SelectedProject = sut.ProjectSelector.Projects.Single();

        Assert.Contains("1042", sut.ActiveProjectDisplay);
        Assert.Contains("North Towers", sut.ActiveProjectDisplay);
    }
}
