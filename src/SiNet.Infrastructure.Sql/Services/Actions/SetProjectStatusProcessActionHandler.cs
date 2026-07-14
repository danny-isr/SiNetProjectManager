using Microsoft.EntityFrameworkCore;
using SiNet.Application.Actions;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.Services.Actions;

internal sealed class SetProjectStatusProcessActionHandler : IProcessActionHandler
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;

    public SetProjectStatusProcessActionHandler(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public string ActionCode => ProcessActionCodes.SetProjectStatus;

    public async ValueTask<ActionExecutionResultDto> ExecuteAsync(
        ActionExecutionCommand command,
        CancellationToken cancellationToken = default)
    {
        var statusCode = WorkflowActionHelpers.ReadDataString(command, ActionExecutionDataKeys.ProjectStatusCode)
                         ?? WorkflowActionHelpers.ReadConfigString(command, ActionExecutionDataKeys.ProjectStatusCode);

        if (string.IsNullOrWhiteSpace(statusCode))
            return ActionExecutionResultDto.Failed(ActionCode, "ProjectStatusCode is required.");

        var instanceId = command.WorkflowInstanceId ?? 0;
        if (instanceId <= 0)
            return ActionExecutionResultDto.Failed(ActionCode, "WorkflowInstanceId is required.");

        var ambientDb = WorkflowActionHelpers.TryGetAmbientDbContext(command);
        var result = ambientDb is not null
            ? await WorkflowActionHelpers.SetProjectStatusByCodeAsync(
                ambientDb, statusCode, instanceId, command.UserId ?? 0, cancellationToken).ConfigureAwait(false)
            : await WorkflowActionHelpers.SetProjectStatusByCodeAsync(
                _dbFactory, statusCode, instanceId, command.UserId ?? 0, cancellationToken).ConfigureAwait(false);

        return result.Success
            ? ActionExecutionResultDto.Completed(ActionCode, result.Message)
            : ActionExecutionResultDto.Failed(ActionCode, result.Message);
    }
}
