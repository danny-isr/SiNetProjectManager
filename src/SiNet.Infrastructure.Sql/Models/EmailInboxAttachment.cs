using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiNetSQL.Models;

/// <summary>
/// Represents an attachment from an ingested email message.
/// Attachments are stored in ACC; this table contains metadata only.
/// 
/// STRICT POLICY:
/// - No binary data is stored in SQL.
/// - ContentSha256 is used for duplicate detection within a message.
/// - UNIQUE constraint on (MessageId, ContentSha256) prevents duplicate uploads.
/// </summary>
public class EmailInboxAttachment
{
    /// <summary>
    /// Primary key (auto-increment).
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Foreign Key to EmailInboxMessage.
    /// ON DELETE CASCADE: when message is deleted, attachments are removed.
    /// </summary>
    [Required]
    public int MessageId { get; set; }

    /// <summary>
    /// Zero-based index of this attachment within the email.
    /// Used to preserve original ordering.
    /// </summary>
    [Required]
    public int AttachmentIndex { get; set; }

    /// <summary>
    /// Original filename from the email attachment.
    /// May contain special characters or non-ASCII names.
    /// </summary>
    [MaxLength(260)]
    public string? OriginalFileName { get; set; }

    /// <summary>
    /// Filename as saved to ACC.
    /// May be sanitized or renamed to avoid conflicts.
    /// </summary>
    [MaxLength(260)]
    public string? SavedFileName { get; set; }

    /// <summary>
    /// SHA-256 hash of the attachment content.
    /// Used for deduplication within a single message.
    /// Format: 64-character lowercase hex string.
    /// </summary>
    [Required]
    [StringLength(64, MinimumLength = 64)]
    public string ContentSha256 { get; set; } = string.Empty;

    /// <summary>
    /// ACC Item ID after upload.
    /// The URN/ID returned by Autodesk Data Management API.
    /// </summary>
    [MaxLength(128)]
    public string? AccItemId { get; set; }

    /// <summary>
    /// ACC Version ID after upload.
    /// The version URN returned by Autodesk Data Management API.
    /// </summary>
    [MaxLength(128)]
    public string? AccVersionId { get; set; }

    /// <summary>
    /// True if this attachment was downloaded from an external link (e.g., Jumbo Mail, WeTransfer)
    /// rather than being a direct Gmail attachment. Used to distinguish in UI with a different icon.
    /// </summary>
    public bool IsExternalDownload { get; set; }

    // ═══════════════════════════════════════════════════════════════════════
    // Tagging: Links this attachment to a ProjectFile entry (determines
    // the target folder when moving from Inbox to the project in ACC).
    // Only ProjectFile records where OutSidData == true are valid targets.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// FK to ProjectFile — the user-selected tag that determines the
    /// destination folder in the project's ACC structure.
    /// NULL means "not yet tagged".
    /// </summary>
    public int? ProjectFileId { get; set; }

    /// <summary>
    /// FK to ProjectAlternative — the user-selected design alternative
    /// for the naming convention. NULL means "use default (1)".
    /// </summary>
    public int? ProjectAlternativeId { get; set; }

    // ═══════════════════════════════════════════════════════════════════════
    // Navigation Properties
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Navigation property to the parent EmailInboxMessage.
    /// </summary>
    [ForeignKey(nameof(MessageId))]
    public virtual EmailInboxMessage Message { get; set; } = null!;

    /// <summary>
    /// Navigation property to the tagged ProjectFile (target folder type).
    /// </summary>
    [ForeignKey(nameof(ProjectFileId))]
    public virtual ProjectFile? ProjectFile { get; set; }

    /// <summary>
    /// Navigation property to the selected design alternative.
    /// </summary>
    [ForeignKey(nameof(ProjectAlternativeId))]
    public virtual ProjectAlternative? ProjectAlternative { get; set; }
}
