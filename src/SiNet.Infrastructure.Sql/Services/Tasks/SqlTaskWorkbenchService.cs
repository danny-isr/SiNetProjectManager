using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Diagnostics; // TEMP WF-DEBUG
using SiNet.Application.Tasks;
using SiNet.Application.Workflow;
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
    private readonly IWorkflowCommandService _workflowCommands;
    private readonly ITaskListChangeNotifier? _taskListNotifier;

    public SqlTaskWorkbenchService(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        IWorkflowCommandService workflowCommands,
        ITaskListChangeNotifier? taskListNotifier = null)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _workflowCommands = workflowCommands ?? throw new ArgumentNullException(nameof(workflowCommands));
        _taskListNotifier = taskListNotifier;
    }

    private void NotifyUiTaskListChanged(string reason)
    {
        try
        {
            WorkflowDebugTrace.Step("Workbench.Notify", $"{reason} notifier={_taskListNotifier is not null}");
            _taskListNotifier?.NotifyTaskListChanged();
        }
        catch (Exception ex)
        {
            Trace.TraceWarning(
                "[SqlTaskWorkbenchService] NotifyTaskListChanged failed ({0}): {1}",
                reason,
                ex.Message);
        }
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

            NotifyUiTaskListChanged($"created task={task.Id}");
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

        // A task that drives a non-terminal workflow cannot be hard-deleted: its TaskLinks would
        // cascade away and the workflow would be left with nothing to advance it (orphaned). The
        // caller must instead deactivate it (pauses the workflow) or complete/cancel the workflow.
        var drivenWorkflowIds = await GetLinkedWorkflowInstanceIdsAsync(
                db, taskId, ct, WorkflowStatus.Active, WorkflowStatus.Paused)
            .ConfigureAwait(false);

        if (drivenWorkflowIds.Count > 0)
        {
            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step("Workbench.DeleteGuard",
                $"task={taskId} BLOCKED — drives workflow instance(s) [{string.Join(",", drivenWorkflowIds)}]");
            return new TaskCommandResult(
                false,
                "משימה זו מפעילה Workflow פעיל ולכן לא ניתן למחוק אותה. יש להשבית אותה (ה-Workflow יושהה) או לסיים/לבטל את ה-Workflow.",
                TaskId: taskId,
                BlockedByWorkflow: true);
        }

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

        NotifyUiTaskListChanged($"deleted task={taskId}");
        return new TaskCommandResult(true, $"Deleted task {taskId}.");
    }

    public async ValueTask<TaskCommandResult> DeactivateTaskAsync(
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

        var cancelledStatus = await db.ProjectAssignmentStatuses
            .FirstOrDefaultAsync(s => s.Code == TaskStatusCodes.Cancelled, ct)
            .ConfigureAwait(false);

        if (cancelledStatus is null)
            return Fail($"Status '{TaskStatusCodes.Cancelled}' is not configured.");

        // Pause the driven workflow(s) FIRST so the watchdog (which scans Active only) never sees a
        // transient orphan between the task closing and the pause. Only pause an instance that this
        // task is the last open trigger for — otherwise other open trigger tasks still drive it.
        var activeInstanceIds = await GetLinkedWorkflowInstanceIdsAsync(
                db, taskId, ct, WorkflowStatus.Active)
            .ConfigureAwait(false);

        // TEMP WF-DEBUG
        WorkflowDebugTrace.Step("Workbench.Deactivate",
            $"task={taskId} activeDrivenInstances=[{string.Join(",", activeInstanceIds)}]");

        var pausedInstanceIds = new List<int>();
        foreach (var instanceId in activeInstanceIds)
        {
            if (await HasOtherOpenTriggerTaskAsync(db, instanceId, taskId, ct).ConfigureAwait(false))
            {
                // TEMP WF-DEBUG
                WorkflowDebugTrace.Step("Workbench.Deactivate",
                    $"task={taskId} instance={instanceId} NOT paused (other open trigger task exists)");
                continue;
            }

            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step("Workbench.Deactivate", $"task={taskId} pausing instance={instanceId}");
            await _workflowCommands.PauseAsync(
                    new PauseWorkflowCommand(instanceId, changedByUserId, $"Driving task {taskId} deactivated."),
                    ct)
                .ConfigureAwait(false);
            pausedInstanceIds.Add(instanceId);
        }

        try
        {
            var oldStatusId = task.StatusId;
            var assigneeId = task.AssignedToId;
            var bucket = WorkQueueBucketResolver.Resolve(task);
            var removedPriority = task.WorkPriority;
            var wasInQueue = task.AssignmentStatus?.IsActionable == true && removedPriority.HasValue;

            task.StatusId = cancelledStatus.Id;
            task.Status = cancelledStatus.Code;
            task.WorkPriority = null;
            task.Modified = DateTime.Now;
            task.EditorId = changedByUserId;

            db.ProjectAssignmentEvents.Add(new ProjectAssignmentEvent
            {
                ProjectAssignmentId = task.Id,
                EventType = "StatusChange",
                OldStatusId = oldStatusId,
                NewStatusId = cancelledStatus.Id,
                Note = pausedInstanceIds.Count > 0
                    ? $"Deactivated; paused workflow instance(s) {string.Join(", ", pausedInstanceIds)}."
                    : "Deactivated.",
                CreatedByUserId = changedByUserId,
                CreatedDate = DateTime.UtcNow,
            });

            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            if (wasInQueue && assigneeId.HasValue)
            {
                await TaskQueuePriorityEngine.CompactAfterRemovalAsync(
                        db, assigneeId.Value, bucket, removedPriority!.Value, ct)
                    .ConfigureAwait(false);
            }

            // TEMP WF-DEBUG
            WorkflowDebugTrace.Step("Workbench.Deactivate",
                $"task={taskId} → status=Cancelled; pausedInstances=[{string.Join(",", pausedInstanceIds)}]");

            NotifyUiTaskListChanged($"deactivated task={taskId}");
            return new TaskCommandResult(true, $"Deactivated task {taskId}.", taskId);
        }
        catch
        {
            // The task write failed after the workflow was already paused. Roll the pause back so we
            // don't leave a paused workflow whose driving task is still active. Best-effort.
            foreach (var instanceId in pausedInstanceIds)
            {
                try
                {
                    await _workflowCommands.ResumeAsync(
                            new ResumeWorkflowCommand(instanceId, changedByUserId,
                                $"Rollback: deactivation of task {taskId} failed."),
                            ct)
                        .ConfigureAwait(false);
                }
                catch (Exception rollbackEx)
                {
                    Trace.TraceError(
                        "[TaskWorkbench] Failed to roll back pause of workflow {0} after task {1} deactivation error: {2}",
                        instanceId, taskId, rollbackEx);
                }
            }

            throw;
        }
    }

    public async ValueTask<TaskCommandResult> ReactivateTaskAsync(
        int taskId,
        int changedByUserId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var task = await db.ProjectAssignments
            .Include(t => t.AssignmentStatus)
            .Include(t => t.TaskType)
            .FirstOrDefaultAsync(t => t.Id == taskId, ct)
            .ConfigureAwait(false);

        if (task is null)
            return Fail($"Task {taskId} not found.");

        var openStatus = await db.ProjectAssignmentStatuses
            .FirstOrDefaultAsync(s => s.Code == TaskStatusCodes.Open, ct)
            .ConfigureAwait(false);

        if (openStatus is null)
            return Fail($"Status '{TaskStatusCodes.Open}' is not configured.");

        var oldStatusId = task.StatusId;
        task.StatusId = openStatus.Id;
        task.Status = openStatus.Code;
        task.Modified = DateTime.Now;
        task.EditorId = changedByUserId;

        // Re-enter the work queue (append to the end of its bucket) if the task is assigned.
        if (task.AssignedToId.HasValue)
        {
            WorkQueueBucketResolver.EnsureValidBucket(task, task.TaskType);
            task.WorkPriority = await TaskQueuePriorityEngine.GetNextPriorityAsync(
                    db, task.AssignedToId.Value, task.WorkQueueBucket, ct)
                .ConfigureAwait(false);
        }

        db.ProjectAssignmentEvents.Add(new ProjectAssignmentEvent
        {
            ProjectAssignmentId = task.Id,
            EventType = "StatusChange",
            OldStatusId = oldStatusId,
            NewStatusId = openStatus.Id,
            Note = "Reactivated.",
            CreatedByUserId = changedByUserId,
            CreatedDate = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Resume any linked workflow instance this task paused while it was deactivated.
        var pausedInstanceIds = await GetLinkedWorkflowInstanceIdsAsync(
                db, taskId, ct, WorkflowStatus.Paused)
            .ConfigureAwait(false);

        // TEMP WF-DEBUG
        WorkflowDebugTrace.Step("Workbench.Reactivate",
            $"task={taskId} → status=Open; resumingInstances=[{string.Join(",", pausedInstanceIds)}]");

        foreach (var instanceId in pausedInstanceIds)
        {
            await _workflowCommands.ResumeAsync(
                    new ResumeWorkflowCommand(instanceId, changedByUserId, $"Driving task {taskId} reactivated."),
                    ct)
                .ConfigureAwait(false);
        }

        NotifyUiTaskListChanged($"reactivated task={taskId}");
        return new TaskCommandResult(true, $"Reactivated task {taskId}.", taskId);
    }

    /// <summary>
    /// Returns distinct ids of workflow instances that <paramref name="taskId"/> drives (linked via a
    /// <see cref="TaskLinkRole.Trigger"/> <see cref="TaskLink"/> to a <see cref="WorkflowInstance"/>),
    /// optionally filtered to the supplied <paramref name="statuses"/>.
    /// </summary>
    private static async Task<List<int>> GetLinkedWorkflowInstanceIdsAsync(
        SiNetSQLDbContext db,
        int taskId,
        CancellationToken ct,
        params WorkflowStatus[] statuses)
    {
        var linkedIds = await db.TaskLinks
            .AsNoTracking()
            .Where(l => l.TaskId == taskId
                     && l.LinkedEntityType == TaskLinkEntityType.WorkflowInstance
                     && l.Role == TaskLinkRole.Trigger)
            .Select(l => l.LinkedEntityId)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (linkedIds.Count == 0)
            return new List<int>();

        var intIds = linkedIds.Select(id => (int)id).ToList();

        var query = db.WorkflowInstances
            .AsNoTracking()
            .Where(i => intIds.Contains(i.Id));

        if (statuses.Length > 0)
            query = query.Where(i => statuses.Contains(i.Status));

        return await query
            .Select(i => i.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// True when <paramref name="instanceId"/> has at least one OTHER open trigger task besides
    /// <paramref name="excludeTaskId"/> — meaning it should not be paused when that task deactivates.
    /// </summary>
    private static async Task<bool> HasOtherOpenTriggerTaskAsync(
        SiNetSQLDbContext db,
        int instanceId,
        int excludeTaskId,
        CancellationToken ct)
    {
        return await (
                from link in db.TaskLinks.AsNoTracking()
                join t in db.ProjectAssignments.AsNoTracking() on link.TaskId equals t.Id
                join s in db.ProjectAssignmentStatuses.AsNoTracking() on t.StatusId equals s.Id
                where link.LinkedEntityType == TaskLinkEntityType.WorkflowInstance
                   && link.LinkedEntityId == instanceId
                   && link.Role == TaskLinkRole.Trigger
                   && link.TaskId != excludeTaskId
                   && s.IsOpen
                select link.Id)
            .AnyAsync(ct)
            .ConfigureAwait(false);
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
