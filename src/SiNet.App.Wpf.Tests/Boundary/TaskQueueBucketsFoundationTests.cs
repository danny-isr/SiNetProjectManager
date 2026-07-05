using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Tasks;
using SiNet.Infrastructure.Sql;
using SiNet.Infrastructure.Sql.Services.Tasks;
using SiNetSQL.Data;
using SiNetSQL.Models;
using SiNetSQL.Services;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>
/// Task Queue Buckets foundation — personal queues scoped by AssignedToId + WorkQueueBucket.
/// </summary>
public sealed class TaskQueueBucketsFoundationTests
{
    [Fact]
    public void Process_backbone_registers_task_queue_service()
    {
        var services = new ServiceCollection();
        services.AddSiNetProcessBackbone();
        Assert.Contains(services, d => d.ServiceType == typeof(ITaskQueueService));
    }

    [Fact]
    public void Model_and_DbContext_define_bucket_fields()
    {
        var projectAssignment = File.ReadAllText(
            Path.Combine(RepoRoot, "src", "SiNet.Infrastructure.Sql", "Models", "ProjectAssignment.cs"));
        Assert.Contains("WorkQueueBucket", projectAssignment, StringComparison.Ordinal);
        Assert.Contains("WorkQueueBucketCodes.Medium", projectAssignment, StringComparison.Ordinal);

        var taskType = File.ReadAllText(
            Path.Combine(RepoRoot, "src", "SiNet.Infrastructure.Sql", "Models", "TaskType.cs"));
        Assert.Contains("DefaultWorkQueueBucket", taskType, StringComparison.Ordinal);

        var dbContext = File.ReadAllText(
            Path.Combine(RepoRoot, "src", "SiNet.Infrastructure.Sql", "Data", "SiNetSQLDbContext.cs"));
        Assert.Contains("WorkQueueBucket", dbContext, StringComparison.Ordinal);
        Assert.Contains("HasDefaultValue(2)", dbContext, StringComparison.Ordinal);
        Assert.Contains("DefaultWorkQueueBucket", dbContext, StringComparison.Ordinal);

        var snapshot = File.ReadAllText(SnapshotPath);
        Assert.Contains("WorkQueueBucket", snapshot, StringComparison.Ordinal);
        Assert.Contains("DefaultWorkQueueBucket", snapshot, StringComparison.Ordinal);
        var indexBlock = snapshot.Split("IX_ProjectAssignment_UniqueOpenTask")[1];
        Assert.DoesNotContain("WorkQueueBucket", indexBlock.Split("HasFilter")[0], StringComparison.Ordinal);
    }

    [Fact]
    public void User_managed_AddTaskWorkQueueBuckets_migration_exists_when_applied()
    {
        var migrationsDir = Path.Combine(RepoRoot, "src", "SiNet.Infrastructure.Sql", "Migrations");
        var migrationFiles = Directory.Exists(migrationsDir)
            ? Directory.GetFiles(migrationsDir, "*AddTaskWorkQueueBuckets.cs", SearchOption.TopDirectoryOnly)
            : [];

        Assert.NotEmpty(migrationFiles);

        var migration = File.ReadAllText(migrationFiles[0]);
        Assert.Contains("WorkQueueBucket", migration, StringComparison.Ordinal);
        Assert.Contains("DefaultWorkQueueBucket", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void New_task_receives_bucket_from_task_type_default()
    {
        using var db = NewContext();
        var openStatus = SeedActionableStatus(db);
        var taskType = new TaskType { Code = "QuickType", Name = "Quick", DefaultWorkQueueBucket = WorkQueueBucketCodes.Quick };
        db.TaskTypes.Add(taskType);
        db.SaveChanges();

        var svc = new TaskService(db);
        var task = svc.GetOrCreateTaskWithDueDate(1, 10, taskType.Id, 99, null);

        Assert.Equal(WorkQueueBucketCodes.Quick, task.WorkQueueBucket);
        Assert.Equal(1, task.WorkPriority);
    }

    [Fact]
    public void New_task_without_task_type_default_receives_medium_bucket()
    {
        using var db = NewContext();
        SeedActionableStatus(db);
        var taskType = new TaskType { Code = "Plain", Name = "Plain" };
        db.TaskTypes.Add(taskType);
        db.SaveChanges();

        var svc = new TaskService(db);
        var task = svc.GetOrCreateTaskWithDueDate(1, 10, taskType.Id, 99, null);

        Assert.Equal(WorkQueueBucketCodes.Medium, task.WorkQueueBucket);
    }

    [Fact]
    public void New_actionable_task_is_appended_to_end_of_assignee_bucket_queue()
    {
        using var db = NewContext();
        SeedActionableStatus(db);
        var taskType = new TaskType { Code = "T", Name = "T", DefaultWorkQueueBucket = WorkQueueBucketCodes.Medium };
        db.TaskTypes.Add(taskType);
        db.SaveChanges();

        var svc = new TaskService(db);
        var first = svc.GetOrCreateTaskWithDueDate(1, 10, taskType.Id, 99, null);
        var second = svc.GetOrCreateTaskWithDueDate(2, 10, taskType.Id, 99, null);

        Assert.Equal(1, first.WorkPriority);
        Assert.Equal(2, second.WorkPriority);
    }

    [Fact]
    public void Non_actionable_task_gets_null_work_priority()
    {
        using var db = NewContext();
        var waiting = new ProjectAssignmentStatus { Name = "Waiting", IsActionable = false, IsOpen = true };
        db.ProjectAssignmentStatuses.Add(waiting);
        var taskType = new TaskType { Code = "T", Name = "T" };
        db.TaskTypes.Add(taskType);
        db.SaveChanges();

        var svc = new TaskService(db);
        var task = svc.GetOrCreateTaskWithDueDateAndStatus(1, 10, taskType.Id, waiting.Id, 99, null);

        Assert.Null(task.WorkPriority);
    }

    [Fact]
    public void Closing_task_clears_priority_and_compacts_only_old_bucket()
    {
        using var db = NewContext();
        var open = SeedActionableStatus(db);
        var closed = new ProjectAssignmentStatus { Name = "Done", IsActionable = false, IsOpen = false };
        db.ProjectAssignmentStatuses.Add(closed);
        db.SaveChanges();

        SeedQueuedTask(db, id: 1, employeeId: 10, bucket: WorkQueueBucketCodes.Quick, priority: 1, statusId: open);
        var closing = SeedQueuedTask(db, id: 2, employeeId: 10, bucket: WorkQueueBucketCodes.Quick, priority: 2, statusId: open);
        SeedQueuedTask(db, id: 3, employeeId: 10, bucket: WorkQueueBucketCodes.Quick, priority: 3, statusId: open);
        SeedQueuedTask(db, id: 4, employeeId: 10, bucket: WorkQueueBucketCodes.Long, priority: 1, statusId: open);

        var svc = new TaskService(db);
        svc.ChangeTaskStatus(closing.Id, closed.Id, 7);

        Assert.Null(db.ProjectAssignments.Find(closing.Id)!.WorkPriority);
        Assert.Equal(1, db.ProjectAssignments.Find(1)!.WorkPriority);
        Assert.Equal(2, db.ProjectAssignments.Find(3)!.WorkPriority);
        Assert.Equal(1, db.ProjectAssignments.Find(4)!.WorkPriority);
    }

    [Fact]
    public void Reopening_appends_to_end_of_same_bucket()
    {
        using var db = NewContext();
        var open = SeedActionableStatus(db);
        var waiting = new ProjectAssignmentStatus { Name = "Waiting", IsActionable = false, IsOpen = true };
        db.ProjectAssignmentStatuses.Add(waiting);
        db.SaveChanges();

        var task = SeedQueuedTask(db, id: 1, employeeId: 10, bucket: WorkQueueBucketCodes.Medium, priority: 1, statusId: open);
        SeedQueuedTask(db, id: 2, employeeId: 10, bucket: WorkQueueBucketCodes.Medium, priority: 2, statusId: open);

        var svc = new TaskService(db);
        svc.ChangeTaskStatus(task.Id, waiting.Id, 7);
        svc.ChangeTaskStatus(task.Id, open, 7);

        var reopened = db.ProjectAssignments.Find(task.Id)!;
        Assert.Equal(WorkQueueBucketCodes.Medium, reopened.WorkQueueBucket);
        Assert.Equal(2, reopened.WorkPriority);
    }

    [Fact]
    public void Change_bucket_moves_same_assignment_to_end_of_new_bucket()
    {
        using var db = NewContext();
        var open = SeedActionableStatus(db);
        SeedQueuedTask(db, id: 1, employeeId: 10, bucket: WorkQueueBucketCodes.Quick, priority: 1, statusId: open);
        var moving = SeedQueuedTask(db, id: 2, employeeId: 10, bucket: WorkQueueBucketCodes.Quick, priority: 2, statusId: open);
        SeedQueuedTask(db, id: 3, employeeId: 10, bucket: WorkQueueBucketCodes.Long, priority: 1, statusId: open);

        var svc = new TaskService(db);
        var beforeId = moving.Id;
        svc.ChangeTaskBucket(moving.Id, WorkQueueBucketCodes.Long, 7);

        var updated = db.ProjectAssignments.Find(beforeId)!;
        Assert.Equal(beforeId, updated.Id);
        Assert.Equal(WorkQueueBucketCodes.Long, updated.WorkQueueBucket);
        Assert.Equal(2, updated.WorkPriority);
        Assert.Equal(1, db.ProjectAssignments.Find(1)!.WorkPriority);
        Assert.DoesNotContain(
            db.ProjectAssignments.Where(t => t.AssignedToId == 10 && t.WorkQueueBucket == WorkQueueBucketCodes.Quick),
            t => t.Id == beforeId);
    }

    [Fact]
    public void Reassign_moves_task_to_new_employee_same_bucket_and_compacts_old_queue()
    {
        using var db = NewContext();
        var open = SeedActionableStatus(db);
        SeedUserGroups(db, employeeId: 10, newEmployeeId: 20);

        SeedQueuedTask(db, id: 1, employeeId: 10, bucket: WorkQueueBucketCodes.Medium, priority: 1, statusId: open);
        var moving = SeedQueuedTask(db, id: 2, employeeId: 10, bucket: WorkQueueBucketCodes.Medium, priority: 2, statusId: open);
        SeedQueuedTask(db, id: 3, employeeId: 20, bucket: WorkQueueBucketCodes.Medium, priority: 1, statusId: open);

        var svc = new TaskService(db);
        var (success, error) = svc.ReassignTask(moving.Id, 20, 7);

        Assert.True(success, error);
        var updated = db.ProjectAssignments.Find(moving.Id)!;
        Assert.Equal(20, updated.AssignedToId);
        Assert.Equal(WorkQueueBucketCodes.Medium, updated.WorkQueueBucket);
        Assert.Equal(2, updated.WorkPriority);
        Assert.Equal(1, db.ProjectAssignments.Find(1)!.WorkPriority);
    }

    [Fact]
    public void Reorder_within_bucket_does_not_affect_other_bucket()
    {
        using var db = NewContext();
        var open = SeedActionableStatus(db);
        var a = SeedQueuedTask(db, id: 1, employeeId: 10, bucket: WorkQueueBucketCodes.Quick, priority: 1, statusId: open);
        var b = SeedQueuedTask(db, id: 2, employeeId: 10, bucket: WorkQueueBucketCodes.Quick, priority: 2, statusId: open);
        SeedQueuedTask(db, id: 3, employeeId: 10, bucket: WorkQueueBucketCodes.Long, priority: 1, statusId: open);

        var svc = new TaskService(db);
        svc.ReorderTask(b.Id, 1, 7);

        Assert.Equal(2, db.ProjectAssignments.Find(a.Id)!.WorkPriority);
        Assert.Equal(1, db.ProjectAssignments.Find(b.Id)!.WorkPriority);
        Assert.Equal(1, db.ProjectAssignments.Find(3)!.WorkPriority);
    }

    [Fact]
    public void Same_employee_may_have_priority_one_in_each_bucket()
    {
        using var db = NewContext();
        var open = SeedActionableStatus(db);
        SeedQueuedTask(db, id: 1, employeeId: 10, bucket: WorkQueueBucketCodes.Quick, priority: 1, statusId: open);
        SeedQueuedTask(db, id: 2, employeeId: 10, bucket: WorkQueueBucketCodes.Medium, priority: 1, statusId: open);
        SeedQueuedTask(db, id: 3, employeeId: 10, bucket: WorkQueueBucketCodes.Long, priority: 1, statusId: open);

        var priorities = db.ProjectAssignments
            .Where(t => t.AssignedToId == 10 && t.WorkPriority == 1)
            .Select(t => t.WorkQueueBucket)
            .OrderBy(b => b)
            .ToList();

        Assert.Equal([1, 2, 3], priorities);
    }

    [Fact]
    public void ValidateAndReindexAll_handles_employee_bucket_pairs()
    {
        using var db = NewContext();
        var open = SeedActionableStatus(db);
        SeedQueuedTask(db, id: 1, employeeId: 10, bucket: WorkQueueBucketCodes.Quick, priority: 5, statusId: open);
        SeedQueuedTask(db, id: 2, employeeId: 10, bucket: WorkQueueBucketCodes.Medium, priority: 9, statusId: open);

        TaskPriorityEngine.ValidateAndReindexAll(db);

        Assert.Equal(1, db.ProjectAssignments.Find(1)!.WorkPriority);
        Assert.Equal(1, db.ProjectAssignments.Find(2)!.WorkPriority);
    }

    [Fact]
    public async Task Task_query_returns_bucket_and_filters_by_bucket()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>().UseInMemoryDatabase(dbName).Options;

        await using (var seed = new SiNetSQLDbContext(options))
        {
            var open = new ProjectAssignmentStatus { Id = 1, Name = "Open", IsOpen = true, IsActionable = true };
            seed.ProjectAssignmentStatuses.Add(open);
            seed.ProjectAssignments.AddRange(
                new ProjectAssignment { Id = 1, ProjectId = 5, AssignedToId = 7, StatusId = 1, WorkQueueBucket = WorkQueueBucketCodes.Quick, WorkPriority = 1, Title = "Q" },
                new ProjectAssignment { Id = 2, ProjectId = 5, AssignedToId = 7, StatusId = 1, WorkQueueBucket = WorkQueueBucketCodes.Long, WorkPriority = 1, Title = "L" });
            await seed.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<SiNetSQLDbContext>>(new StubDbContextFactory(options));
        services.AddSiNetProcessBackbone();
        await using var provider = services.BuildServiceProvider();
        var query = provider.GetRequiredService<ITaskQueryService>();

        var all = await query.GetOpenTasksForUserAsync(7);
        Assert.Equal(2, all.Count);
        Assert.All(all, t => Assert.True(WorkQueueBucketCodes.IsValid(t.WorkQueueBucket)));

        var quickOnly = await query.GetOpenTasksForUserByBucketAsync(7, WorkQueueBucketCodes.Quick, CancellationToken.None);
        Assert.Single(quickOnly);
        Assert.Equal(WorkQueueBucketCodes.Quick, quickOnly[0].WorkQueueBucket);
        Assert.Equal("Quick", quickOnly[0].WorkQueueBucketCode);
    }

    [Fact]
    public async Task Change_bucket_writes_bucket_change_event()
    {
        using var db = NewContext();
        var open = SeedActionableStatus(db);
        var task = SeedQueuedTask(db, id: 1, employeeId: 10, bucket: WorkQueueBucketCodes.Quick, priority: 1, statusId: open);

        var svc = new TaskService(db);
        svc.ChangeTaskBucket(task.Id, WorkQueueBucketCodes.Long, 7);

        var evt = db.ProjectAssignmentEvents.Single(e => e.ProjectAssignmentId == task.Id);
        Assert.Equal(SiNetSQL.Constants.TaskEventTypes.BucketChange, evt.EventType);
        Assert.Contains("Quick", evt.Note!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Long", evt.Note!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Foundation_docs_reference_task_queue_buckets()
    {
        var backbone = File.ReadAllText(Path.Combine(RepoRoot, "docs", "PROCESS_BACKBONE_FOUNDATION.md"));
        Assert.Contains("Task Queue Buckets", backbone, StringComparison.OrdinalIgnoreCase);

        var integration = File.ReadAllText(Path.Combine(RepoRoot, "docs", "WORK_SURFACE_WORKFLOW_INTEGRATION.md"));
        Assert.Contains("ITaskQueueService", integration, StringComparison.Ordinal);

        var uiMap = File.ReadAllText(Path.Combine(RepoRoot, "docs", "UI_WINDOW_MIGRATION_MAP.md"));
        Assert.Contains("Task Queue Buckets", uiMap, StringComparison.OrdinalIgnoreCase);
    }

    private static SiNetSQLDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new SiNetSQLDbContext(options);
    }

    private static int SeedActionableStatus(SiNetSQLDbContext db)
    {
        var status = new ProjectAssignmentStatus { Name = "Open", IsActionable = true, IsOpen = true };
        db.ProjectAssignmentStatuses.Add(status);
        db.SaveChanges();
        return status.Id;
    }

    private static ProjectAssignment SeedQueuedTask(
        SiNetSQLDbContext db,
        int id,
        int employeeId,
        int bucket,
        int priority,
        int statusId)
    {
        var task = new ProjectAssignment
        {
            Id = id,
            ProjectId = 100 + id,
            AssignedToId = employeeId,
            StatusId = statusId,
            WorkQueueBucket = bucket,
            WorkPriority = priority,
            Title = $"Task-{id}",
        };
        db.ProjectAssignments.Add(task);
        db.SaveChanges();
        return task;
    }

    private static void SeedUserGroups(SiNetSQLDbContext db, int employeeId, int newEmployeeId)
    {
        var group = new UserGroup { Name = "G", IsActive = true };
        db.UserGroups.Add(group);
        db.SaveChanges();
        db.UserGroupMemberships.AddRange(
            new UserGroupMembership { SiuserId = employeeId, UserGroupId = group.Id },
            new UserGroupMembership { SiuserId = newEmployeeId, UserGroupId = group.Id });
        db.SaveChanges();
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string SnapshotPath => Path.Combine(
        RepoRoot,
        "src",
        "SiNet.Infrastructure.Sql",
        "Migrations",
        "SiNetSQLDbContextModelSnapshot.cs");

    private sealed class StubDbContextFactory(DbContextOptions<SiNetSQLDbContext> options) : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);
    }
}
