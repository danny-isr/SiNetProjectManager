using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiNetSQL.Models;

/// <summary>
/// Represents an email message ingested into the system's Inbox.
/// All messages are first uploaded to a dedicated ACC "Office Inbox" project.
/// 
/// STRICT POLICY:
/// - ProjectId is NEVER NULL. New messages default to the "ניהול  משרד - כללי" project.
/// - MessageUniqueId must be unique (enforced by database constraint).
/// - Deduplication is handled at INSERT time via UNIQUE constraint.
/// </summary>
public class EmailInboxMessage
{
    /// <summary>
    /// Primary key (auto-increment).
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Unique identifier for the email message.
    /// Derived from the Message-ID header (RFC 2822).
    /// Used for deduplication across multiple users.
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string MessageUniqueId { get; set; } = string.Empty;

    /// <summary>
    /// Foreign Key to Projects table (REQUIRED - NOT NULLABLE).
    /// For new messages, this defaults to the "ניהול  משרד - כללי" project.
    /// Application logic ensures this is never null.
    /// </summary>
    [Required]
    public int ProjectId { get; set; }

    /// <summary>
    /// Gmail thread ID.
    /// <para>
    /// <b>Gmail adapter / runtime / mailbox-local identifier.</b> This is the value
    /// of <c>Message.threadId</c> returned by the Gmail API for the ingesting user's
    /// mailbox and is <b>not</b> a global thread business identity. Treat it as a
    /// cache for same-user thread-aware operations only (e.g., conditional cleanup
    /// of ThreadStatusMapping on unfile). Cross-mailbox thread identity must be
    /// derived from the RFC 2822 <see cref="References"/> / <see cref="InReplyTo"/>
    /// headers — see Gap 4 in the gap register.
    /// </para>
    /// Nullable for pre-migration rows and non-Gmail sources.
    /// </summary>
    [MaxLength(64)]
    public string? GmailThreadId { get; set; }

    // ═══════════════════════════════════════════════════════════════════════
    // RFC 2822 identity headers — global, cross-mailbox business identifiers.
    // MessageUniqueId above is derived from InternetMessageId via
    // MessageKeyGenerator.GetMessageUniqueId.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// RFC 2822 <c>Message-ID</c> header value (raw form, may include angle brackets).
    /// This is the canonical global identifier of the message and the source of
    /// <see cref="MessageUniqueId"/>.
    /// <para>
    /// <b>STRICT POLICY:</b> This field is <b>required</b>, <b>NOT NULL</b>, and
    /// <b>UNIQUE</b> at the database level. Emails ingested without an RFC 2822
    /// <c>Message-ID</c> header are treated as an ingestion error and are not
    /// persisted. The Gmail message id is no longer accepted as a fallback for
    /// new rows.
    /// </para>
    /// </summary>
    [Required]
    [MaxLength(998)] // RFC 5322 line length limit
    public string InternetMessageId { get; set; } = string.Empty;

    /// <summary>
    /// RFC 2822 <c>In-Reply-To</c> header value (raw). Used for global thread
    /// identity reconstruction. Nullable.
    /// </summary>
    [MaxLength(998)]
    public string? InReplyTo { get; set; }

    /// <summary>
    /// RFC 2822 <c>References</c> header value (raw, space-separated list of
    /// message-ids). Used for global thread identity reconstruction. Nullable.
    /// May exceed 998 chars on deeply nested threads, so stored as nvarchar(max).
    /// </summary>
    public string? References { get; set; }

    /// <summary>
    /// Global, cross-mailbox <b>thread business identifier</b>, derived from
    /// RFC 2822 headers via
    /// <c>MessageKeyGenerator.GetThreadUniqueId(References, InReplyTo, InternetMessageId)</c>.
    /// <para>
    /// Required (NOT NULL). Not unique — multiple messages share the same thread.
    /// Gmail's <c>threadId</c> is intentionally not used as a fallback; it stays
    /// in <see cref="GmailThreadId"/> as a mailbox-local runtime adapter.
    /// </para>
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string ThreadUniqueId { get; set; } = string.Empty;

    /// <summary>
    /// Short, deterministic, filesystem-safe key derived from
    /// <see cref="ThreadUniqueId"/> via <c>MessageKeyGenerator.GetThreadKey</c>.
    /// Used for future ACC folder layout and as a compact cache key.
    /// Required (NOT NULL). Not unique.
    /// </summary>
    [Required]
    [MaxLength(8)]
    public string ThreadKey { get; set; } = string.Empty;

    /// <summary>
    /// Email sender address (From header).
    /// Max 320 chars per RFC 5321 (64 local + @ + 255 domain).
    /// </summary>
    [MaxLength(320)]
    public string? FromAddress { get; set; }

    /// <summary>
    /// Email subject line.
    /// </summary>
    [MaxLength(500)]
    public string? Subject { get; set; }

    /// <summary>
    /// UTC timestamp when the email was received.
    /// Parsed from the Date header.
    /// </summary>
    public DateTime ReceivedUtc { get; set; }

    /// <summary>
    /// Current processing status of this message.
    /// </summary>
    public EmailInboxStatus Status { get; set; } = EmailInboxStatus.Pending;

    /// <summary>
    /// The ACC project ID where the Inbox folder resides.
    /// This is the dedicated "Office Inbox" project.
    /// </summary>
    [MaxLength(64)]
    public string? InboxAccProjectId { get; set; }

    /// <summary>
    /// The ACC folder ID where attachments are stored.
    /// Format: /_Inbox/YYYY/YYYY-MM/MSG_{MessageKey}/
    /// </summary>
    [MaxLength(128)]
    public string? InboxAccFolderId { get; set; }

    /// <summary>
    /// Windows login name of the user who ingested this message.
    /// Format: DOMAIN\username or username
    /// </summary>
    [MaxLength(256)]
    public string? CreatedByLogin { get; set; }

    /// <summary>
    /// UTC timestamp when this record was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp when this record was last updated.
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Error message if Status is Error.
    /// Contains exception details or API error response.
    /// </summary>
    public string? Error { get; set; }

    // ═══════════════════════════════════════════════════════════════════════
    // Lease-Based Processing Fields
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Windows login name of the worker currently processing this message.
    /// Used for lease tracking - only set when Status = Processing.
    /// Cleared when processing completes or lease is released.
    /// Format: DOMAIN\username or username
    /// </summary>
    [MaxLength(256)]
    public string? ProcessingByLogin { get; set; }

    /// <summary>
    /// UTC timestamp when the current processing lease was acquired.
    /// Used with TTL to detect crashed workers.
    /// If (UtcNow - ProcessingStartedAtUtc) > TTL, lease can be reclaimed.
    /// </summary>
    public DateTime? ProcessingStartedAtUtc { get; set; }

    // ═══════════════════════════════════════════════════════════════════════
    // Navigation Properties
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Navigation property to the associated Project.
    /// For new messages, this defaults to the "ניהול  משרד - כללי" project.
    /// </summary>
    [ForeignKey(nameof(ProjectId))]
    public virtual Project Project { get; set; } = null!;

    /// <summary>
    /// Collection of attachments belonging to this message.
    /// Cascade delete: when message is deleted, attachments are also deleted.
    /// </summary>
    public virtual ICollection<EmailInboxAttachment> Attachments { get; set; } = new List<EmailInboxAttachment>();
}
