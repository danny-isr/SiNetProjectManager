using Microsoft.EntityFrameworkCore;
using SiNet.Application.Tasks;
using SiNet.Infrastructure.Sql.Constants;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Tasks;

/// <summary>
/// Native Task Workbench write operations — create/delete with queue priority management.
/// </summary>
public sealed class SqlTaskWorkbenchService : ITaskWorkbenchService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;

    public SqlTaskWorkbenchService(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    }

    public async ValueTask<TaskCreationOptionsDto> GetTaskCreationOptionsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var projects = await db.Projects
            .AsNoTracking()
            .OrderBy(p => p.Title)
            .Select(p => new TaskLookupItemDto(p.Id, p.Title ?? $"Project {p.Id}"))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var users = await db.Siusers
            .AsNoTracking()
            .Where(u => u.IsActive)
            .OrderBy(u => u.Name)
            .Select(u => new TaskLookupItemDto(u.Id, u.Name ?? u.LoginName ?? $"User {u.Id}"))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var taskTypes = await db.TaskTypes
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Name)
            .Select(t => new TaskLookupItemDto(t.Id, $"{t.Name} ({t.Code})", t.DefaultWorkQueueBucket))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var statuses = await db.ProjectAssignmentStatuses
            .AsNoTracking()
            .Where(s => s.IsOpen && s.IsActionable)
            .OrderBy(s => s.SortOrder)
            .Select(s => new TaskLookupItemDto(s.Id, s.Name ?? s.Code ?? $"Status {s.Id}"))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var buckets = new List<TaskLookupItemDto>
        {
            new(WorkQueueBucketCodes.Quick, WorkQueueBucketCodes.ToDisplayName(WorkQueueBucketCodes.Quick)),
            new(WorkQueueBucketCodes.Medium, WorkQueueBucketCodes.ToDisplayName(WorkQueueBucketCodes.Medium)),
            new(WorkQueueBucketCodes.Long, WorkQueueBucketCodes.ToDisplayName(WorkQueueBucketCodes.Long)),
        };

        return new TaskCreationOptionsDto(projects, users, taskTypes, statuses, buckets);
    }

    public async ValueTask<TaskCommandResult> CreateTaskAsync(
        CreateTaskRequest request,
        int changedByUserId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Title))
            return Fail("Title is required.");

        if (!WorkQueueBucketCodes.IsValid(request.WorkQueueBucket))
            return Fail("Invalid work queue bucket.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var status = await db.ProjectAssignmentStatuses
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.StatusId, ct)
            .ConfigureAwait(false);

        if (status is null)
            return Fail($"Status {request.StatusId} not found.");

        if (status.IsActionable && await HasDuplicateOpenTaskIdentityAsync(
                db, request.ProjectId, request.AssignedToUserId, request.TaskTypeId, request.ParentAssignmentId, ct)
            .ConfigureAwait(false))
        {
            return Fail(DuplicateOpenTaskMessage(request.ParentAssignmentId));
        }

        var now = DateTime.UtcNow;
        var task = new ProjectAssignment
        {
            Title = request.Title.Trim(),
            ProjectId = request.ProjectId,
            AssignedToId = request.AssignedToUserId,
            StatusId = request.StatusId,
            TaskTypeId = request.TaskTypeId,
            ParentAssignmentId = request.ParentAssignmentId,
            WorkQueueBucket = request.WorkQueueBucket,
            DueDate = request.DueDate,
            Body = request.Body,
            AuthorId = changedByUserId,
            Created = now,
            Modified = now,
        };

        try
        {
            if (status.IsActionable)
            {
                await TaskQueuePriorityEngine.InsertWithAutoPriorityAsync(db, task, ct)
                    .ConfigureAwait(false);
            }
            else
            {
                task.WorkPriority = null;
                db.ProjectAssignments.Add(task);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            return new TaskCommandResult(true, $"Created task {task.Id}.", task.Id);
        }
        catch (DbUpdateException ex) when (IsUniqueOpenTaskViolation(ex))
        {
            return Fail(DuplicateOpenTaskMessage(request.ParentAssignmentId));
        }
    }

    public async ValueTask<TaskCommandResult> DeleteTaskAsync(
        int taskId,
        int changedByUserId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var task = await db.ProjectAssignments
            .Include(t => t.AssignmentStatus)
            .FirstOrDefaultAsync(t => t.Id == taskId, ct)
            .ConfigureAwait(false);

        if (task is null)
            return Fail($"Task {taskId} not found.");

        var hasChildren = await db.ProjectAssignments
            .AnyAsync(t => t.ParentAssignmentId == taskId, ct)
            .ConfigureAwait(false);

        if (hasChildren)
            return Fail("Cannot delete a task that has child tasks.");

        var assigneeId = task.AssignedToId;
        var bucket = WorkQueueBucketResolver.Resolve(task);
        var removedPriority = task.WorkPriority;
        var wasInQueue = task.AssignmentStatus?.IsActionable == true && removedPriority.HasValue;

        db.ProjectAssignments.Remove(task);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (wasInQueue && assigneeId.HasValue)
        {
            await TaskQueuePriorityEngine.CompactAfterRemovalAsync(
                    db, assigneeId.Value, bucket, removedPriority!.Value, ct)
                .ConfigureAwait(false);
        }

        return new TaskCommandResult(true, $"Deleted task {taskId}.");
    }

    public async ValueTask<IReadOnlyList<int>> GetDemoTaskAssigneeUserIdsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        return await db.ProjectAssignments
            .AsNoTracking()
            .Where(t => t.Title != null && t.Title.StartsWith("DEBUG_TASK_SEED"))
            .Where(t => t.AssignedToId != null)
            .Select(t => t.AssignedToId!.Value)
            .Distinct()
            .OrderBy(id => id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    private static async Task<bool> HasDuplicateOpenTaskIdentityAsync(
        SiNetSQLDbContext db,
        int projectId,
        int assignedToId,
        int taskTypeId,
        int? parentAssignmentId,
        CancellationToken ct)
    {
        return await db.ProjectAssignments
            .AnyAsync(t => t.ProjectId == projectId
                           && t.AssignedToId == assignedToId
                           && t.TaskTypeId == taskTypeId
                           && t.ParentAssignmentId == parentAssignmentId
                           && t.WorkPriority != null,
                ct)
            .ConfigureAwait(false);
    }

    private static bool IsUniqueOpenTaskViolation(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("IX_ProjectAssignment_UniqueOpenTask", StringComparison.OrdinalIgnoreCase);
    }

    private static string DuplicateOpenTaskMessage(int? parentAssignmentId) =>
        parentAssignmentId.HasValue
            ? "כבר קיימת תת-משימה פתוחה מאותו סוג תחת משימת האב."
            : "כבר קיימת משימה פתוחה מסוג זה לפרויקט ולמשתמש.";

    private static TaskCommandResult Fail(string message) =>
        new(false, message);
}
