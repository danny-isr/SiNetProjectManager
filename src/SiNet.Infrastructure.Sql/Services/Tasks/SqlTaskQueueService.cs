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

        var tasks = await db.ProjectAssignments
            .AsNoTracking()
            .Include(t => t.TaskType)
            .Include(t => t.AssignmentStatus)
            .Include(t => t.AssignedTo)
            .Include(t => t.LastTaskResult)
            .Where(t => t.AssignedToId == userId
                        && t.WorkQueueBucket == workQueueBucket
                        && t.AssignmentStatus != null
                        && t.AssignmentStatus.IsActionable
                        && t.WorkPriority != null)
            .OrderBy(t => t.WorkPriority)
            .ThenBy(t => t.DueDate ?? DateTime.MaxValue)
            .ThenBy(t => t.Created ?? DateTime.MinValue)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return tasks.Select(SqlTaskQueryService.MapTask).ToList();
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

        if (oldPriority < newPosition)
        {
            await db.ProjectAssignments
                .Where(t => t.AssignedToId == employeeId
                            && t.WorkQueueBucket == bucket
                            && t.AssignmentStatus != null
                            && t.AssignmentStatus.IsActionable
                            && t.WorkPriority > oldPriority
                            && t.WorkPriority <= newPosition)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(t => t.WorkPriority, t => t.WorkPriority - 1),
                    ct)
                .ConfigureAwait(false);
        }
        else
        {
            await db.ProjectAssignments
                .Where(t => t.AssignedToId == employeeId
                            && t.WorkQueueBucket == bucket
                            && t.AssignmentStatus != null
                            && t.AssignmentStatus.IsActionable
                            && t.WorkPriority >= newPosition
                            && t.WorkPriority < oldPriority)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(t => t.WorkPriority, t => t.WorkPriority + 1),
                    ct)
                .ConfigureAwait(false);
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
}
