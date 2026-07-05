namespace SiNet.Application.Tasks;

/// <summary>
/// Write port for personal work-queue operations scoped by assignee + bucket.
/// </summary>
public interface ITaskQueueService
{
    /// <summary>Returns actionable tasks in the user's bucket queue ordered by work priority.</summary>
    ValueTask<IReadOnlyList<TaskSummaryDto>> GetUserQueueAsync(
        int userId,
        int workQueueBucket,
        CancellationToken ct = default);

    /// <summary>Moves a task to a new position within its current assignee + bucket queue.</summary>
    ValueTask MoveWithinBucketAsync(
        int taskId,
        int newPosition,
        int changedByUserId,
        CancellationToken ct = default);

    /// <summary>Moves the same task to another bucket queue (append to end).</summary>
    ValueTask ChangeBucketAsync(
        int taskId,
        int newBucket,
        int changedByUserId,
        CancellationToken ct = default);

    /// <summary>Validates and repairs queue numbering for one assignee + bucket pair.</summary>
    ValueTask<int> ValidateAndRepairQueueAsync(
        int userId,
        int workQueueBucket,
        CancellationToken ct = default);

    /// <summary>Repairs one assignee + bucket queue with detailed result.</summary>
    ValueTask<TaskQueueRepairResult> RepairQueueAsync(
        int userId,
        int workQueueBucket,
        CancellationToken ct = default);

    /// <summary>Repairs all actionable assignee + bucket queues in the database.</summary>
    ValueTask<TaskQueueRepairResult> RepairAllQueuesAsync(CancellationToken ct = default);

    /// <summary>Moves a task one position up within its assignee + bucket queue.</summary>
    ValueTask<TaskQueueOperationResult> MoveUpAsync(
        int taskId,
        int changedByUserId,
        CancellationToken ct = default);

    /// <summary>Moves a task one position down within its assignee + bucket queue.</summary>
    ValueTask<TaskQueueOperationResult> MoveDownAsync(
        int taskId,
        int changedByUserId,
        CancellationToken ct = default);

    /// <summary>Reassigns a task to another user — compacts old queue, appends to new user's bucket queue.</summary>
    ValueTask<TaskQueueOperationResult> ReassignAsync(
        int taskId,
        int newUserId,
        int changedByUserId,
        CancellationToken ct = default);
}
