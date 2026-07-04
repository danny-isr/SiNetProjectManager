using SiNet.Domain.ValueObjects;

namespace SiNet.Application.Abstractions.Email;

/// <summary>
/// Full read-only email message details for the New System read slice. This stays UI-agnostic:
/// it carries mailbox data only, with no WPF formatting concerns and no write/send behavior.
/// </summary>
public sealed record EmailMessageDetails(
    string MessageId,
    string ThreadId,
    EmailAddress From,
    string Subject,
    DateTimeOffset ReceivedAt,
    string BodyText,
    IReadOnlyList<EmailMessageAttachmentDetails> Attachments)
{
    public bool HasAttachments => Attachments.Count > 0;
}
