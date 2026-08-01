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
            .Include(t => t.TaskLinks)
            .FirstOrDefaultAsync(t => t.Id == taskId, ct)
            .ConfigureAwait(false);

        if (task is null)
            return null;

        var trackByTask = await LoadTrackDisplayAsync(db, [task], ct).ConfigureAwait(false);
        return MapTask(task, trackByTask.GetValueOrDefault(task.Id));
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
            .Include(t => t.TaskLinks)
            .Where(t => t.ProjectId == projectId);

        if (!includeClosed)
            query = query.Where(t => t.AssignmentStatus == null || t.AssignmentStatus.IsOpen);

        if (workQueueBucket.HasValue)
            query = query.Where(t => t.WorkQueueBucket == workQueueBucket.Value);

        var tasks = await query.ToListAsync(ct).ConfigureAwait(false);
        var trackByTask = await LoadTrackDisplayAsync(db, tasks, ct).ConfigureAwait(false);
        return TaskQueryOrdering.SortByQueueOrder(tasks)
            .Select(t => MapTask(t, trackByTask.GetValueOrDefault(t.Id)))
            .ToList();
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
            .Include(t => t.TaskLinks)
            .Where(t => t.WorkQueueBucket == workQueueBucket
                        && (t.AssignmentStatus == null || t.AssignmentStatus.IsOpen));

        var tasks = await query.ToListAsync(ct).ConfigureAwait(false);
        var trackByTask = await LoadTrackDisplayAsync(db, tasks, ct).ConfigureAwait(false);
        return TaskQueryOrdering.SortAllUsersInBucket(tasks)
            .Select(t => MapTask(t, trackByTask.GetValueOrDefault(t.Id)))
            .ToList();
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
            .Include(t => t.TaskLinks)
            .Where(t => t.AssignedToId == userId
                        && (t.AssignmentStatus == null || t.AssignmentStatus.IsOpen));

        if (workQueueBucket.HasValue)
            query = query.Where(t => t.WorkQueueBucket == workQueueBucket.Value);

        var tasks = await query.ToListAsync(ct).ConfigureAwait(false);
        var trackByTask = await LoadTrackDisplayAsync(db, tasks, ct).ConfigureAwait(false);
        return TaskQueryOrdering.SortByQueueOrder(tasks)
            .Select(t => MapTask(t, trackByTask.GetValueOrDefault(t.Id)))
            .ToList();
    }

    private static async Task<Dictionary<int, TrackDisplay>> LoadTrackDisplayAsync(
        SiNetSQLDbContext db,
        IReadOnlyList<ProjectAssignment> tasks,
        CancellationToken ct)
    {
        var taskToInstance = new Dictionary<int, int>();
        foreach (var task in tasks)
        {
            var link = task.TaskLinks
                .Where(l => l.Role == TaskLinkRole.Trigger
                            && l.LinkedEntityType == TaskLinkEntityType.WorkflowInstance)
                .OrderByDescending(l => l.CreatedAtUtc)
                .FirstOrDefault();
            if (link is null || link.LinkedEntityId <= 0 || link.LinkedEntityId > int.MaxValue)
                continue;
            taskToInstance[task.Id] = (int)link.LinkedEntityId;
        }

        if (taskToInstance.Count == 0)
            return new Dictionary<int, TrackDisplay>();

        var instanceIds = taskToInstance.Values.Distinct().ToList();
        var instances = await db.WorkflowInstances.AsNoTracking()
            .Include(i => i.WorkflowDefinition)
            .Include(i => i.JobType)
            .Include(i => i.CurrentStage)
            .Where(i => instanceIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, ct)
            .ConfigureAwait(false);

        var result = new Dictionary<int, TrackDisplay>();
        foreach (var (taskId, instanceId) in taskToInstance)
        {
            if (!instances.TryGetValue(instanceId, out var inst))
                continue;
            result[taskId] = new TrackDisplay(
                inst.WorkflowDefinition?.Name ?? inst.WorkflowDefinition?.Code,
                inst.JobType?.Title,
                inst.CurrentStage?.Name ?? inst.CurrentStage?.Code);
        }

        return result;
    }

    internal static TaskSummaryDto MapTask(ProjectAssignment task, TrackDisplay? track = null)
    {
        var bucket = WorkQueueBucketResolver.Resolve(task);
        var taskTypeCode = task.TaskType?.Code;
        var interaction = taskTypeCode is not null
            ? ReviewTaskInteractionRegistry.TryGet(taskTypeCode)
            : null;

        var trackParts = new[] { track?.ProcessName, track?.JobTypeTitle, track?.StageName }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();
        var trackLine = trackParts.Length == 0 ? null : string.Join(" · ", trackParts);

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
            ComponentKey: interaction?.ComponentKey,
            WorkflowDefinitionName: track?.ProcessName,
            JobTypeTitle: track?.JobTypeTitle,
            CurrentStageName: track?.StageName,
            TrackDisplayLine: trackLine);
    }

    internal sealed record TrackDisplay(string? ProcessName, string? JobTypeTitle, string? StageName);
}
