using System.IO;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.App.Wpf.Surfaces.Tasks;
using SiNet.Application.Tasks;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

public sealed class TaskWorkbenchProjectFilterClearTests
{
    [Fact]
    public void Project_filter_clear_button_visible_when_project_selected()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchView.xaml");
        var filterSection = ExtractSection(xaml, "<!-- Context / filter area -->", "<Expander Grid.Row=\"3\"");

        Assert.Contains("ClearSelectedProjectCommand", filterSection, StringComparison.Ordinal);
        Assert.Contains("Content=\"נקה\"", filterSection, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding CanClearSelectedProject}\"", filterSection, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_filter_clear_button_disabled_or_hidden_when_no_project_selected()
    {
        var vm = CreateViewModelWithTasks(SampleTask(1, WorkQueueBucketCodes.Quick, 1042));

        Assert.False(vm.CanClearSelectedProject);
        Assert.False(vm.ClearSelectedProjectCommand.CanExecute(null));
    }

    [Fact]
    public async Task Clear_project_selection_sets_project_filter_off()
    {
        var vm = CreateViewModelWithTasks(
            SampleTask(1, WorkQueueBucketCodes.Quick, 1042),
            SampleTask(2, WorkQueueBucketCodes.Quick, 1041));

        await SelectProjectAsync(vm, 1041);
        Assert.True(vm.FilterTasksByProjectEnabled);

        vm.ClearSelectedProjectCommand.Execute(null);
        await vm.LoadAsync();

        Assert.False(vm.FilterTasksByProjectEnabled);
        Assert.Null(vm.SelectedProjectId);
        Assert.Null(vm.LocalProjectFilterSelector!.SelectedProject);
    }

    [Fact]
    public async Task Clear_project_selection_reload_tasks_from_all_projects()
    {
        var vm = CreateViewModelWithTasks(
            SampleTask(1, WorkQueueBucketCodes.Quick, 1042),
            SampleTask(2, WorkQueueBucketCodes.Quick, 1041));

        await SelectProjectAsync(vm, 1041);
        await vm.LoadAsync();
        Assert.Single(vm.QuickTasks);

        vm.ClearSelectedProjectCommand.Execute(null);
        await vm.LoadAsync();

        Assert.Equal(2, vm.QuickTasks.Count);
    }

    [Fact]
    public async Task Clearing_search_text_does_not_leave_misleading_filter_state()
    {
        var vm = CreateViewModelWithTasks(SampleTask(1, WorkQueueBucketCodes.Quick, 1042));
        await SelectProjectAsync(vm, 1042);

        vm.LocalProjectFilterSelector!.EditorText = string.Empty;

        Assert.True(vm.FilterTasksByProjectEnabled);
        Assert.Equal(1042, vm.SelectedProjectId);
        Assert.Contains("סינון לפי פרויקט: כן", vm.ProjectFilterDisplayText, StringComparison.Ordinal);
        Assert.Contains("1042", vm.ProjectFilterDisplayText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clear_project_selection_does_not_modify_global_current_project_context()
    {
        var globalContext = new InMemoryCurrentProjectContext();
        var globalProject = new Application.Projects.ProjectSummaryDto(
            999, "1999", "Global Project", null, null, null, null, null, true);
        await globalContext.SetCurrentProjectAsync(globalProject);

        var vm = CreateViewModelWithTasks(SampleTask(1, WorkQueueBucketCodes.Quick, 1042));
        await SelectProjectAsync(vm, 1042);

        vm.ClearSelectedProjectCommand.Execute(null);
        await vm.LoadAsync();

        Assert.Null(vm.SelectedProjectId);
        Assert.Equal(999, globalContext.CurrentProject?.ProjectId);
    }

    [Fact]
    public async Task Diagnostics_after_clear_show_all_projects()
    {
        var vm = CreateViewModelWithTasks(SampleTask(1, WorkQueueBucketCodes.Quick, 1042));
        await SelectProjectAsync(vm, 1042);
        await vm.LoadAsync();

        vm.ClearSelectedProjectCommand.Execute(null);
        await vm.LoadAsync();

        Assert.Contains("Project filter: off", vm.DiagnosticsText, StringComparison.Ordinal);
        Assert.Contains("כל הפרויקטים", vm.ProjectFilterDisplayText, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_selector_clear_selection_command_clears_local_context()
    {
        var context = new InMemoryCurrentProjectContext();
        var selector = new ProjectSelectorViewModel(
            new FakeProjectQueryService(),
            new FakeProjectFilterOptionsService(),
            context);

        var project = new Application.Projects.ProjectSummaryDto(
            1042, "1042", "North", null, null, null, null, null, true);
        selector.SelectProjectCommand.Execute(project);

        Assert.True(selector.CanClearSelection);
        selector.ClearSelectionCommand.Execute(null);

        Assert.Null(selector.SelectedProject);
        Assert.Null(context.CurrentProject);
        Assert.False(selector.CanClearSelection);
    }

    [Fact]
    public void No_duplicate_project_selector_created()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchView.xaml");
        Assert.DoesNotContain("ItemsSource=\"{Binding Projects}\"", xaml, StringComparison.Ordinal);
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

    private static async Task SelectProjectAsync(TaskWorkbenchViewModel vm, int projectId)
    {
        await vm.LocalProjectFilterSelector!.InitializeAsync();
        var project = vm.LocalProjectFilterSelector.Projects.First(p => p.ProjectId == projectId);
        vm.LocalProjectFilterSelector.SelectProjectCommand.Execute(project);
        await vm.LoadAsync();
    }

    private static TaskWorkbenchViewModel CreateViewModelWithTasks(params TaskSummaryDto[] tasks) =>
        new(
            new BucketTaskQuery(tasks),
            new StubNav(),
            null,
            new StubUser(12),
            null,
            null,
            null,
            new FakeProjectQueryService(),
            new FakeProjectFilterOptionsService(),
            null);

    private static TaskSummaryDto SampleTask(int id, int bucket, int projectId) =>
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

    private static string ExtractSection(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing marker: {startMarker}");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing end marker: {endMarker}");
        return source[start..end];
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
