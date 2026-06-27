namespace SiNetSQL.Models;

/// <summary>
/// Stores system-level ACC identifiers that are not tied to a specific Project row.
/// Examples: "OfficeInbox" for the shared email ingestion project.
/// 
/// Uses a string Key as the primary key for semantic identification.
/// </summary>
public class AccSystemResource
{
    /// <summary>
    /// Semantic key identifying this system resource (e.g., "OfficeInbox").
    /// This is the primary key.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Foreign key to AccHub.Id.
    /// </summary>
    public int AccHubId { get; set; }

    /// <summary>
    /// ACC project ID string (e.g., "b.xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx").
    /// </summary>
    public string? AccProjectId { get; set; }

    /// <summary>
    /// ACC root folder ID for this resource.
    /// </summary>
    public string? AccRootFolderId { get; set; }

    /// <summary>
    /// ACC folder ID for the "_Inbox" folder inside the project.
    /// Used for email ingestion workflow.
    /// </summary>
    public string? AccInboxFolderId { get; set; }

    /// <summary>
    /// Optional notes or description.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// UTC timestamp when this record was created.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// UTC timestamp when this record was last updated.
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; }

    // Navigation property
    public virtual AccHub AccHub { get; set; } = null!;
}
