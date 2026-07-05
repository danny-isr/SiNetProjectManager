using SiNet.Application.Tasks;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Tasks;

/// <summary>
/// Resolves <see cref="ProjectAssignment.WorkQueueBucket"/> from task state and task-type defaults.
/// </summary>
public static class WorkQueueBucketResolver
{
    /// <summary>
    /// Sets bucket from <see cref="TaskType.DefaultWorkQueueBucket"/> for new task creation only.
    /// </summary>
    public static void ApplyTaskTypeDefault(ProjectAssignment task, TaskType? taskType = null)
    {
        ArgumentNullException.ThrowIfNull(task);

        var typeDefault = (taskType ?? task.TaskType)?.DefaultWorkQueueBucket;
        task.WorkQueueBucket = typeDefault.HasValue && WorkQueueBucketCodes.IsValid(typeDefault.Value)
            ? typeDefault.Value
            : WorkQueueBucketCodes.Medium;
    }

    /// <summary>
    /// Returns the operational bucket for an existing task row.
    /// </summary>
    public static int Resolve(ProjectAssignment task, TaskType? taskType = null)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (WorkQueueBucketCodes.IsValid(task.WorkQueueBucket))
            return task.WorkQueueBucket;

        return ResolveFromTaskType(taskType ?? task.TaskType);
    }

    /// <summary>
    /// Ensures a persisted or in-flight task has a valid bucket without overwriting an explicit value.
    /// </summary>
    public static void EnsureValidBucket(ProjectAssignment task, TaskType? taskType = null)
    {
        if (!WorkQueueBucketCodes.IsValid(task.WorkQueueBucket))
            task.WorkQueueBucket = ResolveFromTaskType(taskType ?? task.TaskType);
    }

    private static int ResolveFromTaskType(TaskType? taskType)
    {
        var typeDefault = taskType?.DefaultWorkQueueBucket;
        if (typeDefault.HasValue && WorkQueueBucketCodes.IsValid(typeDefault.Value))
            return typeDefault.Value;

        return WorkQueueBucketCodes.Medium;
    }
}
