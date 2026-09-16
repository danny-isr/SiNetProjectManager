using SiNet.App.Wpf.Projects.Dashboard;
using SiNet.Application.Projects;
using Xunit;

namespace SiNet.App.Wpf.Tests.Projects.Dashboard;

public sealed class ProjectNumberSortTests
{
    [Fact]
    public void Ascending_orders_sample_set_numerically()
    {
        var rows = CreateRows(10, 2, 1000, 11, 9, 99, 100);

        var ordered = ProjectNumberSort.OrderByKey(rows, r => r.ProjectNumberSortKey, descending: false);

        Assert.Equal([2, 9, 10, 11, 99, 100, 1000], ordered.Select(r => (int)r.ProjectNumberSortKey));
    }

    [Fact]
    public void Descending_orders_sample_set_numerically()
    {
        var rows = CreateRows(10, 2, 1000, 11, 9, 99, 100);

        var ordered = ProjectNumberSort.OrderByKey(rows, r => r.ProjectNumberSortKey, descending: true);

        Assert.Equal([1000, 100, 99, 11, 10, 9, 2], ordered.Select(r => (int)r.ProjectNumberSortKey));
    }

    [Fact]
    public void Null_empty_and_non_finite_values_sort_first_when_ascending()
    {
        var rows = new[]
        {
            MakeVm(1, "10", 10f),
            MakeVm(2, "", null),
            MakeVm(3, "2", 2f),
            MakeVm(4, "legacy", float.NaN),
            MakeVm(5, "", float.PositiveInfinity),
        };

        var ordered = ProjectNumberSort.OrderByKey(rows, r => r.ProjectNumberSortKey, descending: false);

        Assert.Equal([2, 4, 5, 3, 1], ordered.Select(r => r.ProjectId));
        Assert.Equal(long.MinValue, ordered[0].ProjectNumberSortKey);
        Assert.Equal(2, ordered[3].ProjectNumberSortKey);
        Assert.Equal(10, ordered[4].ProjectNumberSortKey);
    }

    [Fact]
    public void Null_empty_and_non_finite_values_sort_last_when_descending()
    {
        var rows = new[]
        {
            MakeVm(1, "10", 10f),
            MakeVm(2, "", null),
            MakeVm(3, "2", 2f),
        };

        var ordered = ProjectNumberSort.OrderByKey(rows, r => r.ProjectNumberSortKey, descending: true);

        Assert.Equal([1, 3, 2], ordered.Select(r => r.ProjectId));
    }

    [Fact]
    public void ToSortKey_does_not_throw_on_null_empty_or_non_finite()
    {
        Assert.Equal(long.MinValue, ProjectNumberSort.ToSortKey(null));
        Assert.Equal(long.MinValue, ProjectNumberSort.ToSortKey(float.NaN));
        Assert.Equal(long.MinValue, ProjectNumberSort.ToSortKey(float.NegativeInfinity));
        Assert.Equal(long.MinValue, ProjectNumberSort.ToSortKey(float.PositiveInfinity));
        Assert.Equal(0, ProjectNumberSort.ToSortKey(0f));
        Assert.Equal(1000, ProjectNumberSort.ToSortKey(1000f));
    }

    [Fact]
    public async Task Filter_text_still_matches_display_string_after_sort_key_added()
    {
        var rows = new[]
        {
            MakeRow(10, "10", "Office"),
            MakeRow(2, "2", "Tower"),
            MakeRow(100, "100", "Bridge"),
        };
        var vm = new ProjectsDashboardViewModel(
            new StubDashboardQuery(rows),
            new StubFilterOptions(),
            new StubCurrentProject());

        await vm.RefreshAsync().ConfigureAwait(true);
        vm.FilterText = "10";

        Assert.Single(vm.Rows);
        Assert.Equal("10", vm.Rows[0].ProjectNumber);
        Assert.Equal(10, vm.Rows[0].ProjectNumberSortKey);
    }

    private static IReadOnlyList<ProjectsDashboardRowVm> CreateRows(params int[] numbers)
        => numbers.Select((n, index) => MakeVm(index + 1, n.ToString(), n)).ToList();

    private static ProjectsDashboardRowVm MakeVm(int id, string display, float? value)
        => new(MakeRow(id, display, "Name", value));

    private static ProjectDashboardRowDto MakeRow(
        int id,
        string display,
        string name,
        float? value = null) =>
        new(
            ProjectId: id,
            ProjectNumber: display,
            ProjectNumberValue: value ?? (float.TryParse(display, out var parsed) ? parsed : null),
            ProjectName: name,
            PlaceName: "TLV",
            CompanyName: "Co",
            JobTypeNames: ["General"],
            JobTypeIds: [9],
            Status: "S",
            StatusCode: "S",
            StatusId: 1,
            AssignedUserName: "Worker",
            IsActive: true,
            Start: null,
            End: null,
            Created: null,
            OpenWorkflowCount: 0,
            OpenWorkflowSummary: null,
            OpenTaskCount: 0);

    private sealed class StubDashboardQuery(IReadOnlyList<ProjectDashboardRowDto> rows)
        : IProjectDashboardQueryService
    {
        public Task<IReadOnlyList<ProjectDashboardRowDto>> GetRowsAsync(
            ProjectDashboardQuery query,
            CancellationToken cancellationToken = default)
            => Task.FromResult(rows);
    }

    private sealed class StubFilterOptions : IProjectFilterOptionsService
    {
        public Task<ProjectFilterOptionsDto> GetFilterOptionsAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ProjectFilterOptionsDto([], [], []));
    }

    private sealed class StubCurrentProject : ICurrentProjectContext
    {
        public ProjectSummaryDto? CurrentProject { get; private set; }

        public event EventHandler<ProjectChangedEventArgs>? CurrentProjectChanged
        {
            add { }
            remove { }
        }

        public Task SetCurrentProjectAsync(
            ProjectSummaryDto? project,
            CancellationToken cancellationToken = default)
        {
            CurrentProject = project;
            return Task.CompletedTask;
        }
    }
}
