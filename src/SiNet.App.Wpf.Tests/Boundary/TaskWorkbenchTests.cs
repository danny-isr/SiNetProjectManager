using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Surfaces.Tasks;
using SiNet.Application.DevTools;
using SiNet.Application.Identity;
using SiNet.Application.Tasks;
using SiNet.Infrastructure.Sql;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.DevTools;
using SiNet.Infrastructure.Sql.Services.Tasks;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

public sealed class TaskWorkbenchTests
{
    [Fact]
    public async Task Task_panel_diagnostics_show_current_user_and_project()
    {
        var vm = new TaskWorkbenchViewModel(
            new StubQuery([]),
            new StubNav(),
            null,
            new StubUser(12),
            null);

        await vm.LoadAsync();

        Assert.Equal("MyTasks", vm.LoadMode);
        Assert.Equal("12", vm.CurrentUserIdDisplay);
        Assert.Contains("Mode: MyTasks", vm.DiagnosticsText, StringComparison.Ordinal);
        Assert.Contains("CurrentUserId: 12", vm.DiagnosticsText, StringComparison.Ordinal);
        Assert.Contains("Quick=0", vm.DiagnosticsText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Task_panel_loads_existing_db_tasks_for_current_user()
    {
        var (options, userId) = await SeedDatabaseWithDemoTasksAsync(12);
        var factory = new StubDbContextFactory(options);
        Assert.True((await new SqlTaskDemoSeedService(factory).SeedAsync(new DemoTaskSeedOptions { TargetUserId = 12 })).Succeeded);

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<SiNetSQLDbContext>>(factory);
        services.AddSiNetProcessBackbone();
        await using var provider = services.BuildServiceProvider();

        var vm = new TaskWorkbenchViewModel(
            provider.GetRequiredService<ITaskQueryService>(),
            provider.GetRequiredService<ITaskNavigationService>(),
            provider.GetRequiredService<ITaskWorkbenchService>(),
            new StubUser(12),
            null);

        await vm.LoadAsync();

        Assert.Equal("MyTasks", vm.LoadMode);
        Assert.Equal("12", vm.CurrentUserIdDisplay);
        Assert.Contains("SqlTaskQueryService", vm.QueryServiceName, StringComparison.Ordinal);
        Assert.True(vm.QuickTasks.Count + vm.MediumTasks.Count + vm.LongTasks.Count > 0);
    }

    [Fact]
    public async Task Task_panel_reports_zero_counts_when_no_tasks()
    {
        var vm = new TaskWorkbenchViewModel(
            new StubQuery([]),
            new StubNav(),
            new StubWorkbench([]),
            new StubUser(99),
            null);

        await vm.LoadAsync();

        Assert.Contains("UserId=99", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("Quick=0", vm.DiagnosticsText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SqlTaskQueryService_returns_demo_tasks_for_user_12()
    {
        var (options, _) = await SeedDatabaseWithDemoTasksAsync(12);
        var factory = new StubDbContextFactory(options);
        await new SqlTaskDemoSeedService(factory).SeedAsync(new DemoTaskSeedOptions { TargetUserId = 12 });

        await using var db = factory.CreateDbContext();
        var svc = new SqlTaskQueryService(factory);
        var quick = await svc.GetOpenTasksForUserByBucketAsync(12, WorkQueueBucketCodes.Quick, CancellationToken.None);

        Assert.NotEmpty(quick);
        Assert.All(quick, t => Assert.Equal(12, t.AssignedToUserId));
    }

    [Fact]
    public async Task SqlTaskQueryService_filters_by_open_status_correctly()
    {
        var options = await SeedDatabaseWithClosedTaskAsync();
        var factory = new StubDbContextFactory(options);
        var svc = new SqlTaskQueryService(factory);

        var open = await svc.GetOpenTasksForUserByBucketAsync(12, WorkQueueBucketCodes.Quick, CancellationToken.None);
        Assert.Empty(open);
    }

    [Fact]
    public void Task_panel_uses_real_ITaskQueryService_not_design_service()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<SiNetSQLDbContext>>(
            new StubDbContextFactory(new DbContextOptionsBuilder<SiNetSQLDbContext>().UseInMemoryDatabase("x").Options));
        services.AddSiNetProcessBackbone();
        services.AddTransient<TaskWorkbenchViewModel>();
        using var provider = services.BuildServiceProvider();

        var vm = provider.GetRequiredService<TaskWorkbenchViewModel>();
        Assert.Equal("SqlTaskQueryService", vm.QueryServiceName);
    }

    [Fact]
    public async Task Task_creation_appends_to_end_of_user_bucket()
    {
        var (options, userId, statusId, taskTypeId, _, projectId) = await SeedEmptyTaskDatabaseAsync(12);
        var factory = new StubDbContextFactory(options);
        var svc = new SqlTaskWorkbenchService(factory);

        var result = await svc.CreateTaskAsync(
            new CreateTaskRequest(projectId, userId, taskTypeId, statusId, "New task", WorkQueueBucketCodes.Quick),
            userId);

        Assert.True(result.Succeeded, result.Message);

        await using var db = factory.CreateDbContext();
        var task = await db.ProjectAssignments.SingleAsync(t => t.Title == "New task");
        Assert.Equal(1, task.WorkPriority);
    }

    [Fact]
    public async Task Task_creation_prevents_unique_open_task_violation()
    {
        var (options, userId, statusId, taskTypeId, _, projectId) = await SeedEmptyTaskDatabaseAsync(12);
        var factory = new StubDbContextFactory(options);
        var svc = new SqlTaskWorkbenchService(factory);
        var request = new CreateTaskRequest(projectId, userId, taskTypeId, statusId, "Dup", WorkQueueBucketCodes.Quick);

        Assert.True((await svc.CreateTaskAsync(request, userId)).Succeeded);
        var second = await svc.CreateTaskAsync(request with { Title = "Dup 2" }, userId);
        Assert.False(second.Succeeded);
        Assert.Contains("כבר קיימת משימה פתוחה", second.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Task_delete_removes_task_and_compacts_bucket()
    {
        var (options, userId, statusId, taskTypeId1, taskTypeId2, projectId) = await SeedEmptyTaskDatabaseAsync(12);
        var factory = new StubDbContextFactory(options);
        var svc = new SqlTaskWorkbenchService(factory);

        var first = await svc.CreateTaskAsync(new CreateTaskRequest(projectId, userId, taskTypeId1, statusId, "T1", WorkQueueBucketCodes.Quick), userId);
        var second = await svc.CreateTaskAsync(new CreateTaskRequest(projectId, userId, taskTypeId2, statusId, "T2", WorkQueueBucketCodes.Quick), userId);
        Assert.True(first.Succeeded && second.Succeeded);

        await using var db = factory.CreateDbContext();
        var t2 = await db.ProjectAssignments.SingleAsync(t => t.Title == "T2");
        Assert.Equal(2, t2.WorkPriority);

        Assert.True((await svc.DeleteTaskAsync(first.TaskId!.Value, userId)).Succeeded);

        await db.Entry(t2).ReloadAsync();
        Assert.Equal(1, t2.WorkPriority);
    }

    [Fact]
    public void Task_workbench_does_not_use_legacy_TaskPanel()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "src", "SiNet.App.Wpf", "Surfaces", "Tasks", "TaskWorkbenchViewModel.cs"));
        Assert.DoesNotContain("TaskPanelViewModel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LegacyBridge", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SqlTaskQueryService_returns_all_users_open_tasks_by_bucket()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using (var db = new SiNetSQLDbContext(options))
        {
            db.Siusers.AddRange(
                new Siuser { Id = 12, Name = "U12", IsActive = true },
                new Siuser { Id = 99, Name = "U99", IsActive = true });
            db.Projects.Add(new Project { Id = 1, Title = "P1", Created = DateTime.UtcNow });
            AddOpenStatuses(db);
            var tt = new TaskType { Code = "T1", Name = "T1", IsActive = true, SortOrder = 1 };
            db.TaskTypes.Add(tt);
            await db.SaveChangesAsync();
            var openId = db.ProjectAssignmentStatuses.First(s => s.Code == TaskStatusCodes.Open).Id;
            db.ProjectAssignments.AddRange(
                new ProjectAssignment { Title = "Q12", ProjectId = 1, AssignedToId = 12, StatusId = openId, TaskTypeId = tt.Id, WorkQueueBucket = WorkQueueBucketCodes.Quick, Created = DateTime.UtcNow },
                new ProjectAssignment { Title = "Q99", ProjectId = 1, AssignedToId = 99, StatusId = openId, TaskTypeId = tt.Id, WorkQueueBucket = WorkQueueBucketCodes.Quick, Created = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var svc = new SqlTaskQueryService(new StubDbContextFactory(options));
        var quick = await svc.GetOpenTasksForAllUsersByBucketAsync(WorkQueueBucketCodes.Quick, CancellationToken.None);

        Assert.Equal(2, quick.Count);
        Assert.Contains(quick, t => t.AssignedToUserId == 12);
        Assert.Contains(quick, t => t.AssignedToUserId == 99);
    }

    [Fact]
    public void SqlTaskQueryService_sorts_in_memory_not_in_sql()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiNet.Infrastructure.Sql", "Services", "Tasks", "SqlTaskQueryService.cs"));
        Assert.Contains("TaskQueryOrdering.SortByQueueOrder", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ThenBy(t => t.DueDate", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Task_workbench_has_no_LegacyBridge()
    {
        var csproj = File.ReadAllText(Path.Combine(RepoRoot, "src", "SiNet.App.Wpf", "SiNet.App.Wpf.csproj"));
        Assert.DoesNotContain("LegacyBridge", csproj, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void No_migration_or_schema_change_for_workbench()
    {
        var snapshot = File.ReadAllText(Path.Combine(RepoRoot, "src", "SiNet.Infrastructure.Sql", "Migrations", "SiNetSQLDbContextModelSnapshot.cs"));
        Assert.Contains("IX_ProjectAssignment_UniqueOpenTask", snapshot, StringComparison.Ordinal);
    }

    private static async Task<(DbContextOptions<SiNetSQLDbContext> Options, int UserId)> SeedDatabaseWithDemoTasksAsync(int userId)
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new SiNetSQLDbContext(options);
        db.Siusers.Add(new Siuser { Id = userId, Name = $"User{userId}", IsActive = true });
        db.Projects.Add(new Project { Title = "P1", Created = DateTime.UtcNow });
        AddOpenStatuses(db);
        db.TaskTypes.Add(new TaskType { Code = TaskTypeCodes.FileInitialInquiry, Name = "File", IsActive = true, SortOrder = 1 });
        await db.SaveChangesAsync();
        return (options, userId);
    }

    private static async Task<DbContextOptions<SiNetSQLDbContext>> SeedDatabaseWithClosedTaskAsync()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new SiNetSQLDbContext(options);
        db.Siusers.Add(new Siuser { Id = 12, Name = "U12", IsActive = true });
        db.Projects.Add(new Project { Id = 1, Title = "P1", Created = DateTime.UtcNow });
        var open = new ProjectAssignmentStatus { Code = TaskStatusCodes.Open, Name = "Open", IsOpen = true, IsActionable = true };
        var closed = new ProjectAssignmentStatus { Code = TaskStatusCodes.Completed, Name = "Done", IsOpen = false, IsActionable = false };
        db.ProjectAssignmentStatuses.AddRange(open, closed);
        var tt = new TaskType { Code = "T1", Name = "T1", IsActive = true, SortOrder = 1 };
        db.TaskTypes.Add(tt);
        await db.SaveChangesAsync();
        db.ProjectAssignments.Add(new ProjectAssignment
        {
            Title = "Closed task",
            ProjectId = 1,
            AssignedToId = 12,
            StatusId = closed.Id,
            TaskTypeId = tt.Id,
            WorkQueueBucket = WorkQueueBucketCodes.Quick,
            WorkPriority = null,
            Created = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return options;
    }

    private static async Task<(DbContextOptions<SiNetSQLDbContext> Options, int UserId, int StatusId, int TaskTypeId1, int TaskTypeId2, int ProjectId)> SeedEmptyTaskDatabaseAsync(int userId)
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new SiNetSQLDbContext(options);
        db.Siusers.Add(new Siuser { Id = userId, Name = "U", IsActive = true });
        var project = new Project { Title = "P", Created = DateTime.UtcNow };
        db.Projects.Add(project);
        AddOpenStatuses(db);
        var tt1 = new TaskType { Code = "TYPE_A", Name = "A", IsActive = true, SortOrder = 1 };
        var tt2 = new TaskType { Code = "TYPE_B", Name = "B", IsActive = true, SortOrder = 2 };
        db.TaskTypes.AddRange(tt1, tt2);
        await db.SaveChangesAsync();
        var statusId = db.ProjectAssignmentStatuses.First(s => s.Code == TaskStatusCodes.Open).Id;
        return (options, userId, statusId, tt1.Id, tt2.Id, project.Id);
    }

    private static void AddOpenStatuses(SiNetSQLDbContext db)
    {
        db.ProjectAssignmentStatuses.AddRange(
            new ProjectAssignmentStatus { Code = TaskStatusCodes.Open, Name = "Open", IsOpen = true, IsActionable = true },
            new ProjectAssignmentStatus { Code = TaskStatusCodes.InProgress, Name = "InProgress", IsOpen = true, IsActionable = true },
            new ProjectAssignmentStatus { Code = TaskStatusCodes.Completed, Name = "Completed", IsOpen = false, IsActionable = false });
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private sealed class StubDbContextFactory(DbContextOptions<SiNetSQLDbContext> options) : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);
    }

    private sealed class StubUser(int id) : ICurrentUserContext { public int? UserId { get; } = id; }

    private sealed class StubQuery(IReadOnlyList<TaskSummaryDto> items) : ITaskQueryService
    {
        public ValueTask<TaskSummaryDto?> GetByIdAsync(int taskId, CancellationToken ct) => ValueTask.FromResult<TaskSummaryDto?>(null);
        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetTasksForProjectAsync(int projectId, bool includeClosed = false, int? workQueueBucket = null, CancellationToken ct = default) => ValueTask.FromResult(items);
        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForUserAsync(int userId, int? workQueueBucket = null, CancellationToken ct = default) => ValueTask.FromResult(items);
        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForUserByBucketAsync(int userId, int workQueueBucket, CancellationToken ct) => ValueTask.FromResult(items);
        public ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForAllUsersByBucketAsync(int workQueueBucket, CancellationToken ct) => ValueTask.FromResult(items);
    }

    private sealed class StubNav : ITaskNavigationService
    {
        public ValueTask<Application.WorkSurfaces.WorkSurfaceContext?> ResolveAsync(int taskId, CancellationToken ct) =>
            ValueTask.FromResult<Application.WorkSurfaces.WorkSurfaceContext?>(null);
    }

    private sealed class StubWorkbench(IReadOnlyList<int> demoUsers) : ITaskWorkbenchService
    {
        public ValueTask<TaskCreationOptionsDto> GetTaskCreationOptionsAsync(CancellationToken ct = default) =>
            ValueTask.FromResult(new TaskCreationOptionsDto([], [], [], [], []));
        public ValueTask<TaskCommandResult> CreateTaskAsync(CreateTaskRequest request, int changedByUserId, CancellationToken ct = default) =>
            ValueTask.FromResult(new TaskCommandResult(true, "ok"));
        public ValueTask<TaskCommandResult> DeleteTaskAsync(int taskId, int changedByUserId, CancellationToken ct = default) =>
            ValueTask.FromResult(new TaskCommandResult(true, "ok"));
        public ValueTask<IReadOnlyList<int>> GetDemoTaskAssigneeUserIdsAsync(CancellationToken ct = default) =>
            ValueTask.FromResult(demoUsers);
    }
}
