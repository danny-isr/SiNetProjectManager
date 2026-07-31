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
    IReadOnlyList<EmailMessageAttachmentDetails> Attachments,
    string? HtmlBody = null,
    IReadOnlyList<EmailInlineImage>? InlineImages = null,
    string? InternetMessageId = null,
    string? InReplyTo = null,
    string? References = null,
    IReadOnlyList<string>? ToAddresses = null,
    IReadOnlyList<string>? CcAddresses = null)
{
    public bool HasAttachments => Attachments.Count > 0;

    /// <summary>Embedded images referenced from <see cref="HtmlBody"/> via <c>cid:</c>. Never null.</summary>
    public IReadOnlyList<EmailInlineImage> InlineImages { get; init; } = InlineImages ?? [];

    /// <summary>Parsed To addresses (bare mailboxes). Never null.</summary>
    public IReadOnlyList<string> ToAddresses { get; init; } = ToAddresses ?? [];

    /// <summary>Parsed Cc addresses (bare mailboxes). Never null.</summary>
    public IReadOnlyList<string> CcAddresses { get; init; } = CcAddresses ?? [];
}
