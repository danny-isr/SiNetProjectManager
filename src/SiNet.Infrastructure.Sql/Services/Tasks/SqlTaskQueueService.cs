using Microsoft.EntityFrameworkCore;
using SiNet.Application.Tasks;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Tasks;

/// <summary>
/// Native bucket-aware queue mutations for the New System process backbone.
/// </summary>
public sealed class SqlTaskQueueService : ITaskQueueService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;

    public SqlTaskQueueService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    }

    public async ValueTask<IReadOnlyList<TaskSummaryDto>> GetUserQueueAsync(
        int userId,
        int workQueueBucket,
        CancellationToken ct = default)
    {
        if (!WorkQueueBucketCodes.IsValid(workQueueBucket))
            throw new ArgumentOutOfRangeException(nameof(workQueueBucket));

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var query = db.ProjectAssignments
            .AsNoTracking()
            .Include(t => t.TaskType)
            .Include(t => t.AssignmentStatus)
            .Include(t => t.AssignedTo)
            .Include(t => t.LastTaskResult)
            .Where(t => t.AssignedToId == userId
                        && t.WorkQueueBucket == workQueueBucket
                        && t.AssignmentStatus != null
                        && t.AssignmentStatus.IsActionable
                        && t.WorkPriority != null);

        var tasks = await query.ToListAsync(ct).ConfigureAwait(false);
        return TaskQueryOrdering.SortByPriorityWithinBucket(tasks).Select(SqlTaskQueryService.MapTask).ToList();
    }

    public async ValueTask MoveWithinBucketAsync(
        int taskId,
        int newPosition,
        int changedByUserId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var task = await db.ProjectAssignments
            .Include(t => t.AssignmentStatus)
            .FirstOrDefaultAsync(t => t.Id == taskId, ct)
            .ConfigureAwait(false)
            ?? throw new ArgumentException($"Task {taskId} not found.");

        if (task.AssignmentStatus?.IsActionable != true)
            throw new InvalidOperationException("Cannot reorder a non-actionable task.");

        if (!task.AssignedToId.HasValue || !task.WorkPriority.HasValue)
            throw new InvalidOperationException("Task is not in a work queue.");

        var employeeId = task.AssignedToId.Value;
        var bucket = WorkQueueBucketResolver.Resolve(task);
        var oldPriority = task.WorkPriority.Value;

        if (oldPriority == newPosition)
            return;

        var queueSize = await db.ProjectAssignments
            .CountAsync(t => t.AssignedToId == employeeId
                             && t.WorkQueueBucket == bucket
                             && t.AssignmentStatus != null
                             && t.AssignmentStatus.IsActionable
                             && t.WorkPriority != null
                             && t.Id != taskId,
                ct)
            .ConfigureAwait(false);

        newPosition = Math.Max(1, Math.Min(newPosition, queueSize + 1));

        if (oldPriority == newPosition)
            return;

        var openTasks = await db.ProjectAssignments
            .Include(t => t.AssignmentStatus)
            .Where(t => t.AssignedToId == employeeId
                        && t.WorkQueueBucket == bucket
                        && t.AssignmentStatus != null
                        && t.AssignmentStatus.IsActionable
                        && t.WorkPriority != null
                        && t.Id != taskId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (oldPriority < newPosition)
        {
            foreach (var other in openTasks.Where(t => t.WorkPriority > oldPriority && t.WorkPriority <= newPosition))
                other.WorkPriority--;
        }
        else
        {
            foreach (var other in openTasks.Where(t => t.WorkPriority >= newPosition && t.WorkPriority < oldPriority))
                other.WorkPriority++;
        }

        task.WorkPriority = newPosition;
        task.Modified = DateTime.Now;
        task.EditorId = changedByUserId;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask ChangeBucketAsync(
        int taskId,
        int newBucket,
        int changedByUserId,
        CancellationToken ct = default)
    {
        if (!WorkQueueBucketCodes.IsValid(newBucket))
            throw new ArgumentOutOfRangeException(nameof(newBucket));

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var task = await db.ProjectAssignments
            .Include(t => t.AssignmentStatus)
            .Include(t => t.TaskType)
            .FirstOrDefaultAsync(t => t.Id == taskId, ct)
            .ConfigureAwait(false)
            ?? throw new ArgumentException($"Task {taskId} not found.");

        var oldBucket = WorkQueueBucketResolver.Resolve(task);
        var oldPriority = task.WorkPriority;
        var inQueue = task.AssignmentStatus?.IsActionable == true && oldPriority.HasValue;

        if (inQueue && task.AssignedToId.HasValue)
            TaskQueuePriorityEngine.RemoveFromQueue(db, task);

        task.WorkQueueBucket = newBucket;

        int? newPriority = null;
        if (task.AssignmentStatus?.IsActionable == true && task.AssignedToId.HasValue)
        {
            TaskQueuePriorityEngine.AppendToQueueEnd(db, task);
            newPriority = task.WorkPriority;
        }
        else
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        task.Modified = DateTime.Now;
        task.EditorId = changedByUserId;

        db.ProjectAssignmentEvents.Add(new ProjectAssignmentEvent
        {
            ProjectAssignmentId = task.Id,
            EventType = "BucketChange",
            CreatedByUserId = changedByUserId,
            CreatedDate = DateTime.UtcNow,
            Note = $"bucket: {WorkQueueBucketCodes.ToDisplayName(oldBucket)} → {WorkQueueBucketCodes.ToDisplayName(newBucket)}; priority: {FormatPriority(oldPriority)} → {FormatPriority(newPriority)}",
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask<TaskQueueRepairResult> RepairQueueAsync(
        int userId,
        int workQueueBucket,
        CancellationToken ct = default)
    {
        if (!WorkQueueBucketCodes.IsValid(workQueueBucket))
            throw new ArgumentOutOfRangeException(nameof(workQueueBucket));

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var detail = await TaskQueuePriorityEngine.ValidateAndReindexDetailedAsync(
                db, userId, workQueueBucket, ct)
            .ConfigureAwait(false);

        return ToRepairResult(userId, workQueueBucket, detail);
    }

    public async ValueTask<TaskQueueRepairResult> RepairAllQueuesAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var pairs = await db.ProjectAssignments
            .Where(t => t.AssignedToId != null
                        && t.AssignmentStatus != null
                        && t.AssignmentStatus.IsActionable)
            .Select(t => new { UserId = t.AssignedToId!.Value, t.WorkQueueBucket })
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var users = pairs.Select(p => p.UserId).Distinct().Count();
        var aggregate = TaskQueueRepairResult.Empty;

        foreach (var pair in pairs)
        {
            var detail = await TaskQueuePriorityEngine.ValidateAndReindexDetailedAsync(
                    db, pair.UserId, pair.WorkQueueBucket, ct)
                .ConfigureAwait(false);
            aggregate = aggregate.Merge(ToRepairResult(pair.UserId, pair.WorkQueueBucket, detail));
        }

        return new TaskQueueRepairResult(
            users,
            pairs.Count,
            aggregate.TasksAssignedPriority,
            aggregate.DuplicatePrioritiesFixed,
            aggregate.NullPrioritiesFixed,
            aggregate.GapsClosed,
            aggregate.Errors);
    }

    public async ValueTask<TaskQueueOperationResult> MoveUpAsync(
        int taskId,
        int changedByUserId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var task = await db.ProjectAssignments
            .Include(t => t.AssignmentStatus)
            .FirstOrDefaultAsync(t => t.Id == taskId, ct)
            .ConfigureAwait(false);

        if (task is null)
            return FailOp($"Task {taskId} not found.", taskId);

        if (task.AssignmentStatus?.IsActionable != true || !task.WorkPriority.HasValue || !task.AssignedToId.HasValue)
            return FailOp("Task is not in an actionable work queue.", taskId);

        var oldPriority = task.WorkPriority.Value;
        if (oldPriority <= 1)
            return FailOp("Task is already at the top of the queue.", taskId, oldPriority: oldPriority);

        var newPriority = oldPriority - 1;
        await MoveWithinBucketAsync(taskId, newPriority, changedByUserId, ct).ConfigureAwait(false);
        return SuccessOp(taskId, oldPriority: oldPriority, newPriority: newPriority, message: $"Moved task {taskId} to position {newPriority}.");
    }

    public async ValueTask<TaskQueueOperationResult> MoveDownAsync(
        int taskId,
        int changedByUserId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var task = await db.ProjectAssignments
            .Include(t => t.AssignmentStatus)
            .FirstOrDefaultAsync(t => t.Id == taskId, ct)
            .ConfigureAwait(false);

        if (task is null)
            return FailOp($"Task {taskId} not found.", taskId);

        if (task.AssignmentStatus?.IsActionable != true || !task.WorkPriority.HasValue || !task.AssignedToId.HasValue)
            return FailOp("Task is not in an actionable work queue.", taskId);

        var employeeId = task.AssignedToId.Value;
        var bucket = WorkQueueBucketResolver.Resolve(task);
        var oldPriority = task.WorkPriority.Value;

        var queueSize = await db.ProjectAssignments
            .CountAsync(t => t.AssignedToId == employeeId
                             && t.WorkQueueBucket == bucket
                             && t.AssignmentStatus != null
                             && t.AssignmentStatus.IsActionable
                             && t.WorkPriority != null,
                ct)
            .ConfigureAwait(false);

        if (oldPriority >= queueSize)
            return FailOp("Task is already at the bottom of the queue.", taskId, oldPriority: oldPriority);

        var newPriority = oldPriority + 1;
        await MoveWithinBucketAsync(taskId, newPriority, changedByUserId, ct).ConfigureAwait(false);
        return SuccessOp(taskId, oldPriority: oldPriority, newPriority: newPriority, message: $"Moved task {taskId} to position {newPriority}.");
    }

    public async ValueTask<TaskQueueOperationResult> ReassignAsync(
        int taskId,
        int newUserId,
        int changedByUserId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var task = await db.ProjectAssignments
            .Include(t => t.AssignmentStatus)
            .Include(t => t.TaskType)
            .FirstOrDefaultAsync(t => t.Id == taskId, ct)
            .ConfigureAwait(false);

        if (task is null)
            return FailOp($"Task {taskId} not found.", taskId);

        if (!task.AssignedToId.HasValue)
            return FailOp("Task has no current assignee.", taskId);

        var oldUserId = task.AssignedToId.Value;
        if (oldUserId == newUserId)
            return SuccessOp(taskId, oldUserId: oldUserId, newUserId: newUserId, message: "Assignee unchanged.");

        var oldBucket = WorkQueueBucketResolver.Resolve(task);
        var oldPriority = task.WorkPriority;
        var wasInQueue = task.AssignmentStatus?.IsActionable == true && oldPriority.HasValue;

        if (wasInQueue)
        {
            task.WorkPriority = null;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await TaskQueuePriorityEngine.CompactAfterRemovalAsync(
                    db, oldUserId, oldBucket, oldPriority!.Value, ct)
                .ConfigureAwait(false);
        }

        WorkQueueBucketResolver.EnsureValidBucket(task, task.TaskType);
        task.AssignedToId = newUserId;
        task.Modified = DateTime.Now;
        task.EditorId = changedByUserId;

        int? newPriority = null;
        if (task.AssignmentStatus?.IsActionable == true)
        {
            newPriority = await TaskQueuePriorityEngine.GetNextPriorityAsync(
                    db, newUserId, task.WorkQueueBucket, ct)
                .ConfigureAwait(false);
            task.WorkPriority = newPriority;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return SuccessOp(
            taskId,
            oldUserId: oldUserId,
            newUserId: newUserId,
            oldBucket: oldBucket,
            newBucket: task.WorkQueueBucket,
            oldPriority: oldPriority,
            newPriority: newPriority,
            message: $"Reassigned task {taskId} from user {oldUserId} to {newUserId}.");
    }

    public async ValueTask<int> ValidateAndRepairQueueAsync(
        int userId,
        int workQueueBucket,
        CancellationToken ct = default)
    {
        if (!WorkQueueBucketCodes.IsValid(workQueueBucket))
            throw new ArgumentOutOfRangeException(nameof(workQueueBucket));

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await TaskQueuePriorityEngine.ValidateAndReindexAsync(db, userId, workQueueBucket, ct)
            .ConfigureAwait(false);
    }

    private static string FormatPriority(int? priority) => priority?.ToString() ?? "null";

    private static TaskQueueRepairResult ToRepairResult(int userId, int bucket, TaskQueueRepairDetail detail) =>
        new(
            UsersProcessed: 1,
            BucketsProcessed: 1,
            TasksAssignedPriority: detail.NullPrioritiesFixed,
            DuplicatePrioritiesFixed: detail.DuplicatePrioritiesFixed,
            NullPrioritiesFixed: detail.NullPrioritiesFixed,
            GapsClosed: detail.GapsClosed,
            Errors: []);

    private static TaskQueueOperationResult FailOp(
        string message,
        int? taskId = null,
        int? oldPriority = null) =>
        new(false, message, taskId, OldPriority: oldPriority);

    private static TaskQueueOperationResult SuccessOp(
        int taskId,
        string message,
        int? oldUserId = null,
        int? newUserId = null,
        int? oldBucket = null,
        int? newBucket = null,
        int? oldPriority = null,
        int? newPriority = null) =>
        new(true, message, taskId, oldUserId, newUserId, oldBucket, newBucket, oldPriority, newPriority);
}
