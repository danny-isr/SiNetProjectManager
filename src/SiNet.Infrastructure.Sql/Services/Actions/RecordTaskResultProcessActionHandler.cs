using Microsoft.EntityFrameworkCore;
using SiNet.Application.Actions;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Actions;

internal sealed class RecordTaskResultProcessActionHandler : IProcessActionHandler
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;

    public RecordTaskResultProcessActionHandler(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public string ActionCode => ProcessActionCodes.RecordTaskResult;

    public async ValueTask<ActionExecutionResultDto> ExecuteAsync(
        ActionExecutionCommand command,
        CancellationToken cancellationToken = default)
    {
        var instanceId = command.WorkflowInstanceId ?? 0;
        if (instanceId <= 0)
            return ActionExecutionResultDto.Failed(ActionCode, "WorkflowInstanceId is required.");

        var resultCode = WorkflowActionHelpers.ReadDataString(command, ActionExecutionDataKeys.TaskResultCode)
                         ?? WorkflowActionHelpers.ReadConfigString(command, ActionExecutionDataKeys.TaskResultCode);

        if (string.IsNullOrWhiteSpace(resultCode))
            return ActionExecutionResultDto.Failed(ActionCode, "TaskResultCode is required.");

        var fromStageId = WorkflowActionHelpers.ReadDataInt(command, ActionExecutionDataKeys.FromStageId);
        if (fromStageId is null or <= 0)
            return ActionExecutionResultDto.Failed(ActionCode, "FromStageId is required.");

        var (db, owns) = await WorkflowActionHelpers.ResolveDbContextAsync(command, _dbFactory, cancellationToken).ConfigureAwait(false);
        try
        {
            var taskResult = await db.TaskResultDefinitions
                .FirstOrDefaultAsync(r => r.Code == resultCode && r.IsActive, cancellationToken)
                .ConfigureAwait(false);

            if (taskResult is null)
                return ActionExecutionResultDto.Failed(ActionCode, $"TaskResultDefinition '{resultCode}' is not seeded.");

            var stageTag = WorkflowActionHelpers.BuildStageTag(fromStageId.Value);

            var linkedTaskIds = await db.TaskLinks
                .AsNoTracking()
                .Where(l => l.LinkedEntityType == TaskLinkEntityType.WorkflowInstance
                            && l.LinkedEntityId == instanceId
                            && l.Role == TaskLinkRole.Trigger
                            && l.Description == stageTag)
                .Select(l => l.TaskId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (linkedTaskIds.Count == 0)
            {
                return ActionExecutionResultDto.Completed(
                    ActionCode,
                    message: "No stage-linked tasks found — nothing to record.");
            }

            var assignments = await db.ProjectAssignments
                .Where(pa => linkedTaskIds.Contains(pa.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var now = DateTime.UtcNow;
            var userId = command.UserId ?? 0;

            foreach (var assignment in assignments)
            {
                assignment.LastTaskResultId = taskResult.Id;
                db.ProjectAssignmentEvents.Add(new ProjectAssignmentEvent
                {
                    ProjectAssignmentId = assignment.Id,
                    EventType = "TaskResult",
                    TaskResultId = taskResult.Id,
                    Note = $"Result: {taskResult.Code}",
                    CreatedByUserId = userId,
                    CreatedDate = now,
                });
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return ActionExecutionResultDto.Completed(
                ActionCode,
                message: $"Recorded {resultCode} on {assignments.Count} task(s).");
        }
        finally
        {
            if (owns)
                await db.DisposeAsync().ConfigureAwait(false);
        }
    }
}
