namespace SiNet.Application.Notifications;

/// <summary>
/// Delivery outcome for a workflow notification.
/// </summary>
public enum NotificationDeliveryStatus
{
    /// <summary>The notification was delivered on the active channel.</summary>
    Delivered,

    /// <summary>No template/recipients/config were supplied, so there was nothing to deliver (not a failure).</summary>
    NothingToDeliver,

    /// <summary>Delivery was attempted but failed; the caller should treat this as an action failure.</summary>
    Failed,
}

/// <summary>
/// Request to deliver a workflow-driven notification. Pure Application contract — no EF / WPF / Google
/// types. Built by the <c>SendNotification</c> process-action handler from the transition
/// <c>ConfigJson</c> plus the action execution context.
/// </summary>
/// <param name="Template">The notification template/body key from config, when supplied.</param>
/// <param name="Recipients">Resolved recipient addresses/ids from config; empty when none configured.</param>
/// <param name="RawConfigJson">The raw <c>ConfigJson</c> as received, for auditing/diagnostics.</param>
/// <param name="ProjectId">The owning project id, when known.</param>
/// <param name="WorkflowInstanceId">The workflow instance the notification relates to, when known.</param>
/// <param name="TaskId">The task the notification relates to, when known.</param>
/// <param name="UserId">The acting user id, when known.</param>
public sealed record NotificationDeliveryRequest(
    string? Template,
    IReadOnlyList<string> Recipients,
    string? RawConfigJson,
    int? ProjectId,
    int? WorkflowInstanceId,
    int? TaskId,
    int? UserId);

/// <summary>Outcome of <see cref="INotificationDeliveryService.DeliverAsync"/>.</summary>
public sealed record NotificationDeliveryResult(
    NotificationDeliveryStatus Status,
    string Channel,
    string? Message = null)
{
    public static NotificationDeliveryResult Delivered(string channel, string? message = null) =>
        new(NotificationDeliveryStatus.Delivered, channel, message);

    public static NotificationDeliveryResult NothingToDeliver(string channel, string? message = null) =>
        new(NotificationDeliveryStatus.NothingToDeliver, channel, message);

    public static NotificationDeliveryResult Failed(string channel, string message) =>
        new(NotificationDeliveryStatus.Failed, channel, message);
}

/// <summary>
/// Delivers workflow notifications on the host's configured channel. This is the single seam behind
/// which concrete channels live: today a log/audit implementation records intent without any external
/// side effect; a Gmail (<c>IEmailSender</c>) or in-app channel can replace it behind this same port
/// once policy (G-Policy) approves, with no change to the <c>SendNotification</c> handler.
/// </summary>
public interface INotificationDeliveryService
{
    ValueTask<NotificationDeliveryResult> DeliverAsync(
        NotificationDeliveryRequest request,
        CancellationToken cancellationToken = default);
}
