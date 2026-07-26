using System.IO;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.App.Wpf.Surfaces.Tasks;
using SiNet.Application.Tasks;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>Task Workbench grid layout and visible collections guards.</summary>
public sealed class TaskWorkbenchLayoutTests
{
    [Fact]
    public async Task Task_workbench_displays_loaded_quick_tasks()
    {
        var vm = CreateViewModelWithTasks(
            SampleTask(1, WorkQueueBucketCodes.Quick),
            SampleTask(2, WorkQueueBucketCodes.Quick));

        await vm.LoadAsync();

        Assert.Equal(2, vm.QuickTasks.Count);
        Assert.Equal(0, vm.MediumTasks.Count);
        Assert.Equal(0, vm.LongTasks.Count);
    }

    [Fact]
    public async Task Task_workbench_displays_loaded_medium_tasks()
    {
        var vm = CreateViewModelWithTasks(SampleTask(3, WorkQueueBucketCodes.Medium));

        await vm.LoadAsync();

        Assert.Single(vm.MediumTasks);
        Assert.Empty(vm.QuickTasks);
    }

    [Fact]
    public async Task Task_workbench_displays_loaded_long_tasks()
    {
        var vm = CreateViewModelWithTasks(SampleTask(4, WorkQueueBucketCodes.Long));

        await vm.LoadAsync();

        Assert.Single(vm.LongTasks);
        Assert.Empty(vm.QuickTasks);
    }

    [Fact]
    public async Task Diagnostics_counts_match_visible_collections()
    {
        var vm = CreateViewModelWithTasks(
            SampleTask(1, WorkQueueBucketCodes.Quick),
            SampleTask(2, WorkQueueBucketCodes.Quick),
            SampleTask(3, WorkQueueBucketCodes.Medium),
            SampleTask(4, WorkQueueBucketCodes.Long),
            SampleTask(5, WorkQueueBucketCodes.Long));

        await vm.LoadAsync();

        Assert.Contains("Counts: Quick=2, Medium=1, Long=2", vm.DiagnosticsText, StringComparison.Ordinal);
        Assert.Equal(2, vm.QuickTasks.Count);
        Assert.Equal(1, vm.MediumTasks.Count);
        Assert.Equal(2, vm.LongTasks.Count);
    }

    [Fact]
    public void Task_workbench_task_grid_area_uses_star_height()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchView.xaml");
        var tabSection = ExtractBetween(xaml, "<!-- Task buckets: main content area -->", "<!-- Resolve preview");

        Assert.Contains("Height=\"*\" MinHeight=\"200\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"4\"", tabSection, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment=\"Stretch\"", tabSection, StringComparison.Ordinal);
        Assert.Contains("ListBox", tabSection, StringComparison.Ordinal);
        Assert.Contains("QuickTasks", tabSection, StringComparison.Ordinal);
    }

    [Fact]
    public void Task_workbench_uses_tall_narrow_floating_shape()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchView.xaml");
        var cs = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchView.xaml.cs");
        var factory = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskPanelReadOnlyWindowFactory.cs");

        Assert.Contains("Width=\"400\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"320\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ApplyTallNarrowLayout", cs, StringComparison.Ordinal);
        Assert.Contains("ShowOrActivate", factory, StringComparison.Ordinal);
        Assert.DoesNotContain("Width=\"1200\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_preview_does_not_consume_main_grid_space()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchView.xaml");

        Assert.Contains("<Expander Grid.Row=\"5\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsExpanded=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"160\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("GroupBox Grid.Row=\"6\" Header=\"Resolve preview", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_task_form_is_not_inline_in_main_workbench()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchView.xaml");
        Assert.DoesNotContain("הוספת משימה", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("NewTitle", xaml, StringComparison.Ordinal);
        Assert.Contains("AddTaskCommand", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Project_filter_off_loads_all_projects()
    {
        var vm = CreateViewModelWithTasks(
            SampleTask(1, WorkQueueBucketCodes.Quick, projectId: 1042),
            SampleTask(2, WorkQueueBucketCodes.Quick, projectId: 1041));

        await vm.LoadAsync();

        Assert.False(vm.FilterTasksByProjectEnabled);
        Assert.Equal(2, vm.QuickTasks.Count);
    }

    [Fact]
    public async Task Project_filter_on_filters_selected_project()
    {
        var vm = CreateViewModelWithTasks(
            SampleTask(1, WorkQueueBucketCodes.Quick, projectId: 1042),
            SampleTask(2, WorkQueueBucketCodes.Quick, projectId: 1041));

        await vm.LocalProjectFilterSelector!.InitializeAsync();
        var project = vm.LocalProjectFilterSelector.Projects.First(p => p.ProjectId == 1041);
        vm.LocalProjectFilterSelector.SelectProjectCommand.Execute(project);
        await vm.LoadAsync();

        Assert.True(vm.FilterTasksByProjectEnabled);
        Assert.Single(vm.QuickTasks);
        Assert.Equal(1041, vm.QuickTasks[0].ProjectId);
    }

    [Fact]
    public async Task Empty_due_to_project_filter_shows_clear_message()
    {
        var vm = CreateViewModelWithTasks(SampleTask(1, WorkQueueBucketCodes.Quick, projectId: 1042));

        await vm.LocalProjectFilterSelector!.InitializeAsync();
        var project = vm.LocalProjectFilterSelector.Projects.First(p => p.ProjectId == 1040);
        vm.LocalProjectFilterSelector.SelectProjectCommand.Execute(project);
        await vm.LoadAsync();

        Assert.Empty(vm.QuickTasks);
        Assert.Equal(TaskWorkbenchViewModel.EmptyProjectFilterStatusMessage, vm.StatusMessage);
    }

    [Fact]
    public void Task_workbench_has_no_LegacyBridge()
    {
        var csproj = ReadRepoFile("src/SiNet.App.Wpf/SiNet.App.Wpf.csproj");
        Assert.DoesNotContain("LegacyBridge", csproj, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void No_schema_change_or_migration()
    {
        var snapshot = ReadRepoFile("src/SiNet.Infrastructure.Sql/Migrations/SiNetSQLDbContextModelSnapshot.cs");
        Assert.Contains("IX_ProjectAssignment_UniqueOpenTask", snapshot, StringComparison.Ordinal);
    }

    private static TaskWorkbenchViewModel CreateViewModelWithTasks(params TaskSummaryDto[] tasks)
    {
        var query = new BucketTaskQuery(tasks);
        return new TaskWorkbenchViewModel(
            query,
            new StubNav(),
            null,
            new StubUser(12),
            null,
            null,
            null,
            new FakeProjectQueryService(),
            new FakeProjectFilterOptionsService(),
            null);
    }

    private static TaskSummaryDto SampleTask(int id, int bucket, int projectId = 1) =>
        new(
            TaskId: id,
            ProjectId: projectId,
            TaskTypeCode: "T",
            TaskTypeName: "Type",
            StatusCode: "Open",
            StatusName: "Open",
            IsOpen: true,
            AssignedToUserId: 12,
            AssignedToUserName: "User 12",
            WorkQueueBucket: bucket,
            WorkQueueBucketCode: WorkQueueBucketCodes.ToCode(bucket),
            WorkQueueBucketDisplayName: WorkQueueBucketCodes.ToDisplayName(bucket),
            WorkPriority: 1,
            DueDate: null,
            CreatedAt: null,
            LastTaskResultCode: null,
            Title: $"Task {id}",
            ComponentKey: null);

    private static string ExtractBetween(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0);
        var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex);
        return source[startIndex..endIndex];
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private sealed class StubUser(int id) : Application.Identity.ICurrentUserContext
    {
        public int? UserId { get; } = id;
    }

    private sealed class StubNav : ITaskNavigationService
    {
        public ValueTask<Application.WorkSurfaces.WorkSurfaceContext?> ResolveAsync(int taskId, CancellationToken ct) =>
            ValueTask.FromResult<Application.WorkSurfaces.WorkSurfaceContext?>(null);
    }

    private sealed class BucketTaskQuery(TaskSummaryDto[] tasks) : ITaskQueryService
    {
        public ValueTask<TaskSummaryDto?> GetByIdAsync(int taskId, CancellationToken ct) =>
            ValueTask.FromResult<TaskSummaryDto?>(null);

        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetTasksForProjectAsync(
            int projectId, bool includeClosed = false, int? workQueueBucket = null, CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>([]);

        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForUserAsync(
            int userId, int? workQueueBucket = null, CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>([]);

        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForUserByBucketAsync(
            int userId, int workQueueBucket, CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>(
                tasks.Where(t => t.WorkQueueBucket == workQueueBucket).ToList());

        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForAllUsersByBucketAsync(
            int workQueueBucket, CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>([]);
    }
}
