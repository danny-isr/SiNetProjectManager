namespace SiNet.Application.Abstractions.Email;

/// <summary>
/// UI-agnostic description of an email to send. Mirrors the read-side <see cref="EmailSummary"/>
/// style: no WPF types, no connector specifics. Recipient fields accept plain RFC 5322 addresses
/// (optionally with a display name, e.g. <c>"Danny &lt;danny@example.com&gt;"</c>).
/// </summary>
public sealed record EmailSendRequest
{
    /// <summary>Primary recipients. At least one <c>To</c> recipient is required.</summary>
    public IReadOnlyList<string> To { get; init; } = Array.Empty<string>();

    /// <summary>Carbon-copy recipients.</summary>
    public IReadOnlyList<string> Cc { get; init; } = Array.Empty<string>();

    /// <summary>Blind carbon-copy recipients.</summary>
    public IReadOnlyList<string> Bcc { get; init; } = Array.Empty<string>();

    /// <summary>Message subject.</summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary>Message body.</summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>When <c>true</c>, <see cref="Body"/> is treated as HTML; otherwise plain text.</summary>
    public bool IsHtml { get; init; }

    /// <summary>
    /// Optional explicit sender / send-as address. When null/empty the authenticated mailbox's
    /// default address is used (Gmail fills <c>From</c> automatically).
    /// </summary>
    public string? From { get; init; }

    /// <summary>
    /// Optional Gmail thread id to send the message as a reply within an existing conversation.
    /// </summary>
    public string? ThreadId { get; init; }

    /// <summary>
    /// Optional <c>Message-ID</c> of the message being replied to, used to populate the
    /// <c>In-Reply-To</c> / <c>References</c> headers for correct threading.
    /// </summary>
    public string? InReplyToMessageId { get; init; }

    /// <summary>Optional attachments.</summary>
    public IReadOnlyList<EmailAttachment> Attachments { get; init; } = Array.Empty<EmailAttachment>();
}
