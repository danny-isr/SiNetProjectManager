using SiNet.Application.Actions;

namespace SiNet.Infrastructure.Sql.Services.Actions;

/// <summary>
/// Marker handler: stage task provisioning remains orchestrator-owned. Returns an explicit completed
/// no-op so callers know the action was recognized.
/// </summary>
internal sealed class CreateStageTasksProcessActionHandler : IProcessActionHandler
{
    public string ActionCode => ProcessActionCodes.CreateStageTasks;

    public ValueTask<ActionExecutionResultDto> ExecuteAsync(
        ActionExecutionCommand command,
        CancellationToken cancellationToken = default)
    {
        _ = command;
        _ = cancellationToken;
        return ValueTask.FromResult(
            ActionExecutionResultDto.Completed(
                ActionCode,
                message: "CreateStageTasks acknowledged — provisioning is orchestrator-owned."));
    }
}
