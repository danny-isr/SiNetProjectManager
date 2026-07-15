using System.Diagnostics;
using SiNet.Application.Notifications;

namespace SiNet.Infrastructure.Sql.Services.Notifications;

/// <summary>
/// Log/audit-only implementation of <see cref="INotificationDeliveryService"/>. Records structured
/// notification intent via <see cref="Trace"/> and returns a non-failing result — it performs no
/// external side effect (no Gmail send, no in-app surface). This is the policy-safe default channel:
/// real channels (Gmail via <c>IEmailSender</c>, or an in-app surface) can replace it behind the same
/// port once G-Policy approves, with no change to <c>SendNotificationProcessActionHandler</c>.
/// </summary>
internal sealed class LogNotificationDeliveryService : INotificationDeliveryService
{
    private const string Channel = "log";

    public ValueTask<NotificationDeliveryResult> DeliverAsync(
        NotificationDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        var hasContent = request.Recipients.Count > 0
            || !string.IsNullOrWhiteSpace(request.Template)
            || !string.IsNullOrWhiteSpace(request.RawConfigJson);

        if (!hasContent)
        {
            Trace.TraceInformation(
                "[SendNotification] No template/recipients/config for project {0} instance {1}; nothing to deliver.",
                request.ProjectId,
                request.WorkflowInstanceId);

            return ValueTask.FromResult(
                NotificationDeliveryResult.NothingToDeliver(
                    Channel,
                    "No notification content configured; nothing to deliver."));
        }

        var recipients = request.Recipients.Count > 0 ? string.Join(", ", request.Recipients) : "(none)";
        var template = string.IsNullOrWhiteSpace(request.Template) ? "(none)" : request.Template;

        Trace.TraceInformation(
            "[SendNotification] Delivered (log channel). Project={0} Instance={1} Task={2} User={3} " +
            "Template='{4}' Recipients=[{5}] Config={6}",
            request.ProjectId,
            request.WorkflowInstanceId,
            request.TaskId,
            request.UserId,
            template,
            recipients,
            request.RawConfigJson ?? "(none)");

        return ValueTask.FromResult(
            NotificationDeliveryResult.Delivered(
                Channel,
                $"Notification recorded (log channel) for {recipients}."));
    }
}
