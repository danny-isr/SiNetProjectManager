namespace SiNet.Application.Tasks;

/// <summary>
/// Read port for task queues, project task lists, and task detail for Work Surface shells.
/// </summary>
public interface ITaskQueryService
{
    /// <summary>Returns a single task summary, or <see langword="null"/> when not found.</summary>
    ValueTask<TaskSummaryDto?> GetByIdAsync(int taskId, CancellationToken ct);

    /// <summary>Returns tasks for a project ordered by bucket, work priority, due date, created.</summary>
    ValueTask<IReadOnlyList<TaskSummaryDto>> GetTasksForProjectAsync(
        int projectId,
        bool includeClosed = false,
        int? workQueueBucket = null,
        CancellationToken ct = default);

    /// <summary>Returns open tasks assigned to a user, optionally filtered by bucket.</summary>
    ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForUserAsync(
        int userId,
        int? workQueueBucket = null,
        CancellationToken ct = default);

    /// <summary>Returns open tasks for one user bucket queue.</summary>
    ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForUserByBucketAsync(
        int userId,
        int workQueueBucket,
        CancellationToken ct);
}
