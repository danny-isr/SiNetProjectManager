using Microsoft.EntityFrameworkCore;
using SiNet.Application.Actions;
using SiNet.Infrastructure.Sql.Constants;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.Services.Actions;

internal sealed class SetBillingPendingProcessActionHandler : IProcessActionHandler
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;

    public SetBillingPendingProcessActionHandler(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public string ActionCode => ProcessActionCodes.SetBillingPending;

    public async ValueTask<ActionExecutionResultDto> ExecuteAsync(
        ActionExecutionCommand command,
        CancellationToken cancellationToken = default)
    {
        var instanceId = command.WorkflowInstanceId ?? 0;
        if (instanceId <= 0)
            return ActionExecutionResultDto.Failed(ActionCode, "WorkflowInstanceId is required.");

        var ambientDb = WorkflowActionHelpers.TryGetAmbientDbContext(command);
        var result = ambientDb is not null
            ? await WorkflowActionHelpers.SetProjectStatusByCodeAsync(
                ambientDb, ProjectStatusCodes.BillingPending, instanceId, command.UserId ?? 0, cancellationToken).ConfigureAwait(false)
            : await WorkflowActionHelpers.SetProjectStatusByCodeAsync(
                _dbFactory, ProjectStatusCodes.BillingPending, instanceId, command.UserId ?? 0, cancellationToken).ConfigureAwait(false);

        return result.Success
            ? ActionExecutionResultDto.Completed(ActionCode, result.Message)
            : ActionExecutionResultDto.Failed(ActionCode, result.Message);
    }
}
