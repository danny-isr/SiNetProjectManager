using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiNetSQL.Models;

/// <summary>
/// Represents a thread-to-project mapping for a global RFC 2822 email thread.
/// STRICT POLICY: Every row MUST be linked to a valid Project.
/// Records without a valid ProjectId are NOT allowed.
/// <para>
/// Stage D: identity model is now
/// <list type="bullet">
/// <item><see cref="Id"/> — surrogate technical primary key (identity).</item>
/// <item><see cref="ThreadUniqueId"/> — required, unique <b>business key</b>
/// (the global RFC 2822 thread identity).</item>
/// <item><see cref="ThreadId"/> — nullable runtime Gmail adapter mirror of the
/// mailbox-local Gmail thread id. Not a business key.</item>
/// </list>
/// </para>
/// </summary>
public class ThreadStatusMapping
{
    /// <summary>
    /// Surrogate technical primary key (identity). Stage D.
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>
    /// Runtime Gmail adapter mirror of the mailbox-local Gmail thread id.
    /// Nullable — this is NOT a business identifier. Use
    /// <see cref="ThreadUniqueId"/> for any business lookup or upsert.
    /// </summary>
    public string? ThreadId { get; set; }

    /// <summary>
    /// 🛑 Foreign Key to Projects table (REQUIRED - NOT NULLABLE)
    /// This enforces that every ThreadStatusMapping MUST belong to a Project.
    /// </summary>
    [Required]
    public int ProjectId { get; set; }

    /// <summary>
    /// The detected/assigned status of this thread.
    /// Should always be Assigned (1) for valid entries.
    /// </summary>
    public ThreadMappingStatus Status { get; set; } = ThreadMappingStatus.Assigned;

    /// <summary>
    /// Timestamp for cache invalidation and audit purposes
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// BIM 360 folder ID (optional, for Autodesk integration)
    /// </summary>
    public string? BimFolderId { get; set; }

    /// <summary>
    /// The Gmail label ID associated with this thread's status (optional).
    /// Runtime Gmail adapter field.
    /// </summary>
    public string? GmailLabelId { get; set; }

    /// <summary>
    /// Global, cross-mailbox <b>thread business identifier</b>, mirrored from
    /// <see cref="EmailInboxMessage.ThreadUniqueId"/>. Derived strictly from RFC
    /// 2822 headers (References / In-Reply-To / Message-ID) — never from
    /// <see cref="ThreadId"/> (Gmail thread id).
    /// <para>
    /// Stage D: required (NOT NULL) and unique. This is the canonical key for
    /// all business lookups, upserts, and cleanup.
    /// </para>
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string ThreadUniqueId { get; set; } = string.Empty;

    /// <summary>
    /// 🛑 Navigation Property to Project
    /// Enables cascade delete - when Project is deleted, all thread mappings are removed.
    /// </summary>
    [ForeignKey(nameof(ProjectId))]
    public virtual Project Project { get; set; } = null!;
}

/// <summary>
/// Status enum for thread classification
/// </summary>
public enum ThreadMappingStatus
{
    Unknown = 0,
    Assigned = 1,
    Pending = 2,
    Personal = 3,
    Irrelevant = 4
}
