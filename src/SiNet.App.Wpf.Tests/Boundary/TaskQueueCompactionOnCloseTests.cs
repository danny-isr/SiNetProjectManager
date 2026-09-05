using System.IO;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Tasks;
using SiNet.App.Wpf.Tests.Support;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.Tasks;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Boundary;

/// <summary>
/// Closing / system-closing a queued task must compact assignee+bucket to contiguous 1..N.
/// </summary>
public sealed class TaskQueueCompactionOnCloseTests
{
    [Fact]
    public async Task Complete_task_at_position_3_compacts_queue_to_1_through_4()
    {
        var (options, userId, _, completedId, _, _) =
            await SeedFiveQueuedAsync(WorkQueueBucketCodes.Medium, closablePriority: 3);
        var factory = new StubDbContextFactory(options);
        var completion = new SqlTaskCompletionService(factory, new StubWorkflowCommandService());

        var midId = await GetIdByPriorityAsync(options, userId, WorkQueueBucketCodes.Medium, 3);
        var outcome = await completion.CompleteAsync(
            new CompleteTaskCommand(
                midId,
                ReviewCompletionEvents.OutsourceQuoteReceived,
                TaskResultCode: null,
                CompletedTaskLinkIds: null,
                userId),
            CancellationToken.None);

        Assert.True(outcome.Success, outcome.ErrorMessage);
        Assert.True(outcome.TaskClosed);

        await using var db = factory.CreateDbContext();
        var closed = await db.ProjectAssignments.SingleAsync(t => t.Id == midId);
        Assert.Equal(completedId, closed.StatusId);
        Assert.Null(closed.WorkPriority);

        var openPriorities = await db.ProjectAssignments
            .Where(t => t.AssignedToId == userId
                        && t.WorkQueueBucket == WorkQueueBucketCodes.Medium
                        && t.WorkPriority != null)
            .OrderBy(t => t.WorkPriority)
            .Select(t => t.WorkPriority!.Value)
            .ToListAsync();

        Assert.Equal([1, 2, 3, 4], openPriorities);
    }

    [Fact]
    public async Task System_close_via_RemoveFromQueue_at_position_2_keeps_queue_contiguous()
    {
        // Mirrors WorkflowActionHelpers.CloseTasksAsSystemAsync (saveChanges:false then SaveChanges).
        var (options, userId, _, completedId, _, _) =
            await SeedFiveQueuedAsync(WorkQueueBucketCodes.Quick, closablePriority: 2);
        await using var db = new SiNetSQLDbContext(options);

        var target = await db.ProjectAssignments.SingleAsync(
            t => t.AssignedToId == userId && t.WorkQueueBucket == WorkQueueBucketCodes.Quick && t.WorkPriority == 2);
        target.StatusId = completedId;
        target.Status = TaskStatusCodes.Completed;
        await TaskQueuePriorityEngine.RemoveFromQueueAsync(
            db, target, compact: true, saveChanges: false, CancellationToken.None);
        await db.SaveChangesAsync();

        var openPriorities = await db.ProjectAssignments
            .Where(t => t.AssignedToId == userId
                        && t.WorkQueueBucket == WorkQueueBucketCodes.Quick
                        && t.WorkPriority != null)
            .OrderBy(t => t.WorkPriority)
            .Select(t => t.WorkPriority!.Value)
            .ToListAsync();

        Assert.Equal([1, 2, 3, 4], openPriorities);
        Assert.Null((await db.ProjectAssignments.SingleAsync(t => t.Id == target.Id)).WorkPriority);
    }

    [Fact]
    public async Task Closing_last_task_leaves_remaining_as_1_through_4()
    {
        var (options, userId, _, _, _, _) =
            await SeedFiveQueuedAsync(WorkQueueBucketCodes.Medium, closablePriority: 5);
        var factory = new StubDbContextFactory(options);
        var completion = new SqlTaskCompletionService(factory, new StubWorkflowCommandService());

        var lastId = await GetIdByPriorityAsync(options, userId, WorkQueueBucketCodes.Medium, 5);
        var outcome = await completion.CompleteAsync(
            new CompleteTaskCommand(lastId, ReviewCompletionEvents.OutsourceQuoteReceived, null, null, userId),
            CancellationToken.None);
        Assert.True(outcome.Success && outcome.TaskClosed, outcome.ErrorMessage);

        await using var db = factory.CreateDbContext();
        var openPriorities = await db.ProjectAssignments
            .Where(t => t.AssignedToId == userId
                        && t.WorkQueueBucket == WorkQueueBucketCodes.Medium
                        && t.WorkPriority != null)
            .OrderBy(t => t.WorkPriority)
            .Select(t => t.WorkPriority!.Value)
            .ToListAsync();

        Assert.Equal([1, 2, 3, 4], openPriorities);
    }

    [Fact]
    public async Task Shell_with_null_WorkPriority_does_not_trigger_compaction()
    {
        var (options, userId, openId, _, typeIds, projectId) =
            await SeedFiveQueuedAsync(WorkQueueBucketCodes.Medium, closablePriority: 3);

        await using (var seed = new SiNetSQLDbContext(options))
        {
            seed.ProjectAssignments.Add(new ProjectAssignment
            {
                Title = "shell — תהליך #99",
                ProjectId = projectId,
                AssignedToId = userId,
                StatusId = openId,
                TaskTypeId = typeIds[0],
                WorkQueueBucket = WorkQueueBucketCodes.Medium,
                WorkPriority = null,
                Created = DateTime.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        await using var db = new SiNetSQLDbContext(options);
        var shell = await db.ProjectAssignments.SingleAsync(t => t.Title!.Contains("תהליך #99"));
        var before = await SnapshotPrioritiesAsync(db, userId, WorkQueueBucketCodes.Medium);

        await TaskQueuePriorityEngine.RemoveFromQueueAsync(
            db, shell, compact: true, saveChanges: true, CancellationToken.None);

        var after = await SnapshotPrioritiesAsync(db, userId, WorkQueueBucketCodes.Medium);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Complete_does_not_affect_different_user_or_bucket()
    {
        var (options, userId, openId, _, typeIds, projectId) =
            await SeedFiveQueuedAsync(WorkQueueBucketCodes.Medium, closablePriority: 3);

        await using (var seed = new SiNetSQLDbContext(options))
        {
            seed.Siusers.Add(new Siuser { Id = 99, Name = "Other", IsActive = true });
            seed.ProjectAssignments.AddRange(
                new ProjectAssignment
                {
                    Title = "OtherUser",
                    ProjectId = projectId,
                    AssignedToId = 99,
                    StatusId = openId,
                    TaskTypeId = typeIds[0],
                    WorkQueueBucket = WorkQueueBucketCodes.Medium,
                    WorkPriority = 1,
                    Created = DateTime.UtcNow,
                },
                new ProjectAssignment
                {
                    Title = "OtherBucket",
                    ProjectId = projectId,
                    AssignedToId = userId,
                    StatusId = openId,
                    TaskTypeId = typeIds[1],
                    WorkQueueBucket = WorkQueueBucketCodes.Quick,
                    WorkPriority = 7,
                    Created = DateTime.UtcNow,
                });
            await seed.SaveChangesAsync();
        }

        var factory = new StubDbContextFactory(options);
        var completion = new SqlTaskCompletionService(factory, new StubWorkflowCommandService());
        var midId = await GetIdByPriorityAsync(options, userId, WorkQueueBucketCodes.Medium, 3);
        var outcome = await completion.CompleteAsync(
            new CompleteTaskCommand(midId, ReviewCompletionEvents.OutsourceQuoteReceived, null, null, userId),
            CancellationToken.None);
        Assert.True(outcome.TaskClosed, outcome.ErrorMessage);

        await using var db = factory.CreateDbContext();
        Assert.Equal(1, (await db.ProjectAssignments.SingleAsync(t => t.Title == "OtherUser")).WorkPriority);
        Assert.Equal(7, (await db.ProjectAssignments.SingleAsync(t => t.Title == "OtherBucket")).WorkPriority);
    }

    [Fact]
    public async Task RemoveFromQueue_saveChanges_false_does_not_persist_until_caller_saves()
    {
        var (options, userId, _, _, _, _) =
            await SeedFiveQueuedAsync(WorkQueueBucketCodes.Medium, closablePriority: 3);
        await using var db = new SiNetSQLDbContext(options);
        var mid = await db.ProjectAssignments.SingleAsync(t => t.AssignedToId == userId && t.WorkPriority == 3);

        await TaskQueuePriorityEngine.RemoveFromQueueAsync(
            db, mid, compact: true, saveChanges: false, CancellationToken.None);

        await using (var other = new SiNetSQLDbContext(options))
        {
            var stillQueued = await other.ProjectAssignments
                .Where(t => t.AssignedToId == userId && t.WorkPriority != null)
                .OrderBy(t => t.WorkPriority)
                .Select(t => t.WorkPriority!.Value)
                .ToListAsync();
            Assert.Equal([1, 2, 3, 4, 5], stillQueued);
        }

        await db.SaveChangesAsync();

        await using var after = new SiNetSQLDbContext(options);
        var compacted = await after.ProjectAssignments
            .Where(t => t.AssignedToId == userId && t.WorkPriority != null)
            .OrderBy(t => t.WorkPriority)
            .Select(t => t.WorkPriority!.Value)
            .ToListAsync();
        Assert.Equal([1, 2, 3, 4], compacted);
    }

    [Fact]
    public async Task Discarding_tracked_close_without_SaveChanges_rolls_back_compaction_with_close()
    {
        var (options, userId, openId, completedId, _, _) =
            await SeedFiveQueuedAsync(WorkQueueBucketCodes.Medium, closablePriority: 3);

        await using (var db = new SiNetSQLDbContext(options))
        {
            var mid = await db.ProjectAssignments.SingleAsync(t => t.AssignedToId == userId && t.WorkPriority == 3);
            mid.StatusId = completedId;
            mid.Status = TaskStatusCodes.Completed;
            await TaskQueuePriorityEngine.RemoveFromQueueAsync(
                db, mid, compact: true, saveChanges: false, CancellationToken.None);
        }

        await using var verify = new SiNetSQLDbContext(options);
        var midAgain = await verify.ProjectAssignments.SingleAsync(t => t.AssignedToId == userId && t.Title == "Q3");
        Assert.Equal(openId, midAgain.StatusId);
        Assert.Equal(3, midAgain.WorkPriority);
        Assert.Equal(
            [1, 2, 3, 4, 5],
            await verify.ProjectAssignments
                .Where(t => t.AssignedToId == userId && t.WorkPriority != null)
                .OrderBy(t => t.WorkPriority)
                .Select(t => t.WorkPriority!.Value)
                .ToListAsync());
    }

    [Fact]
    public void Completion_and_system_close_use_shared_RemoveFromQueueAsync()
    {
        var completion = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiNet.Infrastructure.Sql", "Services", "Tasks", "SqlTaskCompletionService.cs"));
        var helpers = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "SiNet.Infrastructure.Sql", "Services", "Actions", "WorkflowActionHelpers.cs"));

        Assert.Contains("RemoveFromQueueAsync", completion, StringComparison.Ordinal);
        Assert.Contains("saveChanges: false", completion, StringComparison.Ordinal);
        Assert.Contains("RemoveFromQueueAsync", helpers, StringComparison.Ordinal);
        Assert.Contains("saveChanges: false", helpers, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Delete_and_change_bucket_still_compact()
    {
        var (options, userId, _, _, _, _) =
            await SeedFiveQueuedAsync(WorkQueueBucketCodes.Quick, closablePriority: 3);
        var factory = new StubDbContextFactory(options);
        var workbench = new SqlTaskWorkbenchService(factory, new StubWorkflowCommandService());
        var queue = new SqlTaskQueueService(factory);

        var id3 = await GetIdByPriorityAsync(options, userId, WorkQueueBucketCodes.Quick, 3);
        Assert.True((await workbench.DeleteTaskAsync(id3, userId)).Succeeded);

        await using (var db = factory.CreateDbContext())
        {
            Assert.Equal(
                [1, 2, 3, 4],
                await db.ProjectAssignments
                    .Where(t => t.AssignedToId == userId && t.WorkQueueBucket == WorkQueueBucketCodes.Quick && t.WorkPriority != null)
                    .OrderBy(t => t.WorkPriority)
                    .Select(t => t.WorkPriority!.Value)
                    .ToListAsync());
        }

        var id2 = await GetIdByPriorityAsync(options, userId, WorkQueueBucketCodes.Quick, 2);
        await queue.ChangeBucketAsync(id2, WorkQueueBucketCodes.Long, userId);

        await using var after = factory.CreateDbContext();
        Assert.Equal(
            [1, 2, 3],
            await after.ProjectAssignments
                .Where(t => t.AssignedToId == userId && t.WorkQueueBucket == WorkQueueBucketCodes.Quick && t.WorkPriority != null)
                .OrderBy(t => t.WorkPriority)
                .Select(t => t.WorkPriority!.Value)
                .ToListAsync());
        Assert.Equal(
            WorkQueueBucketCodes.Long,
            (await after.ProjectAssignments.SingleAsync(t => t.Id == id2)).WorkQueueBucket);
    }

    private static async Task<List<int>> SnapshotPrioritiesAsync(SiNetSQLDbContext db, int userId, int bucket) =>
        await db.ProjectAssignments
            .Where(t => t.AssignedToId == userId && t.WorkQueueBucket == bucket && t.WorkPriority != null)
            .OrderBy(t => t.WorkPriority)
            .Select(t => t.WorkPriority!.Value)
            .ToListAsync();

    private static async Task<int> GetIdByPriorityAsync(
        DbContextOptions<SiNetSQLDbContext> options,
        int userId,
        int bucket,
        int priority)
    {
        await using var db = new SiNetSQLDbContext(options);
        return await db.ProjectAssignments
            .Where(t => t.AssignedToId == userId && t.WorkQueueBucket == bucket && t.WorkPriority == priority)
            .Select(t => t.Id)
            .SingleAsync();
    }

    private static async Task<(
        DbContextOptions<SiNetSQLDbContext> Options,
        int UserId,
        int OpenId,
        int CompletedId,
        int[] TypeIds,
        int ProjectId)> SeedFiveQueuedAsync(int bucket, int closablePriority)
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;

        await using var db = new SiNetSQLDbContext(options);
        const int userId = 14;
        db.Siusers.Add(new Siuser { Id = userId, Name = "QueueUser", IsActive = true });
        db.Projects.Add(new Project { Id = 1, Title = "P1", Created = DateTime.UtcNow });
        db.ProjectAssignmentStatuses.AddRange(
            new ProjectAssignmentStatus { Code = TaskStatusCodes.Open, Name = "Open", IsOpen = true, IsActionable = true, SortOrder = 1 },
            new ProjectAssignmentStatus { Code = TaskStatusCodes.Completed, Name = "Completed", IsOpen = false, IsActionable = false, SortOrder = 2 },
            new ProjectAssignmentStatus { Code = TaskStatusCodes.Cancelled, Name = "Cancelled", IsOpen = false, IsActionable = false, SortOrder = 3 });
        db.TaskTypes.AddRange(
            new TaskType { Code = "T1", Name = "T1", IsActive = true, SortOrder = 1, DefaultWorkQueueBucket = bucket },
            new TaskType { Code = "T2", Name = "T2", IsActive = true, SortOrder = 2, DefaultWorkQueueBucket = bucket },
            new TaskType { Code = TaskTypeCodes.ReceiveOutsourceQuote, Name = "Receive", IsActive = true, SortOrder = 3, DefaultWorkQueueBucket = bucket },
            new TaskType { Code = "T4", Name = "T4", IsActive = true, SortOrder = 4, DefaultWorkQueueBucket = bucket },
            new TaskType { Code = "T5", Name = "T5", IsActive = true, SortOrder = 5, DefaultWorkQueueBucket = bucket },
            new TaskType { Code = "T6", Name = "T6", IsActive = true, SortOrder = 6, DefaultWorkQueueBucket = bucket });
        await db.SaveChangesAsync();

        var openId = db.ProjectAssignmentStatuses.Single(s => s.Code == TaskStatusCodes.Open).Id;
        var completedId = db.ProjectAssignmentStatuses.Single(s => s.Code == TaskStatusCodes.Completed).Id;
        var receiveTypeId = db.TaskTypes.Single(t => t.Code == TaskTypeCodes.ReceiveOutsourceQuote).Id;
        var otherTypes = db.TaskTypes
            .Where(t => t.Code != TaskTypeCodes.ReceiveOutsourceQuote)
            .OrderBy(t => t.SortOrder)
            .Select(t => t.Id)
            .ToList();

        var typeIds = new int[5];
        var otherIdx = 0;
        for (var i = 0; i < 5; i++)
        {
            var priority = i + 1;
            var typeId = priority == closablePriority ? receiveTypeId : otherTypes[otherIdx++];
            typeIds[i] = typeId;
            db.ProjectAssignments.Add(new ProjectAssignment
            {
                Title = $"Q{priority}",
                ProjectId = 1,
                AssignedToId = userId,
                StatusId = openId,
                TaskTypeId = typeId,
                WorkQueueBucket = bucket,
                WorkPriority = priority,
                Created = DateTime.UtcNow.AddMinutes(i),
            });
        }

        await db.SaveChangesAsync();
        return (options, userId, openId, completedId, typeIds, 1);
    }

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private sealed class StubDbContextFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);

        public ValueTask<SiNetSQLDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CreateDbContext());
    }
}
