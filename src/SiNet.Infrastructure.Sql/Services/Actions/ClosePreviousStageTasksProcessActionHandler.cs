using Microsoft.EntityFrameworkCore;
using SiNet.Application.Actions;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Actions;

/// <summary>
/// Native handler for <see cref="ProcessActionCodes.ClosePreviousStageTasks"/>. When a workflow leaves
/// a stage, closes the open tasks that were provisioned for (tagged to) that stage, so they no longer
/// appear in work queues. Re-homed from the legacy
/// <c>SiNetSQL.Domain.Actions.Handlers.ClosePreviousStageTasksProcessActionHandler</c>; system-closes
/// via <see cref="WorkflowActionHelpers.CloseTasksAsSystemAsync"/> (no auto-advance is requested).
/// </summary>
internal sealed class ClosePreviousStageTasksProcessActionHandler : IProcessActionHandler
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;

    public ClosePreviousStageTasksProcessActionHandler(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public string ActionCode => ProcessActionCodes.ClosePreviousStageTasks;

    public async ValueTask<ActionExecutionResultDto> ExecuteAsync(
        ActionExecutionCommand command,
        CancellationToken cancellationToken = default)
    {
        var instanceId = command.WorkflowInstanceId ?? 0;
        if (instanceId <= 0)
            return ActionExecutionResultDto.Failed(ActionCode, "WorkflowInstanceId is required.");

        var fromStageId = WorkflowActionHelpers.ReadDataInt(command, ActionExecutionDataKeys.FromStageId);
        if (fromStageId is null or <= 0)
            return ActionExecutionResultDto.Failed(ActionCode, "FromStageId is required.");

        var (db, owns) = await WorkflowActionHelpers.ResolveDbContextAsync(command, _dbFactory, cancellationToken).ConfigureAwait(false);
        try
        {
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
                return ActionExecutionResultDto.Completed(ActionCode, "No open tasks to close.");

            var (success, closedCount, error) = await WorkflowActionHelpers.CloseTasksAsSystemAsync(
                db,
                linkedTaskIds,
                command.UserId ?? 0,
                note: $"Auto-closed on leaving stage {fromStageId.Value}.",
                cancellationToken).ConfigureAwait(false);

            return success
                ? ActionExecutionResultDto.Completed(ActionCode, $"Closed {closedCount} task(s) from the previous stage.")
                : ActionExecutionResultDto.Failed(ActionCode, error ?? "System close failed.");
        }
        finally
        {
            if (owns)
                await db.DisposeAsync().ConfigureAwait(false);
        }
    }
}
