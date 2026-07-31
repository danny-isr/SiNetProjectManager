using SiNet.App.Wpf.Projects.Dashboard;
using SiNet.Application.Projects;
using Xunit;

namespace SiNet.App.Wpf.Tests.Projects.Dashboard;

public sealed class ProjectsDashboardViewModelTests
{
    [Fact]
    public async Task Status_filter_narrows_rows()
    {
        var rows = new[]
        {
            MakeRow(1, "Alpha", statusId: 10, place: "TLV", openWf: 1, openTasks: 2),
            MakeRow(2, "Beta", statusId: 20, place: "HAIFA", openWf: 0, openTasks: 0),
        };
        var vm = CreateVm(rows);

        await vm.RefreshAsync().ConfigureAwait(true);
        Assert.Equal(2, vm.Rows.Count);

        vm.StatusFilter = new ProjectFilterOptionDto(10, "Active");
        Assert.Single(vm.Rows);
        Assert.Equal(1, vm.Rows[0].ProjectId);
        Assert.Equal("1", vm.TotalCountText);
    }

    [Fact]
    public async Task Place_and_open_workflow_filters_apply()
    {
        var rows = new[]
        {
            MakeRow(1, "A", statusId: 1, place: "TLV", openWf: 1, openTasks: 0),
            MakeRow(2, "B", statusId: 1, place: "TLV", openWf: 0, openTasks: 3),
            MakeRow(3, "C", statusId: 1, place: "HAIFA", openWf: 2, openTasks: 1),
        };
        var vm = CreateVm(rows);

        await vm.RefreshAsync().ConfigureAwait(true);
        vm.PlaceFilter = "TLV";
        vm.OnlyWithOpenWorkflow = true;

        Assert.Single(vm.Rows);
        Assert.Equal(1, vm.Rows[0].ProjectId);
        Assert.Equal("1", vm.WithOpenWorkflowText);
        Assert.Equal("0", vm.OpenTasksSumText);
    }

    [Fact]
    public async Task Created_date_range_filters_rows()
    {
        var rows = new[]
        {
            MakeRow(1, "Old", statusId: 1, place: "TLV", openWf: 0, openTasks: 0,
                created: new DateTime(2024, 1, 15)),
            MakeRow(2, "New", statusId: 1, place: "TLV", openWf: 0, openTasks: 0,
                created: new DateTime(2025, 6, 1)),
        };
        var vm = CreateVm(rows);

        await vm.RefreshAsync().ConfigureAwait(true);
        vm.CreatedFrom = new DateTime(2025, 1, 1);
        vm.CreatedTo = new DateTime(2025, 12, 31);

        Assert.Single(vm.Rows);
        Assert.Equal(2, vm.Rows[0].ProjectId);
    }

    [Fact]
    public async Task Text_search_matches_name_and_place()
    {
        var rows = new[]
        {
            MakeRow(1, "Bridge North", statusId: 1, place: "Haifa", openWf: 0, openTasks: 0),
            MakeRow(2, "Tower", statusId: 1, place: "Tel Aviv", openWf: 0, openTasks: 0),
        };
        var vm = CreateVm(rows);

        await vm.RefreshAsync().ConfigureAwait(true);
        vm.FilterText = "haifa";

        Assert.Single(vm.Rows);
        Assert.Equal(1, vm.Rows[0].ProjectId);
    }

    [Fact]
    public async Task Summary_cards_reflect_filtered_set()
    {
        var rows = new[]
        {
            MakeRow(1, "A", statusId: 1, place: "TLV", openWf: 1, openTasks: 2, isActive: true),
            MakeRow(2, "B", statusId: 1, place: "TLV", openWf: 0, openTasks: 5, isActive: false),
        };
        var vm = CreateVm(rows, includeClosedByDefault: true);

        await vm.RefreshAsync().ConfigureAwait(true);

        Assert.Equal("2", vm.TotalCountText);
        Assert.Equal("1", vm.ActiveCountText);
        Assert.Equal("1", vm.ClosedCountText);
        Assert.Equal("1", vm.WithOpenWorkflowText);
        Assert.Equal("1", vm.WithoutOpenWorkflowText);
        Assert.Equal("7", vm.OpenTasksSumText);
    }

    private static ProjectsDashboardViewModel CreateVm(
        IReadOnlyList<ProjectDashboardRowDto> rows,
        bool includeClosedByDefault = false)
    {
        var vm = new ProjectsDashboardViewModel(
            new FakeDashboardQuery(rows),
            new FakeFilterOptions(),
            new FakeCurrentProject());
        if (includeClosedByDefault)
            vm.IncludeClosed = true;
        return vm;
    }

    private static ProjectDashboardRowDto MakeRow(
        int id,
        string name,
        int statusId,
        string place,
        int openWf,
        int openTasks,
        bool isActive = true,
        DateTime? created = null,
        DateTime? start = null) =>
        new(
            ProjectId: id,
            ProjectNumber: id.ToString(),
            ProjectNumberValue: id,
            ProjectName: name,
            PlaceName: place,
            CompanyName: "Co",
            JobTypeNames: ["General"],
            JobTypeIds: [9],
            Status: $"S{statusId}",
            StatusCode: $"Code{statusId}",
            StatusId: statusId,
            AssignedUserName: "Worker",
            IsActive: isActive,
            Start: start,
            End: null,
            Created: created,
            OpenWorkflowCount: openWf,
            OpenWorkflowSummary: openWf > 0 ? "Proposal — Intake" : null,
            OpenTaskCount: openTasks);

    private sealed class FakeDashboardQuery(IReadOnlyList<ProjectDashboardRowDto> rows)
        : IProjectDashboardQueryService
    {
        public Task<IReadOnlyList<ProjectDashboardRowDto>> GetRowsAsync(
            ProjectDashboardQuery query,
            CancellationToken cancellationToken = default)
        {
            var result = query.IncludeClosed
                ? rows
                : rows.Where(r => r.IsActive).ToList();
            return Task.FromResult<IReadOnlyList<ProjectDashboardRowDto>>(result);
        }
    }

    private sealed class FakeFilterOptions : IProjectFilterOptionsService
    {
        public Task<ProjectFilterOptionsDto> GetFilterOptionsAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ProjectFilterOptionsDto([], [], []));
    }

    private sealed class FakeCurrentProject : ICurrentProjectContext
    {
        public ProjectSummaryDto? CurrentProject { get; private set; }

        public event EventHandler<ProjectChangedEventArgs>? CurrentProjectChanged;

        public Task SetCurrentProjectAsync(
            ProjectSummaryDto? project,
            CancellationToken cancellationToken = default)
        {
            CurrentProject = project;
            CurrentProjectChanged?.Invoke(this, new ProjectChangedEventArgs(project));
            return Task.CompletedTask;
        }
    }
}
