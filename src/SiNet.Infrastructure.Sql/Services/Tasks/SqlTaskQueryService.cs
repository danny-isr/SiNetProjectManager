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

        return task is null ? null : Map(task);
    }

    public async ValueTask<IReadOnlyList<TaskSummaryDto>> GetTasksForProjectAsync(
        int projectId,
        bool includeClosed = false,
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
        {
            query = query.Where(t => t.AssignmentStatus == null || t.AssignmentStatus.IsOpen);
        }

        var tasks = await query
            .OrderBy(t => t.WorkPriority ?? int.MaxValue)
            .ThenBy(t => t.Created)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return tasks.Select(Map).ToList();
    }

    public async ValueTask<IReadOnlyList<TaskSummaryDto>> GetOpenTasksForUserAsync(int userId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var tasks = await db.ProjectAssignments
            .AsNoTracking()
            .Include(t => t.TaskType)
            .Include(t => t.AssignmentStatus)
            .Include(t => t.AssignedTo)
            .Include(t => t.LastTaskResult)
            .Include(t => t.Project)
            .Where(t => t.AssignedToId == userId
                        && (t.AssignmentStatus == null || t.AssignmentStatus.IsOpen))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return tasks
            .OrderBy(t => t.WorkPriority ?? int.MaxValue)
            .ThenBy(t => t.Created ?? DateTime.MinValue)
            .Select(Map)
            .ToList();
    }

    private static TaskSummaryDto Map(ProjectAssignment task)
    {
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
            WorkPriority: task.WorkPriority,
            DueDate: task.DueDate,
            LastTaskResultCode: task.LastTaskResult?.Code,
            Title: task.Title,
            ComponentKey: interaction?.ComponentKey);
    }
}
