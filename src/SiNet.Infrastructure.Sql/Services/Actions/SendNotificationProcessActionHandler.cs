using SiNet.Application.Actions;
using SiNet.Application.Notifications;

namespace SiNet.Infrastructure.Sql.Services.Actions;

/// <summary>
/// Handler for <see cref="ProcessActionCodes.SendNotification"/>. Parses the transition
/// <c>ConfigJson</c> (<c>{"template":"...","to":"..."}</c>) and delegates delivery to
/// <see cref="INotificationDeliveryService"/>. The active channel is host-configured (log/audit by
/// default, policy-gated Gmail/in-app later) — this handler is channel-agnostic. A failed delivery
/// maps to <see cref="ActionExecutionResultDto.Failed"/> so a workflow transition blocks; a
/// nothing-to-deliver result still completes (no configured content is not an error).
/// </summary>
internal sealed class SendNotificationProcessActionHandler : IProcessActionHandler
{
    private const string TemplateConfigKey = "template";
    private const string RecipientsConfigKey = "to";

    private readonly INotificationDeliveryService _delivery;

    public SendNotificationProcessActionHandler(INotificationDeliveryService delivery)
    {
        _delivery = delivery;
    }

    public string ActionCode => ProcessActionCodes.SendNotification;

    public async ValueTask<ActionExecutionResultDto> ExecuteAsync(
        ActionExecutionCommand command,
        CancellationToken cancellationToken = default)
    {
        var request = new NotificationDeliveryRequest(
            Template: WorkflowActionHelpers.ReadConfigString(command, TemplateConfigKey),
            Recipients: WorkflowActionHelpers.ReadConfigStringList(command, RecipientsConfigKey),
            RawConfigJson: WorkflowActionHelpers.ReadDataString(command, ActionExecutionDataKeys.ConfigJson),
            ProjectId: command.ProjectId,
            WorkflowInstanceId: command.WorkflowInstanceId,
            TaskId: command.TaskId,
            UserId: command.UserId);

        var result = await _delivery.DeliverAsync(request, cancellationToken).ConfigureAwait(false);

        return result.Status == NotificationDeliveryStatus.Failed
            ? ActionExecutionResultDto.Failed(ActionCode, result.Message ?? "Notification delivery failed.")
            : ActionExecutionResultDto.Completed(ActionCode, result.Message ?? "Notification handled.");
    }
}
