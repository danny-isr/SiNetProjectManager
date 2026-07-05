using SiNet.Application.Actions;

namespace SiNet.Infrastructure.Sql.Services.Actions;

/// <summary>
/// Safe foundation-slice handler for <see cref="ProcessActionCodes.SendNotification"/>. Performs no
/// side effects — proves the Application action dispatcher wiring without touching email/ACC/workflow
/// write paths.
/// </summary>
internal sealed class SendNotificationProcessActionHandler : IProcessActionHandler
{
    public string ActionCode => ProcessActionCodes.SendNotification;

    public ValueTask<ActionExecutionResultDto> ExecuteAsync(
        ActionExecutionCommand command,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return ValueTask.FromResult(
            ActionExecutionResultDto.Completed(
                ActionCode,
                message: "SendNotification acknowledged (foundation no-op handler)."));
    }
}
