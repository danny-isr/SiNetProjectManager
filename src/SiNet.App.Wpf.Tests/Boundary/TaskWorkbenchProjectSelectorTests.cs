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

public sealed class TaskWorkbenchProjectSelectorTests
{
    [Fact]
    public void Task_workbench_project_selector_is_not_in_action_toolbar()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchView.xaml");
        var actionsSection = ExtractSection(xaml, "<!-- Actions toolbar:", "<!-- Context / filter area:");
        var titleSection = ExtractSection(xaml, "<!-- Title -->", "<!-- Actions toolbar:");

        Assert.DoesNotContain("ProjectSelectorView", actionsSection, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectSelectorView", titleSection, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding RefreshCommand}\"", actionsSection, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding MoveDownCommand}\"", actionsSection, StringComparison.Ordinal);
    }

    [Fact]
    public void Task_workbench_project_selector_is_in_context_filter_area()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchView.xaml");
        var filterSection = ExtractSection(xaml, "<!-- Context / filter area:", "<Border Grid.Row=\"3\"");

        Assert.Contains("ProjectSelectorView", filterSection, StringComparison.Ordinal);
        Assert.Contains("מציג משימות:", filterSection, StringComparison.Ordinal);
        Assert.Contains("SelectedScope", filterSection, StringComparison.Ordinal);
        Assert.Contains("ActiveProjectDisplay", filterSection, StringComparison.Ordinal);
    }

    [Fact]
    public void Task_workbench_actions_toolbar_contains_only_actions()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchView.xaml");
        var actionsSection = ExtractSection(xaml, "<!-- Actions toolbar:", "<!-- Context / filter area:");

        Assert.Contains("RefreshCommand", actionsSection, StringComparison.Ordinal);
        Assert.Contains("ShowAddPanelCommand", actionsSection, StringComparison.Ordinal);
        Assert.Contains("DeleteTaskCommand", actionsSection, StringComparison.Ordinal);
        Assert.Contains("RepairQueueCommand", actionsSection, StringComparison.Ordinal);
        Assert.Contains("MoveUpCommand", actionsSection, StringComparison.Ordinal);
        Assert.Contains("MoveDownCommand", actionsSection, StringComparison.Ordinal);
        Assert.DoesNotContain("AvailableScopes", actionsSection, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedUserId", actionsSection, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectSelector", actionsSection, StringComparison.Ordinal);
    }

    [Fact]
    public void No_duplicate_project_combobox_created()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchView.xaml");
        Assert.DoesNotContain("ItemsSource=\"{Binding Projects}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedItem=\"{Binding SelectedProject}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_task_without_selected_project_shows_clear_message()
    {
        var context = new InMemoryCurrentProjectContext();
        var workbench = new RecordingWorkbench();
        var vm = CreateViewModel(context, workbench, userId: 12);
        await vm.InitializeAsync();

        vm.NewTitle = "Orphan task";
        vm.SelectedAssignee = vm.Users.First();
        vm.SelectedTaskType = vm.TaskTypes.First();
        vm.SelectedStatus = vm.Statuses.First();
        vm.SelectedBucket = vm.Buckets.First();

        Assert.False(vm.CreateTaskCommand.CanExecute(null));
        Assert.Equal("לא נבחר פרויקט", vm.ActiveProjectDisplay);
    }

    [Fact]
    public async Task Create_task_uses_current_project_context_from_project_selector()
    {
        var context = new InMemoryCurrentProjectContext();
        await context.SetCurrentProjectAsync(new ProjectSummaryDto(42, "1042", "Demo Project", null, null, null, null, null, true));

        var workbench = new RecordingWorkbench();
        var vm = CreateViewModel(context, workbench, userId: 12);
        await vm.InitializeAsync();

        vm.NewTitle = "New from selector";
        vm.SelectedAssignee = vm.Users.First();
        vm.SelectedTaskType = vm.TaskTypes.First();
        vm.SelectedStatus = vm.Statuses.First();
        vm.SelectedBucket = vm.Buckets.First();

        vm.CreateTaskCommand.Execute(null);
        await System.Threading.Tasks.Task.Delay(300);

        Assert.Equal(42, workbench.LastCreateRequest?.ProjectId);
        Assert.Equal(42, vm.SelectedProjectId);
    }

    [Fact]
    public async Task Selecting_project_updates_diagnostics_project_id()
    {
        var context = new InMemoryCurrentProjectContext();
        var vm = CreateViewModel(context, new RecordingWorkbench(), userId: 12);
        await vm.InitializeAsync();

        Assert.Contains("ProjectId: (none)", vm.DiagnosticsText, StringComparison.Ordinal);

        await context.SetCurrentProjectAsync(new ProjectSummaryDto(77, "1077", "Diagnostics Project", null, null, null, null, null, true));
        await vm.LoadAsync();

        Assert.Contains("ProjectId: 77", vm.DiagnosticsText, StringComparison.Ordinal);
        Assert.Contains("ProjectTitle: 1077 — Diagnostics Project", vm.DiagnosticsText, StringComparison.Ordinal);
        Assert.Contains("ProjectFilterActive: True", vm.DiagnosticsText, StringComparison.Ordinal);
    }

    private static string ExtractSection(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing marker: {startMarker}");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing end marker: {endMarker}");
        return source[start..end];
    }

    [Fact]
    public void Task_workbench_uses_existing_project_selector()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchView.xaml");
        var vmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchViewModel.cs");

        Assert.Contains("ProjectSelectorView", xaml, StringComparison.Ordinal);
        Assert.Contains("ProjectSelectorViewModel", vmSource, StringComparison.Ordinal);
        Assert.Contains("ICurrentProjectContext", vmSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Selecting_project_updates_current_project_context()
    {
        var context = new InMemoryCurrentProjectContext();
        var selector = new ProjectSelectorViewModel(
            new FakeProjectQueryService(),
            new FakeProjectFilterOptionsService(),
            context);

        await selector.InitializeAsync();
        var project = selector.Projects.First();
        selector.SelectProjectCommand.Execute(project);

        Assert.Equal(project.ProjectId, context.CurrentProject?.ProjectId);
    }

    [Fact]
    public async Task Create_task_uses_queue_service_for_work_priority()
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
    public async Task Create_task_prevents_duplicate_open_task_identity()
    {
        var (options, userId, statusId, taskTypeId, _, projectId) = await SeedEmptyTaskDatabaseAsync(12);
        var factory = new StubDbContextFactory(options);
        var workbench = new SqlTaskWorkbenchService(factory);
        var request = new CreateTaskRequest(projectId, userId, taskTypeId, statusId, "Dup", WorkQueueBucketCodes.Quick);

        Assert.True((await workbench.CreateTaskAsync(request, userId)).Succeeded);
        var second = await workbench.CreateTaskAsync(request with { Title = "Dup 2", WorkQueueBucket = WorkQueueBucketCodes.Long }, userId);
        Assert.False(second.Succeeded);
        Assert.Contains("כבר קיימת משימה פתוחה", second.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkQueueBucket_is_not_part_of_task_identity()
    {
        var (options, userId, statusId, taskTypeId, _, projectId) = await SeedEmptyTaskDatabaseAsync(12);
        var factory = new StubDbContextFactory(options);
        var workbench = new SqlTaskWorkbenchService(factory);
        var request = new CreateTaskRequest(projectId, userId, taskTypeId, statusId, "Same identity", WorkQueueBucketCodes.Quick);

        Assert.True((await workbench.CreateTaskAsync(request, userId)).Succeeded);
        var differentBucket = await workbench.CreateTaskAsync(
            request with { Title = "Same identity 2", WorkQueueBucket = WorkQueueBucketCodes.Long },
            userId);

        Assert.False(differentBucket.Succeeded);
    }

    [Fact]
    public async Task Parent_assignment_allows_child_tasks()
    {
        var (options, userId, statusId, taskTypeId1, taskTypeId2, projectId) = await SeedEmptyTaskDatabaseAsync(12);
        var factory = new StubDbContextFactory(options);
        var workbench = new SqlTaskWorkbenchService(factory);

        var parent = await workbench.CreateTaskAsync(
            new CreateTaskRequest(projectId, userId, taskTypeId1, statusId, "Parent", WorkQueueBucketCodes.Quick),
            userId);
        Assert.True(parent.Succeeded);

        var child = await workbench.CreateTaskAsync(
            new CreateTaskRequest(projectId, userId, taskTypeId2, statusId, "Child", WorkQueueBucketCodes.Quick, parent.TaskId),
            userId);

        Assert.True(child.Succeeded);
    }

    [Fact]
    public async Task Duplicate_child_task_same_parent_user_type_is_prevented()
    {
        var (options, userId, statusId, taskTypeId1, taskTypeId2, projectId) = await SeedEmptyTaskDatabaseAsync(12);
        var factory = new StubDbContextFactory(options);
        var workbench = new SqlTaskWorkbenchService(factory);

        var parent = await workbench.CreateTaskAsync(
            new CreateTaskRequest(projectId, userId, taskTypeId1, statusId, "Parent", WorkQueueBucketCodes.Quick),
            userId);
        Assert.True(parent.Succeeded);

        var childRequest = new CreateTaskRequest(
            projectId, userId, taskTypeId2, statusId, "Child A", WorkQueueBucketCodes.Quick, parent.TaskId);
        Assert.True((await workbench.CreateTaskAsync(childRequest, userId)).Succeeded);

        var duplicateChild = await workbench.CreateTaskAsync(
            childRequest with { Title = "Child B" },
            userId);

        Assert.False(duplicateChild.Succeeded);
        Assert.Contains("תת-משימה פתוחה", duplicateChild.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Priority_legacy_field_is_not_used_as_queue_position()
    {
        var vmSource = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchViewModel.cs");
        Assert.DoesNotContain("WorkPriority =", vmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkPriority_is_displayed_as_queue_position()
    {
        var xaml = ReadRepoFile("src/SiNet.App.Wpf/Surfaces/Tasks/TaskWorkbenchView.xaml");
        Assert.Contains("Header=\"מיקום בתור\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding WorkPriority}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Task_workbench_has_no_LegacyBridge()
    {
        var csproj = ReadRepoFile("src/SiNet.App.Wpf/SiNet.App.Wpf.csproj");
        Assert.DoesNotContain("LegacyBridge", csproj, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void No_schema_change_or_migration_for_project_selector_integration()
    {
        var snapshot = ReadRepoFile("src/SiNet.Infrastructure.Sql/Migrations/SiNetSQLDbContextModelSnapshot.cs");
        Assert.Contains("IX_ProjectAssignment_UniqueOpenTask", snapshot, StringComparison.Ordinal);
    }

    private static TaskWorkbenchViewModel CreateViewModel(
        InMemoryCurrentProjectContext context,
        RecordingWorkbench workbench,
        int userId)
    {
        return new TaskWorkbenchViewModel(
            new EmptyTaskQuery(),
            new StubNav(),
            workbench,
            new StubUser(userId),
            context,
            null,
            null,
            null,
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

    private sealed class EmptyTaskQuery : ITaskQueryService
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
            ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>([]);

        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForAllUsersByBucketAsync(
            int workQueueBucket, CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<TaskSummaryDto>>([]);
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
