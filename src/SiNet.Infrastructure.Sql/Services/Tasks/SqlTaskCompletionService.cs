using Microsoft.EntityFrameworkCore;
using SiNet.Application.Tasks;
using SiNet.Application.Workflow;
using SiNet.Infrastructure.Sql.Constants;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Tasks;

/// <summary>
/// Native Infrastructure.Sql implementation of <see cref="ITaskCompletionService"/>. Ports the
/// decision logic from legacy <c>TaskCompletionCoordinator</c> and routes workflow auto-advance
/// exclusively through <see cref="IWorkflowCommandService"/>.
/// </summary>
public sealed class SqlTaskCompletionService : ITaskCompletionService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;
    private readonly IWorkflowCommandService? _workflowCommands;

    public SqlTaskCompletionService(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        IWorkflowCommandService? workflowCommands = null)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _workflowCommands = workflowCommands;
    }

    public async ValueTask<TaskCompletionResultDto> CompleteAsync(CompleteTaskCommand command, CancellationToken ct)
    {
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

            if (completedStatus is not null)
            {
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

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var workflowAdvanced = behavior.RequestWorkflowAdvance && taskClosed;
        StageCompletionResultDto? stageAdvanceResult = null;

        if (workflowAdvanced)
            stageAdvanceResult = await TryAutoAdvanceAsync(command.TaskId, command.UserId, ct).ConfigureAwait(false);

        return success with
        {
            TaskClosed = taskClosed,
            WorkflowAdvanced = workflowAdvanced,
            NewProjectStatusId = newProjectStatusId,
            NewProjectStatusCode = newProjectStatusCode,
            StageAdvanceResult = stageAdvanceResult,
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

    private async Task<StageCompletionResultDto?> TryAutoAdvanceAsync(int taskId, int userId, CancellationToken ct)
    {
        if (_workflowCommands is null)
            return null;

        try
        {
            return await _workflowCommands
                .CheckAndAutoAdvanceAsync(new TaskClosedCommand(taskId, userId), ct)
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }
}
