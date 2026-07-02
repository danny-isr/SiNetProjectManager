using SiNet.App.Wpf.Shared.Projects;
using SiNet.Application.Projects;
using Xunit;

namespace SiNet.App.Wpf.Tests.Projects;

/// <summary>
/// UX state tests for <see cref="ProjectSelectorViewModel"/>: selection display, popup open/close,
/// expanded results cap, and full-catalog search guarantees.
/// </summary>
public sealed class ProjectSelectorViewModelUxTests
{
    private static ProjectSummaryDto Project(int id, string number, string name = "Project")
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

    private sealed class StubProjectQueryService(
        Func<ProjectSearchQuery, Task<IReadOnlyList<ProjectSummaryDto>>> search) : IProjectQueryService
    {
        public List<ProjectSearchQuery> ReceivedQueries { get; } = [];

        public Task<IReadOnlyList<ProjectSummaryDto>> SearchProjectsAsync(
            ProjectSearchQuery query,
            CancellationToken cancellationToken = default)
        {
            ReceivedQueries.Add(query);
            return search(query);
        }

        public Task<ProjectSummaryDto?> GetProjectAsync(int projectId, CancellationToken cancellationToken = default)
            => Task.FromResult<ProjectSummaryDto?>(null);
    }

    private static ProjectSelectorViewModel CreateSut(
        StubProjectQueryService queryService,
        TimeSpan debounce = default)
        => new(
            queryService,
            new FakeProjectFilterOptionsService(),
            new InMemoryCurrentProjectContext(),
            debounce);

    [Fact]
    public async Task Selecting_project_updates_selected_project_and_context()
    {
        var project = Project(7, "1007", "Gamma");
        var context = new InMemoryCurrentProjectContext();
        var queryService = new StubProjectQueryService(_ => Task.FromResult<IReadOnlyList<ProjectSummaryDto>>(new[] { project }));
        var sut = new ProjectSelectorViewModel(queryService, new FakeProjectFilterOptionsService(), context, TimeSpan.Zero);
        await sut.LoadAsync();

        sut.SelectProjectCommand.Execute(project);

        Assert.NotNull(sut.SelectedProject);
        Assert.Equal(7, sut.SelectedProject!.ProjectId);
        Assert.Equal(7, context.CurrentProject!.ProjectId);
    }

    [Fact]
    public async Task Selecting_project_updates_editor_text_to_compact_display()
    {
        var project = Project(7, "1007", "Gamma");
        var queryService = new StubProjectQueryService(_ => Task.FromResult<IReadOnlyList<ProjectSummaryDto>>(new[] { project }));
        var sut = CreateSut(queryService);
        await sut.LoadAsync();

        sut.SelectProjectCommand.Execute(project);

        Assert.False(sut.IsUserTyping);
        Assert.Equal("1007 \u2014 Gamma", sut.EditorText);
        Assert.Equal("1007 \u2014 Gamma", sut.SearchText);
    }

    [Fact]
    public async Task Selecting_project_closes_results_popup()
    {
        var project = Project(1, "1001", "Alpha");
        var queryService = new StubProjectQueryService(_ => Task.FromResult<IReadOnlyList<ProjectSummaryDto>>(new[] { project }));
        var sut = CreateSut(queryService);
        await sut.LoadAsync();

        sut.IsResultsOpen = true;
        sut.SelectProjectCommand.Execute(project);

        Assert.False(sut.IsResultsOpen);
    }

    [Fact]
    public async Task Search_text_is_preserved_during_typing_reload()
    {
        var projects = new[] { Project(1, "1234", name: "\u05E8\u05E2\u05E0\u05E0\u05D4") };
        var queryService = new StubProjectQueryService(q => Task.FromResult(ProjectSummaryQuery.Apply(projects, q)));
        var sut = CreateSut(queryService);

        sut.SearchText = "\u05E8\u05E2\u05E0\u05E0\u05D4 1234";
        await sut.LoadAsync();

        Assert.True(sut.IsUserTyping);
        Assert.Equal("\u05E8\u05E2\u05E0\u05E0\u05D4 1234", sut.EditorText);
        Assert.Equal("\u05E8\u05E2\u05E0\u05E0\u05D4 1234", sut.SearchText);
    }

    [Fact]
    public async Task Selection_switches_from_typing_to_display_mode_only_after_explicit_pick()
    {
        var projects = new[] { Project(1, "1234", name: "Match") };
        var queryService = new StubProjectQueryService(q => Task.FromResult(ProjectSummaryQuery.Apply(projects, q)));
        var sut = CreateSut(queryService);

        sut.SearchText = "1234";
        await sut.LoadAsync();

        Assert.True(sut.IsUserTyping);
        Assert.Null(sut.SelectedProject);

        sut.SelectProjectCommand.Execute(projects[0]);

        Assert.False(sut.IsUserTyping);
        Assert.NotNull(sut.SelectedProject);
    }

    [Fact]
    public void Escape_closes_results_popup()
    {
        var sut = CreateSut(new StubProjectQueryService(_ => Task.FromResult<IReadOnlyList<ProjectSummaryDto>>(Array.Empty<ProjectSummaryDto>())));

        sut.IsResultsOpen = true;
        sut.CloseResults();

        Assert.False(sut.IsResultsOpen);
    }

    [Fact]
    public async Task Toggle_results_opens_and_closes_popup()
    {
        var queryService = new StubProjectQueryService(_ => Task.FromResult<IReadOnlyList<ProjectSummaryDto>>(Array.Empty<ProjectSummaryDto>()));
        var sut = CreateSut(queryService);
        await sut.LoadAsync();

        sut.ToggleResultsCommand.Execute(null);
        Assert.True(sut.IsResultsOpen);

        sut.ToggleResultsCommand.Execute(null);
        Assert.False(sut.IsResultsOpen);
    }

    [Fact]
    public async Task Open_results_in_display_mode_triggers_browse_reload()
    {
        var queryService = new StubProjectQueryService(_ => Task.FromResult<IReadOnlyList<ProjectSummaryDto>>(Array.Empty<ProjectSummaryDto>()));
        var sut = CreateSut(queryService);
        var project = Project(1, "1001", "Alpha");
        await sut.LoadAsync();
        sut.SelectProjectCommand.Execute(project);
        queryService.ReceivedQueries.Clear();

        sut.OpenResults();
        await Task.Delay(50);

        Assert.True(sut.IsResultsOpen);
        Assert.Single(queryService.ReceivedQueries);
        Assert.Null(queryService.ReceivedQueries[0].SearchText);
    }

    [Fact]
    public void Default_max_results_is_200()
    {
        var sut = CreateSut(new StubProjectQueryService(_ => Task.FromResult<IReadOnlyList<ProjectSummaryDto>>(Array.Empty<ProjectSummaryDto>())));

        Assert.Equal(200, sut.EffectiveMaxResults);
        Assert.False(sut.ShowExpandedResults);
    }

    [Fact]
    public async Task Selecting_project_stays_closed_when_search_box_regains_focus()
    {
        var project = Project(1, "1001", "Alpha");
        var queryService = new StubProjectQueryService(_ => Task.FromResult<IReadOnlyList<ProjectSummaryDto>>(new[] { project }));
        var sut = CreateSut(queryService);
        await sut.LoadAsync();

        sut.IsResultsOpen = true;
        sut.SelectProjectCommand.Execute(project);
        sut.HandleSearchBoxGotFocus();

        Assert.False(sut.IsResultsOpen);
    }

    [Fact]
    public async Task Expanded_results_show_all_projects_without_cap()
    {
        var many = Enumerable.Range(1, 1200)
            .Select(i => Project(i, i.ToString()))
            .ToArray();

        var queryService = new StubProjectQueryService(q => Task.FromResult(ProjectSummaryQuery.Apply(many, q)));
        var sut = CreateSut(queryService);
        await sut.LoadAsync();

        Assert.Equal(ProjectSelectorViewModel.DefaultMaxResults, sut.Projects.Count);

        sut.ShowExpandedResults = true;
        await Task.Delay(50);

        Assert.Null(sut.EffectiveMaxResults);
        Assert.Equal(1200, sut.Projects.Count);
        Assert.Contains("\u05DE\u05DC\u05D0\u05D4", sut.StatusMessage);
    }

    [Fact]
    public async Task Search_query_uses_effective_max_results_but_searches_full_catalog()
    {
        var many = Enumerable.Range(1, 2500)
            .Select(i => Project(i, i.ToString()))
            .ToArray();

        var queryService = new StubProjectQueryService(q => Task.FromResult(ProjectSummaryQuery.Apply(many, q)));
        var sut = CreateSut(queryService);

        sut.SearchText = "1";
        await Task.Delay(50);

        var query = Assert.Single(queryService.ReceivedQueries);
        Assert.Equal("1", query.SearchText);
        Assert.Equal(ProjectSelectorViewModel.DefaultMaxResults, query.MaxResults);
        Assert.Contains(sut.Projects, p => p.ProjectNumber == "1");
    }
}
