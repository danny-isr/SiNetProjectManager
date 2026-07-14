using Microsoft.EntityFrameworkCore;
using SiNet.Application.Actions;
using SiNet.Infrastructure.Sql.Constants;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.Services.Actions;

/// <summary>
/// Native handler for <see cref="ProcessActionCodes.CloseProject"/>. System-closes any remaining open
/// tasks for the project and sets the project to <see cref="ProjectStatusCodes.Closed"/> (the single
/// final status). Re-homed from the legacy
/// <c>SiNetSQL.Domain.Actions.Handlers.CloseProjectProcessActionHandler</c>. No workflow auto-advance is
/// requested for the system close.
/// </summary>
internal sealed class CloseProjectProcessActionHandler : IProcessActionHandler
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;

    public CloseProjectProcessActionHandler(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public string ActionCode => ProcessActionCodes.CloseProject;

    public async ValueTask<ActionExecutionResultDto> ExecuteAsync(
        ActionExecutionCommand command,
        CancellationToken cancellationToken = default)
    {
        var instanceId = command.WorkflowInstanceId ?? 0;
        if (instanceId <= 0)
            return ActionExecutionResultDto.Failed(ActionCode, "WorkflowInstanceId is required.");

        var (db, owns) = await WorkflowActionHelpers.ResolveDbContextAsync(command, _dbFactory, cancellationToken).ConfigureAwait(false);
        try
        {
            var projectId = await db.WorkflowInstances
                .AsNoTracking()
                .Where(i => i.Id == instanceId && i.IsProjectBound)
                .Select(i => (int?)i.ProjectId)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (projectId is int pid)
            {
                var openTaskIds = await db.ProjectAssignments
                    .AsNoTracking()
                    .Where(pa => pa.ProjectId == pid
                              && pa.AssignmentStatus != null
                              && pa.AssignmentStatus.IsOpen)
                    .Select(pa => pa.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (openTaskIds.Count > 0)
                {
                    var (closeSuccess, _, closeError) = await WorkflowActionHelpers.CloseTasksAsSystemAsync(
                        db,
                        openTaskIds,
                        command.UserId ?? 0,
                        note: "Auto-closed on project close.",
                        cancellationToken).ConfigureAwait(false);

                    if (!closeSuccess)
                        return ActionExecutionResultDto.Failed(ActionCode, closeError ?? "System close failed.");
                }
            }

            var result = await WorkflowActionHelpers.SetProjectStatusByCodeAsync(
                db,
                ProjectStatusCodes.Closed,
                instanceId,
                command.UserId ?? 0,
                cancellationToken).ConfigureAwait(false);

            return result.Success
                ? ActionExecutionResultDto.Completed(ActionCode, "Project closed.")
                : ActionExecutionResultDto.Failed(ActionCode, result.Message);
        }
        finally
        {
            if (owns)
                await db.DisposeAsync().ConfigureAwait(false);
        }
    }
}
