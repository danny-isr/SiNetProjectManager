using Microsoft.EntityFrameworkCore;
using SiNet.Infrastructure.Sql.Services.Tasks;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Workflow;

/// <summary>
/// Native task factory for the workflow engine: creates <see cref="ProjectAssignment"/> tasks
/// with proper work-queue priority, linking, and event recording. Re-homed from the legacy
/// <c>SiNetSQL.Services.TaskFactory</c> onto the native <see cref="TaskQueuePriorityEngine"/>
/// and <see cref="WorkQueueBucketResolver"/> primitives.
/// <para>Named <c>WorkflowTaskFactory</c> (not <c>TaskFactory</c>) to avoid colliding with
/// <see cref="System.Threading.Tasks.TaskFactory"/> under implicit usings.</para>
/// </summary>
internal static class WorkflowTaskFactory
{
    /// <summary>Describes how the new task should be linked to its trigger entity.</summary>
    public sealed record TaskLinkInfo(
        TaskLinkEntityType EntityType,
        int EntityId,
        TaskLinkRole Role = TaskLinkRole.Trigger,
        string? Description = null);

    /// <summary>
    /// Creates a task, assigns priority (if assignee exists), records a creation event,
    /// and optionally links it to a trigger entity — all against the caller-provided context.
    /// SaveChanges is called internally.
    /// </summary>
    public static async Task<ProjectAssignment> CreateAsync(
        SiNetSQLDbContext db,
        ProjectAssignment task,
        int userId,
        TaskLinkInfo? link = null,
        string? eventNote = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(task);

        task.Created ??= DateTime.Now;
        task.AuthorId ??= userId > 0 ? userId : null;

        if (task.TaskTypeId.HasValue && task.TaskType is null)
            task.TaskType = await db.TaskTypes.FindAsync([task.TaskTypeId.Value], ct).ConfigureAwait(false);

        WorkQueueBucketResolver.ApplyTaskTypeDefault(task, task.TaskType);

        if (task.AssignedToId.HasValue)
        {
            await TaskQueuePriorityEngine.InsertWithAutoPriorityAsync(db, task, ct).ConfigureAwait(false);
        }
        else
        {
            db.ProjectAssignments.Add(task);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        db.ProjectAssignmentEvents.Add(new ProjectAssignmentEvent
        {
            ProjectAssignmentId = task.Id,
            EventType = "Created",
            NewStatusId = task.StatusId,
            CreatedByUserId = userId,
            CreatedDate = DateTime.UtcNow,
            Note = eventNote ?? "משימה נוצרה",
        });

        if (link is not null)
        {
            db.TaskLinks.Add(new TaskLink
            {
                TaskId = task.Id,
                LinkedEntityType = link.EntityType,
                LinkedEntityId = link.EntityId,
                Role = link.Role,
                Description = link.Description,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = userId,
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return task;
    }

    /// <summary>Resolves the first actionable (open) status ID.</summary>
    public static async Task<int> GetOpenStatusIdAsync(SiNetSQLDbContext db, CancellationToken ct = default)
    {
        var status = await db.ProjectAssignmentStatuses
            .FirstOrDefaultAsync(s => s.IsActionable, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("No actionable status found in database.");
        return status.Id;
    }
}
