using SiNet.App.Wpf.Shared.Projects;
using SiNet.Application.Projects;
using Xunit;

namespace SiNet.App.Wpf.Tests.Projects;

/// <summary>
/// Behavioral tests for <see cref="ProjectSelectorViewModel"/>: search text preservation, stable filter
/// selections, independent filter option loading, and multi-word search integration.
/// </summary>
public sealed class ProjectSelectorViewModelBehaviorTests
{
    private static ProjectSummaryDto Project(
        int id,
        string number,
        string name = "Project",
        string? place = null,
        string? company = null,
        string? jobType = null,
        string? status = null,
        bool isActive = true,
        int? statusId = null,
        IReadOnlyList<int>? jobTypeIds = null)
        => new(
            ProjectId: id,
            ProjectNumber: number,
            ProjectName: name,
            PlaceName: place,
            CompanyName: company,
            JobType: jobType,
            Status: status,
            AssignedUserName: null,
            IsActive: isActive,
            StatusId: statusId,
            JobTypeIds: jobTypeIds);

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

    private sealed class StubFilterOptionsService(ProjectFilterOptionsDto options) : IProjectFilterOptionsService
    {
        public int CallCount { get; private set; }

        public Task<ProjectFilterOptionsDto> GetFilterOptionsAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(options);
        }
    }

    private static readonly ProjectFilterOptionsDto FullFilterOptions = new(
        Statuses:
        [
            new(1, "\u05E4\u05E2\u05D9\u05DC"),
            new(2, "\u05E1\u05D2\u05D5\u05E8"),
        ],
        JobTypes:
        [
            new(10, "\u05DE\u05D2\u05D5\u05E8\u05D9\u05DD"),
            new(20, "\u05DE\u05E1\u05D7\u05E8"),
        ],
        Users: Array.Empty<ProjectFilterOptionDto>());

    [Fact]
    public async Task SearchText_is_unchanged_after_reload()
    {
        var projects = new[] { Project(1, "1001", place: "\u05E8\u05E2\u05E0\u05E0\u05D4") };
        var queryService = new StubProjectQueryService(q => Task.FromResult(ProjectSummaryQuery.Apply(projects, q)));
        var sut = new ProjectSelectorViewModel(
            queryService,
            new StubFilterOptionsService(FullFilterOptions),
            new InMemoryCurrentProjectContext(),
            TimeSpan.Zero);

        sut.SearchText = "\u05E8\u05E2\u05E0\u05E0\u05D4 1234";
        await sut.LoadAsync();

        Assert.Equal("\u05E8\u05E2\u05E0\u05E0\u05D4 1234", sut.SearchText);
    }

    [Fact]
    public async Task Typing_does_not_set_selected_project()
    {
        var projects = new[] { Project(1, "1234", place: "\u05E8\u05E2\u05E0\u05E0\u05D4") };
        var queryService = new StubProjectQueryService(q => Task.FromResult(ProjectSummaryQuery.Apply(projects, q)));
        var sut = new ProjectSelectorViewModel(
            queryService,
            new StubFilterOptionsService(FullFilterOptions),
            new InMemoryCurrentProjectContext(),
            TimeSpan.Zero);

        sut.SearchText = "\u05E8\u05E2\u05E0\u05E0\u05D4 1234";
        await sut.LoadAsync();

        Assert.Null(sut.SelectedProject);
    }

    [Fact]
    public async Task Selected_project_is_preserved_after_reload_when_still_present()
    {
        var project = Project(7, "1007", name: "Keep Me");
        var queryService = new StubProjectQueryService(_ => Task.FromResult<IReadOnlyList<ProjectSummaryDto>>(new[] { project }));
        var sut = new ProjectSelectorViewModel(
            queryService,
            new StubFilterOptionsService(FullFilterOptions),
            new InMemoryCurrentProjectContext(),
            TimeSpan.Zero);

        await sut.InitializeAsync();
        sut.SelectProjectCommand.Execute(project);
        await sut.LoadAsync();

        Assert.NotNull(sut.SelectedProject);
        Assert.Equal(7, sut.SelectedProject!.ProjectId);
    }

    [Fact]
    public async Task Status_selection_is_preserved_after_filter_options_reload()
    {
        var filterService = new StubFilterOptionsService(FullFilterOptions);
        var sut = new ProjectSelectorViewModel(
            new StubProjectQueryService(_ => Task.FromResult<IReadOnlyList<ProjectSummaryDto>>(Array.Empty<ProjectSummaryDto>())),
            filterService,
            new InMemoryCurrentProjectContext(),
            TimeSpan.Zero);

        await sut.InitializeAsync();
        sut.SelectedStatusId = 1;
        await sut.LoadFilterOptionsAsync();

        Assert.Equal(1, sut.SelectedStatusId);
    }

    [Fact]
    public async Task JobType_selection_is_preserved_after_filter_options_reload()
    {
        var filterService = new StubFilterOptionsService(FullFilterOptions);
        var sut = new ProjectSelectorViewModel(
            new StubProjectQueryService(_ => Task.FromResult<IReadOnlyList<ProjectSummaryDto>>(Array.Empty<ProjectSummaryDto>())),
            filterService,
            new InMemoryCurrentProjectContext(),
            TimeSpan.Zero);

        await sut.InitializeAsync();
        sut.SelectedJobTypeId = 20;
        await sut.LoadFilterOptionsAsync();

        Assert.Equal(20, sut.SelectedJobTypeId);
    }

    [Fact]
    public async Task Filter_options_are_not_derived_from_capped_project_results()
    {
        var many = Enumerable.Range(1, ProjectSelectorViewModel.DefaultMaxResults + 50)
            .Select(i => Project(i, (1000 + i).ToString(), status: "\u05E4\u05E2\u05D9\u05DC", jobType: "\u05DE\u05D2\u05D5\u05E8\u05D9\u05DD"))
            .ToArray();

        var filterService = new StubFilterOptionsService(FullFilterOptions);
        var sut = new ProjectSelectorViewModel(
            new StubProjectQueryService(q => Task.FromResult(ProjectSummaryQuery.Apply(many, q))),
            filterService,
            new InMemoryCurrentProjectContext(),
            TimeSpan.Zero);

        await sut.InitializeAsync();

        Assert.Equal(ProjectSelectorViewModel.DefaultMaxResults, sut.Projects.Count);
        Assert.Equal(3, sut.StatusOptions.Count); // "all" + 2 statuses from filter service
        Assert.Equal(3, sut.JobTypeOptions.Count); // "all" + 2 job types from filter service
    }

    [Fact]
    public async Task Multi_word_search_finds_project_across_fields_in_either_order()
    {
        var projects = new[]
        {
            Project(1, "1234", name: "\u05E9\u05DD", place: "\u05E8\u05E2\u05E0\u05E0\u05D4", company: "\u05DC\u05E7\u05D5\u05D7"),
            Project(2, "5678", name: "other", place: "other", company: "other"),
        };

        var queryService = new StubProjectQueryService(q => Task.FromResult(ProjectSummaryQuery.Apply(projects, q)));
        var sut = new ProjectSelectorViewModel(
            queryService,
            new StubFilterOptionsService(FullFilterOptions),
            new InMemoryCurrentProjectContext(),
            TimeSpan.Zero);

        sut.SearchText = "\u05E8\u05E2\u05E0\u05E0\u05D4 1234";
        await sut.LoadAsync();
        Assert.Single(sut.Projects);

        sut.SearchText = "1234 \u05E8\u05E2\u05E0\u05E0\u05D4";
        await sut.LoadAsync();
        Assert.Single(sut.Projects);
        Assert.Equal(1, sut.Projects[0].ProjectId);
    }

    [Fact]
    public async Task Browse_status_message_explains_display_cap()
    {
        var many = Enumerable.Range(1, ProjectSelectorViewModel.DefaultMaxResults + 50)
            .Select(i => Project(i, (1000 + i).ToString()))
            .ToArray();

        var sut = new ProjectSelectorViewModel(
            new StubProjectQueryService(q => Task.FromResult(ProjectSummaryQuery.Apply(many, q))),
            new StubFilterOptionsService(FullFilterOptions),
            new InMemoryCurrentProjectContext(),
            TimeSpan.Zero);

        await sut.LoadAsync();

        Assert.Contains("200", sut.StatusMessage);
        Assert.Contains("\u05DC\u05D7\u05E4\u05E9", sut.StatusMessage);
    }

    [Fact]
    public async Task Search_at_cap_shows_narrowing_hint()
    {
        var many = Enumerable.Range(1, 2500)
            .Select(i => Project(i, i.ToString()))
            .ToArray();

        var sut = new ProjectSelectorViewModel(
            new StubProjectQueryService(q => Task.FromResult(ProjectSummaryQuery.Apply(many, q))),
            new StubFilterOptionsService(FullFilterOptions),
            new InMemoryCurrentProjectContext(),
            TimeSpan.Zero);

        sut.SearchText = "1";
        await sut.LoadAsync();

        Assert.Equal(ProjectSelectorViewModel.DefaultMaxResults, sut.Projects.Count);
        Assert.Contains("\u05E6\u05DE\u05E6\u05DD", sut.StatusMessage);
    }

    [Fact]
    public async Task Search_finds_old_project_outside_browse_cap()
    {
        var many = Enumerable.Range(1, 2500)
            .Select(i => Project(i, i.ToString(), place: i == 1 ? "\u05E8\u05E2\u05E0\u05E0\u05D4" : null))
            .ToArray();

        var sut = new ProjectSelectorViewModel(
            new StubProjectQueryService(q => Task.FromResult(ProjectSummaryQuery.Apply(many, q))),
            new StubFilterOptionsService(FullFilterOptions),
            new InMemoryCurrentProjectContext(),
            TimeSpan.Zero);

        await sut.InitializeAsync();
        Assert.DoesNotContain(sut.Projects, p => p.ProjectNumber == "1");

        sut.SearchText = "1";
        await sut.LoadAsync();

        Assert.Contains(sut.Projects, p => p.ProjectNumber == "1");
        Assert.Equal("1", sut.Projects[0].ProjectNumber);
    }

    [Fact]
    public async Task Stale_async_results_do_not_overwrite_newer_search()
    {
        var gates = new Dictionary<string, TaskCompletionSource<IReadOnlyList<ProjectSummaryDto>>>();
        var queryService = new StubProjectQueryService(query =>
        {
            var text = query.SearchText ?? string.Empty;
            var tcs = new TaskCompletionSource<IReadOnlyList<ProjectSummaryDto>>();
            gates[text] = tcs;
            return tcs.Task;
        });

        var sut = new ProjectSelectorViewModel(
            queryService,
            new StubFilterOptionsService(FullFilterOptions),
            new InMemoryCurrentProjectContext(),
            TimeSpan.Zero);

        sut.SearchText = "old";
        sut.SearchText = "new";

        gates["new"].TrySetResult([Project(2, "2000", name: "New")]);
        await Task.Delay(50);
        gates["old"].TrySetResult([Project(1, "1000", name: "Old")]);
        await Task.Delay(50);

        Assert.Single(sut.Projects);
        Assert.Equal("New", sut.Projects[0].ProjectName);
        Assert.Equal("new", sut.SearchText);
    }

    [Fact]
    public async Task Status_filter_returns_only_matching_projects()
    {
        var projects = new[]
        {
            Project(1, "1042", status: "\u05E4\u05E2\u05D9\u05DC", statusId: 1),
            Project(2, "1041", status: "\u05E1\u05D2\u05D5\u05E8", statusId: 2, isActive: false),
        };

        var queryService = new StubProjectQueryService(q => Task.FromResult(ProjectSummaryQuery.Apply(projects, q)));
        var sut = new ProjectSelectorViewModel(
            queryService,
            new StubFilterOptionsService(FullFilterOptions),
            new InMemoryCurrentProjectContext(),
            TimeSpan.Zero);

        await sut.InitializeAsync();
        sut.SelectedStatusId = 1;
        sut.IncludeClosed = true;
        await sut.LoadAsync();

        Assert.Single(sut.Projects);
        Assert.Equal(1, sut.Projects[0].ProjectId);
        Assert.Equal(1, queryService.ReceivedQueries[^1].StatusId);
    }

    [Fact]
    public async Task JobType_filter_returns_only_matching_projects()
    {
        var projects = new[]
        {
            Project(1, "1042", jobType: "\u05DE\u05D2\u05D5\u05E8\u05D9\u05DD", jobTypeIds: [10]),
            Project(2, "1041", jobType: "\u05DE\u05E1\u05D7\u05E8", jobTypeIds: [20]),
        };

        var queryService = new StubProjectQueryService(q => Task.FromResult(ProjectSummaryQuery.Apply(projects, q)));
        var sut = new ProjectSelectorViewModel(
            queryService,
            new StubFilterOptionsService(FullFilterOptions),
            new InMemoryCurrentProjectContext(),
            TimeSpan.Zero);

        await sut.InitializeAsync();
        sut.SelectedJobTypeId = 20;
        await sut.LoadAsync();

        Assert.Single(sut.Projects);
        Assert.Equal(2, sut.Projects[0].ProjectId);
        Assert.Equal(20, queryService.ReceivedQueries[^1].JobTypeId);
    }

    [Fact]
    public async Task Combined_status_and_job_type_filters_narrow_results()
    {
        var projects = new[]
        {
            Project(1, "1042", jobType: "\u05DE\u05D2\u05D5\u05E8\u05D9\u05DD", status: "\u05E4\u05E2\u05D9\u05DC", statusId: 1, jobTypeIds: [10]),
            Project(2, "1041", jobType: "\u05DE\u05D2\u05D5\u05E8\u05D9\u05DD", status: "\u05E1\u05D2\u05D5\u05E8", statusId: 2, jobTypeIds: [10], isActive: false),
            Project(3, "1040", jobType: "\u05DE\u05E1\u05D7\u05E8", status: "\u05E4\u05E2\u05D9\u05DC", statusId: 1, jobTypeIds: [20]),
        };

        var queryService = new StubProjectQueryService(q => Task.FromResult(ProjectSummaryQuery.Apply(projects, q)));
        var sut = new ProjectSelectorViewModel(
            queryService,
            new StubFilterOptionsService(FullFilterOptions),
            new InMemoryCurrentProjectContext(),
            TimeSpan.Zero);

        await sut.InitializeAsync();
        sut.SelectedStatusId = 1;
        sut.SelectedJobTypeId = 10;
        sut.IncludeClosed = true;
        await sut.LoadAsync();

        Assert.Single(sut.Projects);
        Assert.Equal(1, sut.Projects[0].ProjectId);
    }

    [Fact]
    public async Task All_filter_selection_does_not_apply_field_filter()
    {
        var projects = new[]
        {
            Project(1, "1042", status: "\u05E4\u05E2\u05D9\u05DC", statusId: 1, jobTypeIds: [10]),
            Project(2, "1041", status: "\u05E1\u05D2\u05D5\u05E8", statusId: 2, jobTypeIds: [20], isActive: false),
        };

        var queryService = new StubProjectQueryService(q => Task.FromResult(ProjectSummaryQuery.Apply(projects, q)));
        var sut = new ProjectSelectorViewModel(
            queryService,
            new StubFilterOptionsService(FullFilterOptions),
            new InMemoryCurrentProjectContext(),
            TimeSpan.Zero);

        await sut.InitializeAsync();
        sut.SelectedStatusId = null;
        sut.SelectedJobTypeId = null;
        sut.IncludeClosed = true;
        await sut.LoadAsync();

        Assert.Equal(2, sut.Projects.Count);
        Assert.Null(queryService.ReceivedQueries[^1].StatusId);
        Assert.Null(queryService.ReceivedQueries[^1].JobTypeId);
    }
}
