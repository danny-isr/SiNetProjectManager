using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Tasks;

/// <summary>
/// In-memory task ordering — avoids SQL Server datetime/varchar conversion errors on legacy rows
/// (see legacy <c>TaskService.GetTasksForEmployee</c>).
/// </summary>
internal static class TaskQueryOrdering
{
    public static List<ProjectAssignment> SortByQueueOrder(IEnumerable<ProjectAssignment> tasks) =>
        tasks
            .OrderBy(t => t.WorkQueueBucket)
            .ThenBy(t => t.WorkPriority ?? int.MaxValue)
            .ThenBy(t => t.DueDate ?? DateTime.MaxValue)
            .ThenBy(t => t.Created ?? DateTime.MinValue)
            .ToList();

    public static List<ProjectAssignment> SortByPriorityWithinBucket(IEnumerable<ProjectAssignment> tasks) =>
        tasks
            .OrderBy(t => t.WorkPriority ?? int.MaxValue)
            .ThenBy(t => t.DueDate ?? DateTime.MaxValue)
            .ThenBy(t => t.Created ?? DateTime.MinValue)
            .ToList();

    public static List<ProjectAssignment> SortAllUsersInBucket(IEnumerable<ProjectAssignment> tasks) =>
        tasks
            .OrderBy(t => t.AssignedTo?.Name ?? string.Empty)
            .ThenBy(t => t.AssignedToId ?? int.MaxValue)
            .ThenBy(t => t.WorkPriority ?? int.MaxValue)
            .ThenBy(t => t.DueDate ?? DateTime.MaxValue)
            .ThenBy(t => t.Created ?? DateTime.MinValue)
            .ToList();
}
