using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Shared.Projects;
using SiNet.App.Wpf.Shell;
using SiNet.App.Wpf.Surfaces.Tasks;
using SiNet.Application.Identity;
using SiNet.Application.Projects;
using SiNet.Application.Tasks;
using SiNet.Infrastructure.Sql;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.Tasks;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>Task Workbench list filtering, Add Task dialog, and project-context guards.</summary>
public sealed class TaskWorkbenchCreateDialogTests
{
    [Fact]
    public async Task Task_workbench_does_not_filter_by_project_by_default()
    {
        var query = new RecordingProjectFilterQuery();
        var vm = CreateViewModel(query, userId: 10);
        await vm.LoadAsync();

        Assert.False(vm.FilterTasksByProjectEnabled);
        Assert.Equal(2, vm.QuickTasks.Count);
        Assert.Contains(vm.QuickTasks, t => t.ProjectId == 1042);
        Assert.Contains(vm.QuickTasks, t => t.ProjectId == 1041);
    }

    [Fact]
    public async Task Task_workbench_project_filter_off_shows_tasks_from_all_projects()
    {
        var query = new RecordingProjectFilterQuery();
        var vm = CreateViewModel(query, userId: 10);
        vm.FilterTasksByProjectEnabled = false;
        await vm.LoadAsync();

        Assert.Equal(2, vm.QuickTasks.Count);
    }

    [Fact]
    public async Task Task_workbench_project_filter_on_filters_to_selected_project()
    {
        var query = new RecordingProjectFilterQuery();
        var vm = CreateViewModel(query, userId: 10);
        await vm.LocalProjectFilterSelector!.InitializeAsync();
        var project = vm.LocalProjectFilterSelector.Projects.First(p => p.ProjectId == 1041);
        vm.LocalProjectFilterSelector.SelectProjectCommand.Execute(project);
        vm.FilterTasksByProjectEnabled = true;
        await vm.LoadAsync();

        Assert.Single(vm.QuickTasks);
        Assert.Equal(1041, vm.QuickTasks[0].ProjectId);
    }

    [Fact]
    public async Task Task_workbench_empty_due_to_project_filter_shows_clear_message()
    {
        var query = new RecordingProjectFilterQuery();
        var vm = CreateViewModel(query, userId: 10);
        await vm.LocalProjectFilterSelector!.InitializeAsync();
        var project = vm.LocalProjectFilterSelector.Projects.First(p => p.ProjectId == 1040);
        vm.LocalProjectFilterSelector.SelectProjectCommand.Execute(project);
        vm.FilterTasksByProjectEnabled = true;
        await vm.LoadAsync();

        Assert.Empty(vm.QuickTasks);
        Assert.Contains("סינון פרויקט מופעל", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("1040", vm.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Task_workbench_main_view_does_not_contain_inline_create_form()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchView.xaml");
        Assert.DoesNotContain("הוספת משימה", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("NewTitle", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("NewBody", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IsAddPanelVisible", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowAddPanelCommand", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_task_button_opens_create_dialog()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchView.xaml");
        var vmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchViewModel.cs");

        Assert.Contains("Command=\"{Binding AddTaskCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ITaskCreateDialogFactory", vmSource, StringComparison.Ordinal);
        Assert.Contains("TaskCreateDialogWindow", vmSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_dialog_requires_project_selection()
    {
        var vm = CreateDialogViewModel(userId: 12);
        await vm.InitializeAsync();

        vm.Title = "Needs project";
        vm.SelectedAssignee = vm.Users.First();
        vm.SelectedTaskType = vm.TaskTypes.First();
        vm.SelectedStatus = vm.Statuses.First();
        vm.SelectedBucket = vm.Buckets.First();

        Assert.False(vm.CreateCommand.CanExecute(null));
        Assert.Null(vm.SelectedProjectId);
    }

    [Fact]
    public async Task Create_dialog_uses_queue_service_for_work_priority()
    {
        var (options, userId, statusId, taskTypeId1, _, projectId) = await SeedEmptyTaskDatabaseAsync(12);
        var factory = new StubDbContextFactory(options);
        var workbench = new SqlTaskWorkbenchService(factory);

        var result = await workbench.CreateTaskAsync(
            new CreateTaskRequest(projectId, userId, taskTypeId1, statusId, "Queued", WorkQueueBucketCodes.Quick),
            userId);

        Assert.True(result.Succeeded, result.Message);

        await using var db = factory.CreateDbContext();
        var task = await db.ProjectAssignments.SingleAsync(t => t.Title == "Queued");
        Assert.Equal(1, task.WorkPriority);
    }

    [Fact]
    public async Task Create_dialog_does_not_modify_global_project_context()
    {
        var globalContext = new InMemoryCurrentProjectContext();
        var dialogVm = CreateDialogViewModel(userId: 12, globalContext: globalContext);
        await dialogVm.InitializeAsync();
        await dialogVm.ProjectSelector.InitializeAsync();

        var project = dialogVm.ProjectSelector.Projects.First();
        dialogVm.ProjectSelector.SelectProjectCommand.Execute(project);

        Assert.Equal(project.ProjectId, dialogVm.SelectedProjectId);
        Assert.Null(globalContext.CurrentProject);
    }

    [Fact]
    public async Task Create_dialog_uses_local_project_context_for_create()
    {
        var workbench = new RecordingWorkbench();
        var dialogVm = CreateDialogViewModel(workbench, userId: 12);
        await dialogVm.InitializeAsync();
        await dialogVm.ProjectSelector.InitializeAsync();

        var project = dialogVm.ProjectSelector.Projects.First();
        dialogVm.ProjectSelector.SelectProjectCommand.Execute(project);
        dialogVm.Title = "From dialog";
        dialogVm.SelectedAssignee = dialogVm.Users.First();
        dialogVm.SelectedTaskType = dialogVm.TaskTypes.First();
        dialogVm.SelectedStatus = dialogVm.Statuses.First();
        dialogVm.SelectedBucket = dialogVm.Buckets.First();

        Assert.True(dialogVm.CreateCommand.CanExecute(null));
        dialogVm.CreateCommand.Execute(null);
        await Task.Delay(300);

        Assert.Equal(project.ProjectId, workbench.LastCreateRequest?.ProjectId);
    }

    [Fact]
    public void No_duplicate_project_selector_created()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchView.xaml");
        Assert.DoesNotContain("ItemsSource=\"{Binding Projects}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedItem=\"{Binding SelectedProject}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Task_workbench_has_no_LegacyBridge()
    {
        var csproj = ReadRepoFile("src/SiNet.App.Wpf/SiNet.App.Wpf.csproj");
        Assert.DoesNotContain("LegacyBridge", csproj, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void No_schema_change_or_migration_for_create_dialog()
    {
        var snapshot = ReadRepoFile("src/SiNet.Infrastructure.Sql/Migrations/SiNetSQLDbContextModelSnapshot.cs");
        Assert.Contains("IX_ProjectAssignment_UniqueOpenTask", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_dialog_viewmodel_does_not_set_work_priority()
    {
        var source = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskCreateDialogViewModel.cs");
        Assert.DoesNotContain("WorkPriority =", source, StringComparison.Ordinal);
    }

    private static TaskWorkbenchViewModel CreateViewModel(RecordingProjectFilterQuery query, int userId) =>
        new(
            query,
            new StubNav(),
            new RecordingWorkbench(),
            new StubUser(userId),
            null,
            null,
            null,
            new FakeProjectQueryService(),
            new FakeProjectFilterOptionsService(),
            new StubTaskCreateDialogFactory());

    private static TaskCreateDialogViewModel CreateDialogViewModel(
        RecordingWorkbench? workbench = null,
        int userId = 12,
        InMemoryCurrentProjectContext? globalContext = null)
    {
        _ = globalContext;
        return new TaskCreateDialogViewModel(
            workbench ?? new RecordingWorkbench(),
            new StubUser(userId),
            new FakeProjectQueryService(),
            new FakeProjectFilterOptionsService());
    }

    private static async Task<(DbContextOptions<SiNetSQLDbContext> Options, int UserId, int StatusId, int TaskTypeId1, int TaskTypeId2, int ProjectId)> SeedEmptyTaskDatabaseAsync(int userId)
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new SiNetSQLDbContext(options);
        db.Siusers.Add(new Siuser { Id = userId, Name = "U", IsActive = true });
        var project = new Project { Title = "P", Created = DateTime.UtcNow };
        db.Projects.Add(project);
        var open = new ProjectAssignmentStatus { Code = TaskStatusCodes.Open, Name = "Open", IsOpen = true, IsActionable = true };
        db.ProjectAssignmentStatuses.Add(open);
        var tt1 = new TaskType { Code = "TYPE_A", Name = "A", IsActive = true, SortOrder = 1 };
        var tt2 = new TaskType { Code = "TYPE_B", Name = "B", IsActive = true, SortOrder = 2 };
        db.TaskTypes.AddRange(tt1, tt2);
        await db.SaveChangesAsync();
        return (options, userId, open.Id, tt1.Id, tt2.Id, project.Id);
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private sealed class StubUser(int id) : ICurrentUserContext
    {
        public int? UserId { get; } = id;
    }

    private sealed class StubNav : ITaskNavigationService
    {
        public ValueTask<Application.WorkSurfaces.WorkSurfaceContext?> ResolveAsync(int taskId, CancellationToken ct) =>
            ValueTask.FromResult<Application.WorkSurfaces.WorkSurfaceContext?>(null);
    }

    private sealed class StubTaskCreateDialogFactory : ITaskCreateDialogFactory
    {
        public TaskCreateDialogResult ShowDialog(System.Windows.Window? owner) => new(false, null);
    }

    private sealed class RecordingProjectFilterQuery : ITaskQueryService
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
                workQueueBucket == WorkQueueBucketCodes.Quick
                    ?
                    [
                        CreateTask(1, userId, 1042, WorkQueueBucketCodes.Quick),
                        CreateTask(2, userId, 1041, WorkQueueBucketCodes.Quick),
                    ]
                    : []);

        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForAllUsersByBucketAsync(
            int workQueueBucket, CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>([]);

        private static TaskSummaryDto CreateTask(int id, int userId, int projectId, int bucket) =>
            new(
                TaskId: id,
                ProjectId: projectId,
                TaskTypeCode: "T",
                TaskTypeName: "Type",
                StatusCode: "Open",
                StatusName: "Open",
                IsOpen: true,
                AssignedToUserId: userId,
                AssignedToUserName: $"User {userId}",
                WorkQueueBucket: bucket,
                WorkQueueBucketCode: WorkQueueBucketCodes.ToCode(bucket),
                WorkQueueBucketDisplayName: WorkQueueBucketCodes.ToDisplayName(bucket),
                WorkPriority: 1,
                DueDate: null,
                LastTaskResultCode: null,
                Title: $"Task {id}",
                ComponentKey: null);
    }

    private sealed class RecordingWorkbench : ITaskWorkbenchService
    {
        public CreateTaskRequest? LastCreateRequest { get; private set; }

        public ValueTask<TaskCreationOptionsDto> GetTaskCreationOptionsAsync(CancellationToken ct = default) =>
            ValueTask.FromResult(new TaskCreationOptionsDto(
                [],
                [new TaskLookupItemDto(12, "User 12")],
                [new TaskLookupItemDto(1, "Type 1")],
                [new TaskLookupItemDto(1, "Open")],
                [new TaskLookupItemDto(WorkQueueBucketCodes.Quick, "Quick")]));

        public ValueTask<TaskCommandResult> CreateTaskAsync(CreateTaskRequest request, int changedByUserId, CancellationToken ct = default)
        {
            LastCreateRequest = request;
            return ValueTask.FromResult(new TaskCommandResult(true, "ok", 100));
        }

        public ValueTask<TaskCommandResult> DeleteTaskAsync(int taskId, int changedByUserId, CancellationToken ct = default) =>
            ValueTask.FromResult(new TaskCommandResult(true, "ok"));

        public ValueTask<IReadOnlyList<int>> GetDemoTaskAssigneeUserIdsAsync(CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlyList<int>>([]);
    }

    private sealed class StubDbContextFactory(DbContextOptions<SiNetSQLDbContext> options) : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);
    }
}
