namespace SiNet.Application.Tasks;

/// <summary>
/// Basic task CRUD for the Task Workbench (create/delete + lookup options).
/// Uses native Infrastructure.Sql queue/priority engines — no LegacyBridge.
/// </summary>
public interface ITaskWorkbenchService
{
    ValueTask<TaskCreationOptionsDto> GetTaskCreationOptionsAsync(CancellationToken ct = default);

    ValueTask<TaskCommandResult> CreateTaskAsync(
        CreateTaskRequest request,
        int changedByUserId,
        CancellationToken ct = default);

    ValueTask<TaskCommandResult> DeleteTaskAsync(
        int taskId,
        int changedByUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Deactivates a task that drives a workflow: pauses the linked non-terminal workflow instance(s)
    /// first, then sets the task to <c>Cancelled</c> and removes it from the queue. The task row and
    /// its <c>TaskLink</c>s are preserved so it can later be reactivated. Non-workflow tasks may also
    /// be deactivated (no workflow to pause).
    /// </summary>
    ValueTask<TaskCommandResult> DeactivateTaskAsync(
        int taskId,
        int changedByUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Reactivates a previously deactivated task: sets it back to <c>Open</c>, re-enters it into the
    /// work queue, and resumes any linked <c>Paused</c> workflow instance.
    /// </summary>
    ValueTask<TaskCommandResult> ReactivateTaskAsync(
        int taskId,
        int changedByUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns distinct assignee ids for demo tasks (DEBUG_TASK_SEED prefix) — diagnostics only.
    /// </summary>
    ValueTask<IReadOnlyList<int>> GetDemoTaskAssigneeUserIdsAsync(CancellationToken ct = default);
}
