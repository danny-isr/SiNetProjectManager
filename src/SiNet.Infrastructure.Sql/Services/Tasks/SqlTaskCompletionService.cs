using System.Diagnostics;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using SiNet.Application.Diagnostics; // TEMP WF-DEBUG
using SiNet.Application.Tasks;
using SiNet.Application.Workflow;

using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.Workflow;

using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Tasks;

/// <summary>
/// Native Infrastructure.Sql implementation of <see cref="ITaskCompletionService"/>. Ports the
/// decision logic from legacy <c>TaskCompletionCoordinator</c> and routes workflow auto-advance
/// exclusively through <see cref="IWorkflowCommandService"/>.
/// <para>
/// When the concrete native <see cref="NativeWorkflowCommandService"/> is in effect, task-close and
/// workflow auto-advance run on a single shared <see cref="SiNetSQLDbContext"/> (and, on relational
/// providers, one transaction), so the two are atomic (Phase 1d). With any other command service (e.g.
/// a test double or the fail-fast unbound service) it falls back to the separate-context model where a
/// failed advance is surfaced as a retryable <see cref="TaskCompletionResultDto.WorkflowAdvancePending"/>.
/// </para>
/// </summary>
public sealed class SqlTaskCompletionService : ITaskCompletionService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;
    private readonly IWorkflowCommandService _workflowCommands;

    public SqlTaskCompletionService(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        IWorkflowCommandService workflowCommands)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _workflowCommands = workflowCommands ?? throw new ArgumentNullException(nameof(workflowCommands));
    }

    public async ValueTask<TaskCompletionResultDto> CompleteAsync(CompleteTaskCommand command, CancellationToken ct)
    {
        // TEMP WF-DEBUG
        WorkflowDebugTrace.Step("TaskCompletion.Complete",
            $"task={command.TaskId} event={command.CompletionEventCode} result={command.TaskResultCode ?? "(none)"} user={command.UserId}");

        if (string.IsNullOrWhiteSpace(command.CompletionEventCode))
            return TaskCompletionResultDto.Failure("completionEventCode is required.");

        var behavior = ReviewCompletionEventBehavior.TryGet(command.CompletionEventCode);
        if (behavior is null)
            return TaskCompletionResultDto.Failure($"Unknown completion event '{command.CompletionEventCode}'.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var task = await db.ProjectAssignments
            .Include(t => t.TaskLinks)
            .FirstOrDefaultAsync(t => t.Id == command.TaskId, ct)
            .ConfigureAwait(false);

        if (task is null)
            return TaskCompletionResultDto.Failure($"Task {command.TaskId} not found.");

        var taskType = task.TaskTypeId.HasValue
            ? await db.TaskTypes.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == task.TaskTypeId.Value, ct)
                .ConfigureAwait(false)
            : null;

        if (taskType is null)
            return TaskCompletionResultDto.Failure($"Task {command.TaskId} has no TaskType.");

        if (!behavior.ApplicableTaskTypeCodes.Contains(taskType.Code, StringComparer.Ordinal))
        {
            return TaskCompletionResultDto.Failure(
                $"Event '{command.CompletionEventCode}' is not valid for task type '{taskType.Code}'.");
        }

        var interaction = ReviewTaskInteractionRegistry.TryGet(taskType.Code);
        var taskResultCode = command.TaskResultCode;

        if (!string.IsNullOrEmpty(taskResultCode))
        {
            if (behavior.AllowedTaskResultCodes.Count > 0
                && !behavior.AllowedTaskResultCodes.Contains(taskResultCode, StringComparer.Ordinal))
            {
                return TaskCompletionResultDto.Failure(
                    $"Result '{taskResultCode}' is not allowed for event '{command.CompletionEventCode}'.");
            }

            if (interaction is not null
                && interaction.AllowedTaskResultCodes.Count > 0
                && !interaction.AllowedTaskResultCodes.Contains(taskResultCode, StringComparer.Ordinal))
            {
                return TaskCompletionResultDto.Failure(
                    $"Result '{taskResultCode}' is not allowed for task type '{taskType.Code}'.");
            }
        }
        else if (behavior.AllowedTaskResultCodes.Count > 0)
        {
            if (behavior.AllowedTaskResultCodes.Count > 1)
            {
                return TaskCompletionResultDto.Failure(
                    $"Event '{command.CompletionEventCode}' requires a taskResultCode.");
            }

            taskResultCode = behavior.AllowedTaskResultCodes[0];
        }

        if (command.CompletedTaskLinkIds is { Count: > 0 })
        {
            var taskLinkIds = task.TaskLinks.Select(l => l.Id).ToHashSet();
            foreach (var id in command.CompletedTaskLinkIds)
            {
                if (!taskLinkIds.Contains(id))
                {
                    return TaskCompletionResultDto.Failure(
                        $"TaskLink {id} does not belong to task {command.TaskId}.");
                }
            }
        }

        var nowUtc = DateTime.UtcNow;
        var success = new TaskCompletionResultDto(
            Success: true,
            TaskClosed: false,
            WorkflowAdvanced: false,
            RecordedTaskResultCode: taskResultCode);

        if (command.CompletedTaskLinkIds is { Count: > 0 })
        {
            foreach (var link in task.TaskLinks.Where(l => l.IsWorkTarget && command.CompletedTaskLinkIds.Contains(l.Id)))
            {
                if (link.WorkStatus == WorkTargetStatus.Done)
                    continue;

                link.WorkStatus = WorkTargetStatus.Done;
                link.CompletedAtUtc = nowUtc;
                link.CompletedByUserId = command.UserId;
            }
        }

        if (!string.IsNullOrEmpty(taskResultCode))
        {
            var def = await db.TaskResultDefinitions
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Code == taskResultCode, ct)
                .ConfigureAwait(false);

            if (def is null)
            {
                return TaskCompletionResultDto.Failure(
                    $"TaskResultDefinition for code '{taskResultCode}' is not seeded.");
            }

            task.LastTaskResultId = def.Id;

            db.ProjectAssignmentEvents.Add(new ProjectAssignmentEvent
            {
                ProjectAssignmentId = task.Id,
                EventType = "TaskResult",
                TaskResultId = def.Id,
                Note = command.CompletionEventCode,
                CreatedByUserId = command.UserId,
                CreatedDate = nowUtc,
            });
        }

        var closure = await EvaluateClosureAsync(task, interaction, behavior).ConfigureAwait(false);
        var taskClosed = false;

        if (closure.ShouldClose)
        {
            var completedStatus = await db.ProjectAssignmentStatuses
                .FirstOrDefaultAsync(s => s.Code == TaskStatusCodes.Completed, ct)
                .ConfigureAwait(false);

            if (completedStatus is null)
            {
                return TaskCompletionResultDto.Failure(
                    $"Task {command.TaskId} should close but status '{TaskStatusCodes.Completed}' is not configured.");
            }

            var oldStatusId = task.StatusId;
            task.StatusId = completedStatus.Id;
            task.Status = completedStatus.Code;
            task.WorkPriority = null;
            task.Modified = DateTime.Now;
            task.EditorId = command.UserId;

            db.ProjectAssignmentEvents.Add(new ProjectAssignmentEvent
            {
                ProjectAssignmentId = task.Id,
                EventType = "StatusChange",
                OldStatusId = oldStatusId,
                NewStatusId = completedStatus.Id,
                Note = $"Closed by event {command.CompletionEventCode}.",
                CreatedByUserId = command.UserId,
                CreatedDate = nowUtc,
            });
            taskClosed = true;
        }

        int? newProjectStatusId = null;
        string? newProjectStatusCode = null;

        var statusCode = behavior.NewProjectStatusCode
                         ?? ReviewCompletionEventBehavior.ResolveResultDependentProjectStatus(
                             command.CompletionEventCode, taskResultCode);

        if (!string.IsNullOrEmpty(statusCode) && task.ProjectId is int projectId)
        {
            var status = await db.ProjectStatuses
                .FirstOrDefaultAsync(s => s.Code == statusCode && s.IsActive, ct)
                .ConfigureAwait(false);
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct).ConfigureAwait(false);

            if (status is not null && project is not null && project.ProjectStatusId != status.Id)
                project.ProjectStatusId = status.Id;

            if (status is not null)
            {
                newProjectStatusId = status.Id;
                newProjectStatusCode = status.Code;
            }
        }

        var willAutoAdvance = behavior.RequestWorkflowAdvance && taskClosed;
        var nativeCommands = _workflowCommands as NativeWorkflowCommandService;

        // TEMP WF-DEBUG
        WorkflowDebugTrace.Step("TaskCompletion.Closure",
            $"task={command.TaskId} recordedResult={taskResultCode ?? "(none)"} taskClosed={taskClosed} requestAdvance={behavior.RequestWorkflowAdvance} willAutoAdvance={willAutoAdvance} path={(willAutoAdvance && nativeCommands is not null ? "atomic" : "fallback")} newProjectStatus={newProjectStatusCode ?? "(unchanged)"}");

        // Atomic path (Phase 1d): when the native command service is in effect, run the auto-advance on
        // this same DbContext so it sees the not-yet-committed close and, on relational providers,
        // enlists in one transaction — making task-close + advance all-or-nothing.
        if (willAutoAdvance && nativeCommands is not null)
        {
            var useSharedTransaction = db.Database.IsRelational();
            IDbContextTransaction? sharedTx = useSharedTransaction
                ? await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false)
                : null;

            await using (sharedTx)
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);

                StageCompletionResultDto? stageAdvanceResult;
                try
                {
                    stageAdvanceResult = await nativeCommands
                        .CheckAndAutoAdvanceSharedAsync(db, new TaskClosedCommand(command.TaskId, command.UserId), ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // The shared transaction (when relational) rolls back on dispose, undoing the close
                    // together with the advance — the completion is reported as failed, not pending.
                    Trace.TraceError(
                        "[SqlTaskCompletionService] atomic auto-advance failed for task {0}; rolling back close: {1}",
                        command.TaskId,
                        ex);

                    return TaskCompletionResultDto.Failure(
                        $"Workflow auto-advance failed for task {command.TaskId}: {ex.Message}");
                }

                if (sharedTx is not null)
                    await sharedTx.CommitAsync(ct).ConfigureAwait(false);

                return success with
                {
                    TaskClosed = taskClosed,
                    WorkflowAdvanced = true,
                    NewProjectStatusId = newProjectStatusId,
                    NewProjectStatusCode = newProjectStatusCode,
                    StageAdvanceResult = stageAdvanceResult,
                };
            }
        }

        // Non-native fallback: commit the task-close now; the advance (if any) runs on a separate
        // context and is retryable if it fails.
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        StageCompletionResultDto? fallbackAdvanceResult = null;

        if (willAutoAdvance)
        {
            var (result, advanceError) = await TryAutoAdvanceAsync(command.TaskId, command.UserId, ct)
                .ConfigureAwait(false);

            if (advanceError is not null)
            {
                // The task's own writes already committed above. A failed follow-on auto-advance on a
                // SEPARATE DbContext must NOT be reported as a failed completion: the closure stands and
                // the advance (idempotent over committed state) is retryable.
                Trace.TraceWarning(
                    "[SqlTaskCompletionService] Task {0} closed but workflow auto-advance is pending (retryable): {1}",
                    command.TaskId,
                    advanceError);

                return success with
                {
                    TaskClosed = taskClosed,
                    WorkflowAdvanced = false,
                    WorkflowAdvancePending = true,
                    ErrorMessage = advanceError,
                    NewProjectStatusId = newProjectStatusId,
                    NewProjectStatusCode = newProjectStatusCode,
                };
            }

            fallbackAdvanceResult = result;
        }

        return success with
        {
            TaskClosed = taskClosed,
            WorkflowAdvanced = willAutoAdvance,
            NewProjectStatusId = newProjectStatusId,
            NewProjectStatusCode = newProjectStatusCode,
            StageAdvanceResult = fallbackAdvanceResult,
        };
    }

    private static ValueTask<(bool ShouldClose, string? Reason)> EvaluateClosureAsync(
        ProjectAssignment task,
        TaskInteractionDefinition? interaction,
        ReviewCompletionBehavior behavior)
    {
        if (behavior.ClosesAssociatedTask)
            return new ValueTask<(bool, string?)>((true, "event-closes-task"));

        if (interaction is null)
            return new ValueTask<(bool, string?)>((false, "no-interaction"));

        if (!interaction.AutoCloseOnCompletion)
            return new ValueTask<(bool, string?)>((false, "auto-close-disabled"));

        var workTargets = task.TaskLinks.Where(l => l.IsWorkTarget).ToList();
        if (workTargets.Count > 0)
        {
            var allDone = workTargets.All(l =>
                l.WorkStatus == WorkTargetStatus.Done ||
                l.WorkStatus == WorkTargetStatus.Skipped);

            if (!allDone)
                return new ValueTask<(bool, string?)>((false, "work-targets-pending"));
        }

        return new ValueTask<(bool, string?)>((true, "policy-satisfied"));
    }

    private async Task<(StageCompletionResultDto? Result, string? Error)> TryAutoAdvanceAsync(
        int taskId,
        int userId,
        CancellationToken ct)
    {
        try
        {
            var result = await _workflowCommands
                .CheckAndAutoAdvanceAsync(new TaskClosedCommand(taskId, userId), ct)
                .ConfigureAwait(false);

            return (result, null);
        }
        catch (Exception ex)
        {
            Trace.TraceError(
                "[SqlTaskCompletionService] CheckAndAutoAdvanceAsync failed for task {0}: {1}",
                taskId,
                ex);

            return (null, $"Workflow auto-advance failed for task {taskId}: {ex.Message}");
        }
    }
}
