using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.App.Wpf.Surfaces.Tasks;
using SiNet.App.Wpf.Tests.Support;
using SiNet.Application.Tasks;
using SiNet.Infrastructure.Sql;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.DevTools;
using SiNet.Infrastructure.Sql.Services.Tasks;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

public sealed class TaskQueueManagementTests
{
    [Fact]
    public async Task Queue_create_task_appends_to_end_of_user_bucket()
    {
        var (options, userId, statusId, taskTypeId1, taskTypeId2, projectId) = await SeedEmptyTaskDatabaseAsync(12);
        var factory = new StubDbContextFactory(options);
        var workbench = new SqlTaskWorkbenchService(factory, new StubWorkflowCommandService());

        await workbench.CreateTaskAsync(
            new CreateTaskRequest(projectId, userId, taskTypeId1, statusId, "T1", WorkQueueBucketCodes.Quick),
            userId);
        var second = await workbench.CreateTaskAsync(
            new CreateTaskRequest(projectId, userId, taskTypeId2, statusId, "T2", WorkQueueBucketCodes.Quick),
            userId);

        Assert.True(second.Succeeded);

        await using var db = factory.CreateDbContext();
        var priorities = await db.ProjectAssignments
            .Where(t => t.AssignedToId == userId && t.WorkQueueBucket == WorkQueueBucketCodes.Quick)
            .OrderBy(t => t.WorkPriority)
            .Select(t => t.WorkPriority)
            .ToListAsync();

        Assert.Equal([1, 2], priorities);
    }

    [Fact]
    public async Task Queue_delete_task_removes_and_compacts_bucket()
    {
        var (options, userId, statusId, taskTypeId1, taskTypeId2, projectId) = await SeedEmptyTaskDatabaseAsync(12);
        var factory = new StubDbContextFactory(options);
        var workbench = new SqlTaskWorkbenchService(factory, new StubWorkflowCommandService());

        var first = await workbench.CreateTaskAsync(new CreateTaskRequest(projectId, userId, taskTypeId1, statusId, "T1", WorkQueueBucketCodes.Quick), userId);
        await workbench.CreateTaskAsync(new CreateTaskRequest(projectId, userId, taskTypeId2, statusId, "T2", WorkQueueBucketCodes.Quick), userId);
        Assert.True(first.Succeeded);

        Assert.True((await workbench.DeleteTaskAsync(first.TaskId!.Value, userId)).Succeeded);

        await using var db = factory.CreateDbContext();
        var remaining = await db.ProjectAssignments.SingleAsync(t => t.Title == "T2");
        Assert.Equal(1, remaining.WorkPriority);
    }

    [Fact]
    public async Task Queue_reassign_moves_between_user_queues_and_compacts_old_queue()
    {
        var options = await SeedQueuedTasksForReassignAsync();
        var factory = new StubDbContextFactory(options);
        var queue = new SqlTaskQueueService(factory);

        var result = await queue.ReassignAsync(2, 20, 7);

        Assert.True(result.Succeeded);
        await using var db = factory.CreateDbContext();
        var moved = await db.ProjectAssignments.FindAsync(2);
        Assert.Equal(20, moved!.AssignedToId);
        Assert.Equal(2, moved.WorkPriority);
        Assert.Equal(1, (await db.ProjectAssignments.FindAsync(1))!.WorkPriority);
        Assert.Equal(1, (await db.ProjectAssignments.FindAsync(3))!.WorkPriority);
    }

    [Fact]
    public async Task Queue_change_bucket_moves_between_buckets_and_compacts_old_bucket()
    {
        var options = await SeedQueuedTasksForBucketChangeAsync();
        var factory = new StubDbContextFactory(options);
        var queue = new SqlTaskQueueService(factory);

        await queue.ChangeBucketAsync(2, WorkQueueBucketCodes.Long, 7);

        await using var db = factory.CreateDbContext();
        var moved = await db.ProjectAssignments.FindAsync(2);
        Assert.Equal(WorkQueueBucketCodes.Long, moved!.WorkQueueBucket);
        Assert.Equal(2, moved.WorkPriority);
        Assert.Equal(1, (await db.ProjectAssignments.FindAsync(1))!.WorkPriority);
    }

    [Fact]
    public async Task Queue_move_up_keeps_unique_priorities()
    {
        var options = await SeedThreeTaskQueueAsync(10, WorkQueueBucketCodes.Quick);
        var factory = new StubDbContextFactory(options);
        var queue = new SqlTaskQueueService(factory);

        var result = await queue.MoveUpAsync(2, 7);
        Assert.True(result.Succeeded);

        await using var db = factory.CreateDbContext();
        var priorities = await db.ProjectAssignments
            .Where(t => t.AssignedToId == 10 && t.WorkQueueBucket == WorkQueueBucketCodes.Quick)
            .OrderBy(t => t.WorkPriority)
            .Select(t => new { t.Id, t.WorkPriority })
            .ToListAsync();

        Assert.Equal(2, priorities[0].Id);
        Assert.Equal(1, priorities[0].WorkPriority);
        Assert.Equal(1, priorities[1].Id);
        Assert.Equal(2, priorities[1].WorkPriority);
        Assert.Equal(3, priorities[2].WorkPriority);
        Assert.Equal(priorities.Count, priorities.Select(p => p.WorkPriority).Distinct().Count());
    }

    [Fact]
    public async Task Queue_move_down_keeps_unique_priorities()
    {
        var options = await SeedThreeTaskQueueAsync(10, WorkQueueBucketCodes.Quick);
        var factory = new StubDbContextFactory(options);
        var queue = new SqlTaskQueueService(factory);

        var result = await queue.MoveDownAsync(1, 7);
        Assert.True(result.Succeeded);

        await using var db = factory.CreateDbContext();
        var priorities = await db.ProjectAssignments
            .Where(t => t.AssignedToId == 10 && t.WorkQueueBucket == WorkQueueBucketCodes.Quick)
            .OrderBy(t => t.WorkPriority)
            .Select(t => t.WorkPriority)
            .ToListAsync();

        Assert.Equal([1, 2, 3], priorities);
        Assert.Equal(priorities.Count, priorities.Distinct().Count());
    }

    [Fact]
    public async Task Queue_repair_assigns_priority_to_null_open_tasks()
    {
        var options = await SeedBrokenQueueAsync(nullPriority: true);
        var factory = new StubDbContextFactory(options);
        var queue = new SqlTaskQueueService(factory);

        var result = await queue.RepairQueueAsync(10, WorkQueueBucketCodes.Quick);

        Assert.True(result.NullPrioritiesFixed >= 1);
        await using var db = factory.CreateDbContext();
        Assert.All(
            db.ProjectAssignments.Where(t => t.AssignedToId == 10 && t.WorkQueueBucket == WorkQueueBucketCodes.Quick),
            t => Assert.NotNull(t.WorkPriority));
    }

    [Fact]
    public async Task Queue_repair_fixes_duplicate_priorities()
    {
        var options = await SeedBrokenQueueAsync(duplicatePriority: true);
        var factory = new StubDbContextFactory(options);
        var queue = new SqlTaskQueueService(factory);

        var result = await queue.RepairQueueAsync(10, WorkQueueBucketCodes.Quick);

        Assert.True(result.DuplicatePrioritiesFixed >= 1);
        await using var db = factory.CreateDbContext();
        var priorities = await db.ProjectAssignments
            .Where(t => t.AssignedToId == 10 && t.WorkQueueBucket == WorkQueueBucketCodes.Quick && t.WorkPriority != null)
            .Select(t => t.WorkPriority!.Value)
            .ToListAsync();
        Assert.Equal(priorities.Count, priorities.Distinct().Count());
    }

    [Fact]
    public async Task Queue_repair_closes_priority_gaps()
    {
        var options = await SeedBrokenQueueAsync(gapPriority: true);
        var factory = new StubDbContextFactory(options);
        var queue = new SqlTaskQueueService(factory);

        var result = await queue.RepairQueueAsync(10, WorkQueueBucketCodes.Quick);

        Assert.True(result.GapsClosed >= 1);
        await using var db = factory.CreateDbContext();
        var priorities = await db.ProjectAssignments
            .Where(t => t.AssignedToId == 10 && t.WorkQueueBucket == WorkQueueBucketCodes.Quick && t.WorkPriority != null)
            .OrderBy(t => t.WorkPriority)
            .Select(t => t.WorkPriority!.Value)
            .ToListAsync();
        Assert.Equal([1, 2], priorities);
    }

    [Fact]
    public async Task Queue_repair_groups_by_assigned_user_and_bucket()
    {
        var options = await SeedMultipleUserBucketQueuesAsync();
        var factory = new StubDbContextFactory(options);
        var queue = new SqlTaskQueueService(factory);

        var result = await queue.RepairAllQueuesAsync();

        Assert.True(result.BucketsProcessed >= 2);
        Assert.True(result.UsersProcessed >= 2);
    }

    [Fact]
    public async Task Closed_or_non_actionable_task_is_not_required_to_have_work_priority()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using (var db = new SiNetSQLDbContext(options))
        {
            db.Siusers.Add(new Siuser { Id = 10, Name = "U", IsActive = true });
            var closed = new ProjectAssignmentStatus { Code = "Done", Name = "Done", IsOpen = false, IsActionable = false };
            db.ProjectAssignmentStatuses.Add(closed);
            await db.SaveChangesAsync();
            db.ProjectAssignments.Add(new ProjectAssignment
            {
                Title = "Closed",
                ProjectId = 1,
                AssignedToId = 10,
                StatusId = closed.Id,
                WorkQueueBucket = WorkQueueBucketCodes.Medium,
                WorkPriority = null,
            });
            await db.SaveChangesAsync();
        }

        await using var verify = new SiNetSQLDbContext(options);
        var task = await verify.ProjectAssignments.SingleAsync();
        Assert.Null(task.WorkPriority);
    }

    [Fact]
    public async Task Demo_seed_does_not_create_open_task_without_work_priority()
    {
        var (options, userId) = await SeedMinimalDemoDatabaseAsync();
        var factory = new StubDbContextFactory(options);
        var result = await new SqlTaskDemoSeedService(factory).SeedAsync(new Application.DevTools.DemoTaskSeedOptions { TargetUserId = userId });
        Assert.True(result.Succeeded, result.Summary);

        await using var db = factory.CreateDbContext();
        var openWithoutPriority = await db.ProjectAssignments
            .Include(t => t.AssignmentStatus)
            .Where(t => t.AssignedToId == userId)
            .Where(t => t.Title != null && t.Title.StartsWith(SqlTaskDemoSeedService.TitlePrefix))
            .Where(t => t.AssignmentStatus!.IsActionable && t.WorkPriority == null)
            .CountAsync();

        Assert.Equal(0, openWithoutPriority);
    }

    [Fact]
    public void Task_workbench_create_uses_queue_service()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "src", "SiNet.Infrastructure.Sql", "Services", "Tasks", "SqlTaskWorkbenchService.cs"));
        Assert.Contains("TaskQueuePriorityEngine.InsertWithAutoPriorityAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Task_workbench_delete_uses_queue_service()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "src", "SiNet.Infrastructure.Sql", "Services", "Tasks", "SqlTaskWorkbenchService.cs"));
        Assert.Contains("CompactAfterRemovalAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Task_workbench_does_not_edit_work_priority_directly()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "src", "SiNet.App.Wpf", "Surfaces", "Tasks", "TaskWorkbenchViewModel.cs"));
        Assert.DoesNotContain("WorkPriority =", source, StringComparison.Ordinal);
        Assert.Contains("ITaskQueueService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Task_workbench_viewmodel_wires_queue_service()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "src", "SiNet.App.Wpf", "Surfaces", "Tasks", "TaskWorkbenchViewModel.cs"));
        Assert.Contains("RepairQueueCommand", source, StringComparison.Ordinal);
        Assert.Contains("MoveUpCommand", source, StringComparison.Ordinal);
        Assert.Contains("MoveDownCommand", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Task_queue_management_has_no_LegacyBridge()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "src", "SiNet.Infrastructure.Sql", "Services", "Tasks", "SqlTaskQueueService.cs"));
        Assert.DoesNotContain("LegacyBridge", source, StringComparison.Ordinal);
    }

    [Fact]
    public void No_schema_change_or_migration_for_queue_management()
    {
        var snapshot = File.ReadAllText(Path.Combine(RepoRoot, "src", "SiNet.Infrastructure.Sql", "Migrations", "SiNetSQLDbContextModelSnapshot.cs"));
        Assert.Contains("WorkQueueBucket", snapshot, StringComparison.Ordinal);
        Assert.Contains("WorkPriority", snapshot, StringComparison.Ordinal);
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

    private static async Task<DbContextOptions<SiNetSQLDbContext>> SeedThreeTaskQueueAsync(int userId, int bucket)
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new SiNetSQLDbContext(options);
        var open = new ProjectAssignmentStatus { Name = "Open", IsOpen = true, IsActionable = true };
        db.ProjectAssignmentStatuses.Add(open);
        await db.SaveChangesAsync();
        for (var i = 1; i <= 3; i++)
        {
            db.ProjectAssignments.Add(new ProjectAssignment
            {
                Id = i,
                Title = $"T{i}",
                ProjectId = 100 + i,
                AssignedToId = userId,
                StatusId = open.Id,
                AssignmentStatus = open,
                WorkQueueBucket = bucket,
                WorkPriority = i,
            });
        }
        await db.SaveChangesAsync();
        return options;
    }

    private static async Task<DbContextOptions<SiNetSQLDbContext>> SeedQueuedTasksForReassignAsync()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new SiNetSQLDbContext(options);
        db.Siusers.AddRange(new Siuser { Id = 10, Name = "A", IsActive = true }, new Siuser { Id = 20, Name = "B", IsActive = true });
        var open = new ProjectAssignmentStatus { Name = "Open", IsOpen = true, IsActionable = true };
        db.ProjectAssignmentStatuses.Add(open);
        await db.SaveChangesAsync();
        db.ProjectAssignments.AddRange(
            new ProjectAssignment { Id = 1, Title = "T1", ProjectId = 1, AssignedToId = 10, StatusId = open.Id, WorkQueueBucket = WorkQueueBucketCodes.Medium, WorkPriority = 1 },
            new ProjectAssignment { Id = 2, Title = "T2", ProjectId = 2, AssignedToId = 10, StatusId = open.Id, WorkQueueBucket = WorkQueueBucketCodes.Medium, WorkPriority = 2 },
            new ProjectAssignment { Id = 3, Title = "T3", ProjectId = 3, AssignedToId = 20, StatusId = open.Id, WorkQueueBucket = WorkQueueBucketCodes.Medium, WorkPriority = 1 });
        await db.SaveChangesAsync();
        return options;
    }

    private static async Task<DbContextOptions<SiNetSQLDbContext>> SeedQueuedTasksForBucketChangeAsync()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new SiNetSQLDbContext(options);
        var open = new ProjectAssignmentStatus { Name = "Open", IsOpen = true, IsActionable = true };
        db.ProjectAssignmentStatuses.Add(open);
        await db.SaveChangesAsync();
        db.ProjectAssignments.AddRange(
            new ProjectAssignment { Id = 1, Title = "T1", ProjectId = 1, AssignedToId = 10, StatusId = open.Id, WorkQueueBucket = WorkQueueBucketCodes.Quick, WorkPriority = 1 },
            new ProjectAssignment { Id = 2, Title = "T2", ProjectId = 2, AssignedToId = 10, StatusId = open.Id, WorkQueueBucket = WorkQueueBucketCodes.Quick, WorkPriority = 2 },
            new ProjectAssignment { Id = 3, Title = "T3", ProjectId = 3, AssignedToId = 10, StatusId = open.Id, WorkQueueBucket = WorkQueueBucketCodes.Long, WorkPriority = 1 });
        await db.SaveChangesAsync();
        return options;
    }

    private static async Task<DbContextOptions<SiNetSQLDbContext>> SeedBrokenQueueAsync(
        bool nullPriority = false,
        bool duplicatePriority = false,
        bool gapPriority = false)
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new SiNetSQLDbContext(options);
        var open = new ProjectAssignmentStatus { Name = "Open", IsOpen = true, IsActionable = true };
        db.ProjectAssignmentStatuses.Add(open);
        await db.SaveChangesAsync();

        if (nullPriority)
        {
            db.ProjectAssignments.Add(new ProjectAssignment
            {
                Id = 1, Title = "Null", ProjectId = 1, AssignedToId = 10, StatusId = open.Id,
                WorkQueueBucket = WorkQueueBucketCodes.Quick, WorkPriority = null,
            });
        }
        else if (duplicatePriority)
        {
            db.ProjectAssignments.AddRange(
                new ProjectAssignment { Id = 1, Title = "D1", ProjectId = 1, AssignedToId = 10, StatusId = open.Id, WorkQueueBucket = WorkQueueBucketCodes.Quick, WorkPriority = 1 },
                new ProjectAssignment { Id = 2, Title = "D2", ProjectId = 2, AssignedToId = 10, StatusId = open.Id, WorkQueueBucket = WorkQueueBucketCodes.Quick, WorkPriority = 1 });
        }
        else if (gapPriority)
        {
            db.ProjectAssignments.AddRange(
                new ProjectAssignment { Id = 1, Title = "G1", ProjectId = 1, AssignedToId = 10, StatusId = open.Id, WorkQueueBucket = WorkQueueBucketCodes.Quick, WorkPriority = 1 },
                new ProjectAssignment { Id = 2, Title = "G2", ProjectId = 2, AssignedToId = 10, StatusId = open.Id, WorkQueueBucket = WorkQueueBucketCodes.Quick, WorkPriority = 5 });
        }

        await db.SaveChangesAsync();
        return options;
    }

    private static async Task<DbContextOptions<SiNetSQLDbContext>> SeedMultipleUserBucketQueuesAsync()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new SiNetSQLDbContext(options);
        var open = new ProjectAssignmentStatus { Name = "Open", IsOpen = true, IsActionable = true };
        db.ProjectAssignmentStatuses.Add(open);
        await db.SaveChangesAsync();
        db.ProjectAssignments.AddRange(
            new ProjectAssignment { Id = 1, Title = "U10Q", ProjectId = 1, AssignedToId = 10, StatusId = open.Id, WorkQueueBucket = WorkQueueBucketCodes.Quick, WorkPriority = 5 },
            new ProjectAssignment { Id = 2, Title = "U20M", ProjectId = 2, AssignedToId = 20, StatusId = open.Id, WorkQueueBucket = WorkQueueBucketCodes.Medium, WorkPriority = null });
        await db.SaveChangesAsync();
        return options;
    }

    private static async Task<(DbContextOptions<SiNetSQLDbContext> Options, int UserId)> SeedMinimalDemoDatabaseAsync()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new SiNetSQLDbContext(options);
        var userId = 12;
        db.Siusers.Add(new Siuser { Id = userId, Name = "U12", IsActive = true });
        db.Projects.Add(new Project { Title = SqlTaskDemoSeedService.DemoProjectName, Created = DateTime.UtcNow });
        db.ProjectAssignmentStatuses.Add(new ProjectAssignmentStatus { Code = TaskStatusCodes.Open, Name = "Open", IsOpen = true, IsActionable = true });
        db.ProjectAssignmentStatuses.Add(new ProjectAssignmentStatus { Code = TaskStatusCodes.Completed, Name = "Done", IsOpen = false, IsActionable = false });
        db.TaskTypes.Add(new TaskType { Code = TaskTypeCodes.FileInitialInquiry, Name = "File", IsActive = true, SortOrder = 1 });
        await db.SaveChangesAsync();
        await SqlTaskDemoSeedService.EnsureDemoTaskTypesAsync(db, CancellationToken.None);
        return (options, userId);
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private sealed class StubDbContextFactory(DbContextOptions<SiNetSQLDbContext> options) : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);
    }
}
