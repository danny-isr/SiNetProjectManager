using Microsoft.EntityFrameworkCore;
using SiNet.Application.Tasks;
using SiNet.Infrastructure.Sql.Services.Tasks;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Tasks;

/// <summary>
/// Native read implementation of <see cref="ITaskQueryService"/>. Ports list/detail queries from
/// legacy <c>TaskService</c> without exposing EF entities.
/// </summary>
public sealed class SqlTaskQueryService : ITaskQueryService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;

    public SqlTaskQueryService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    }

    public async ValueTask<TaskSummaryDto?> GetByIdAsync(int taskId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var task = await db.ProjectAssignments
            .AsNoTracking()
            .Include(t => t.TaskType)
            .Include(t => t.AssignmentStatus)
            .Include(t => t.AssignedTo)
            .Include(t => t.LastTaskResult)
            .FirstOrDefaultAsync(t => t.Id == taskId, ct)
            .ConfigureAwait(false);

        return task is null ? null : MapTask(task);
    }

    public async ValueTask<IReadOnlyList<TaskSummaryDto>> GetTasksForProjectAsync(
        int projectId,
        bool includeClosed = false,
        int? workQueueBucket = null,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var query = db.ProjectAssignments
            .AsNoTracking()
            .Include(t => t.TaskType)
            .Include(t => t.AssignmentStatus)
            .Include(t => t.AssignedTo)
            .Include(t => t.LastTaskResult)
            .Where(t => t.ProjectId == projectId);

        if (!includeClosed)
            query = query.Where(t => t.AssignmentStatus == null || t.AssignmentStatus.IsOpen);

        if (workQueueBucket.HasValue)
            query = query.Where(t => t.WorkQueueBucket == workQueueBucket.Value);

        var tasks = await query.ToListAsync(ct).ConfigureAwait(false);
        return TaskQueryOrdering.SortByQueueOrder(tasks).Select(MapTask).ToList();
    }

    public ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForUserAsync(
        int userId,
        int? workQueueBucket = null,
        CancellationToken ct = default)
        => QueryOpenTasksForUserAsync(userId, workQueueBucket, ct);

    public async ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForUserByBucketAsync(
        int userId,
        int workQueueBucket,
        CancellationToken ct)
    {
        if (!WorkQueueBucketCodes.IsValid(workQueueBucket))
            throw new ArgumentOutOfRangeException(nameof(workQueueBucket));

        return await QueryOpenTasksForUserAsync(userId, workQueueBucket, ct).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForAllUsersByBucketAsync(
        int workQueueBucket,
        CancellationToken ct)
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
            .Where(t => t.WorkQueueBucket == workQueueBucket
                        && (t.AssignmentStatus == null || t.AssignmentStatus.IsOpen));

        var tasks = await query.ToListAsync(ct).ConfigureAwait(false);
        return TaskQueryOrdering.SortAllUsersInBucket(tasks).Select(MapTask).ToList();
    }

    private async ValueTask<IReadOnlyList<TaskSummaryDto>> QueryOpenTasksForUserAsync(
        int userId,
        int? workQueueBucket,
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var query = db.ProjectAssignments
            .AsNoTracking()
            .Include(t => t.TaskType)
            .Include(t => t.AssignmentStatus)
            .Include(t => t.AssignedTo)
            .Include(t => t.LastTaskResult)
            .Where(t => t.AssignedToId == userId
                        && (t.AssignmentStatus == null || t.AssignmentStatus.IsOpen));

        if (workQueueBucket.HasValue)
            query = query.Where(t => t.WorkQueueBucket == workQueueBucket.Value);

        // Sort in memory — legacy DB rows may have varchar/datetime values that break SQL ORDER BY.
        var tasks = await query.ToListAsync(ct).ConfigureAwait(false);
        return TaskQueryOrdering.SortByQueueOrder(tasks).Select(MapTask).ToList();
    }

    internal static TaskSummaryDto MapTask(ProjectAssignment task)
    {
        var bucket = WorkQueueBucketResolver.Resolve(task);
        var taskTypeCode = task.TaskType?.Code;
        var interaction = taskTypeCode is not null
            ? ReviewTaskInteractionRegistry.TryGet(taskTypeCode)
            : null;

        return new TaskSummaryDto(
            TaskId: task.Id,
            ProjectId: task.ProjectId,
            TaskTypeCode: taskTypeCode,
            TaskTypeName: task.TaskType?.Name,
            StatusCode: task.AssignmentStatus?.Code ?? task.Status,
            StatusName: task.AssignmentStatus?.Name,
            IsOpen: task.AssignmentStatus?.IsOpen ?? true,
            AssignedToUserId: task.AssignedToId,
            AssignedToUserName: task.AssignedTo?.Name,
            WorkQueueBucket: bucket,
            WorkQueueBucketCode: WorkQueueBucketCodes.ToCode(bucket),
            WorkQueueBucketDisplayName: WorkQueueBucketCodes.ToDisplayName(bucket),
            WorkPriority: task.WorkPriority,
            DueDate: task.DueDate,
            CreatedAt: task.Created ?? task.StartDate,
            LastTaskResultCode: task.LastTaskResult?.Code,
            Title: task.Title,
            ComponentKey: interaction?.ComponentKey);
    }
}
