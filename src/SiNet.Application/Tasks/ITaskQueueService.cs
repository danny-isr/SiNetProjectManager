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
}
