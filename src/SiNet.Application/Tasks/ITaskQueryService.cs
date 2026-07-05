namespace SiNet.Application.Tasks;

/// <summary>
/// Read port for task queues, project task lists, and task detail for Work Surface shells.
/// </summary>
public interface ITaskQueryService
{
    /// <summary>Returns a single task summary, or <see langword="null"/> when not found.</summary>
    ValueTask<TaskSummaryDto?> GetByIdAsync(int taskId, CancellationToken ct);

    /// <summary>Returns tasks for a project ordered by work priority then created date.</summary>
    ValueTask<IReadOnlyList<TaskSummaryDto>> GetTasksForProjectAsync(
        int projectId,
        bool includeClosed = false,
        CancellationToken ct = default);

    /// <summary>Returns open tasks assigned to a user, ordered by work priority.</summary>
    ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForUserAsync(int userId, CancellationToken ct);
}
