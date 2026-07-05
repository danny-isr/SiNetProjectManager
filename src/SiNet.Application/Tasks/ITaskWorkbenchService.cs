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
    /// Returns distinct assignee ids for demo tasks (DEBUG_TASK_SEED prefix) — diagnostics only.
    /// </summary>
    ValueTask<IReadOnlyList<int>> GetDemoTaskAssigneeUserIdsAsync(CancellationToken ct = default);
}
