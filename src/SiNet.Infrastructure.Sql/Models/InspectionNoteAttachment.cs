namespace SiNetSQL.Models;

using System.ComponentModel.DataAnnotations;

/// <summary>Type of attachment linked to an inspection note.</summary>
public enum InspectionNoteAttachmentType
{
    Screenshot = 0,
    Document = 1,
    Other = 2
}

/// <summary>
/// External attachment for an inspection note. Image/file binaries are NOT stored in the DB —
/// only Google Drive metadata. The caller uploads the file to the project's Drive folder
/// and persists the resulting Drive ID and URL here.
/// </summary>
public class InspectionNoteAttachment
{
    [Key]
    public int AttachmentId { get; set; }

    public long NoteId { get; set; }

    public InspectionNoteAttachmentType AttachmentType { get; set; } = InspectionNoteAttachmentType.Screenshot;

    public string? FileName { get; set; }

    public string? GoogleDriveFileId { get; set; }

    public string? GoogleDriveUrl { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public int? UploadedByUserId { get; set; }

    public string? Caption { get; set; }

    /// <summary>
    /// SHA256 hash (lowercase hex, 64 chars) of the canonical PNG content of the
    /// uploaded image. Used to detect duplicate screenshots within the same report
    /// before re-uploading. Null for legacy rows / non-image attachments.
    /// </summary>
    [MaxLength(64)]
    public string? ContentHashSha256 { get; set; }

    /// <summary>Size of the uploaded file in bytes (best-effort).</summary>
    public long? FileSizeBytes { get; set; }

    // Navigation
    public virtual InspectionNote Note { get; set; } = null!;
    public virtual Siuser? UploadedByUser { get; set; }
}
