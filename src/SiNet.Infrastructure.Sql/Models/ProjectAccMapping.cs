namespace SiNetSQL.Models;

/// <summary>
/// Identifies which ACC platform a project belongs to.
/// </summary>
public enum AccPlatform
{
    /// <summary>Not yet determined.</summary>
    Unknown = 0,
    /// <summary>Legacy BIM 360 (created via HQ API).</summary>
    LegacyBim360 = 1,
    /// <summary>ACC-native project (created in ACC Admin Console).</summary>
    AccNative = 2
}

/// <summary>
/// Docs (Document Management) provisioning/readiness status.
/// </summary>
public enum DocsStatus
{
    /// <summary>Not yet checked.</summary>
    Unknown = 0,
    /// <summary>Docs is ready - "Project Files" folder exists and accessible.</summary>
    Ready = 1,
    /// <summary>Project exists but Docs not provisioned yet (common after project creation).</summary>
    NotProvisionedYet = 2,
    /// <summary>Docs disabled for this project or no access permissions.</summary>
    DocsDisabledOrNoAccess = 3,
    /// <summary>Error occurred while checking status.</summary>
    Error = 4
}

/// <summary>
/// Maps a dbo.Projects row to its corresponding ACC (Autodesk Construction Cloud) location.
/// Each project can have at most one ACC mapping (1:1 relationship).
/// 
/// This enables filing emails and documents to the correct ACC project/folder.
/// </summary>
public class ProjectAccMapping
{
    /// <summary>
    /// Primary key (auto-increment).
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to dbo.Projects.ID.
    /// </summary>
    public int ProjectId { get; set; }

    /// <summary>
    /// Foreign key to AccHub.Id.
    /// </summary>
    public int AccHubId { get; set; }

    /// <summary>
    /// ACC project ID string (e.g., "b.xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx").
    /// </summary>
    public string? AccProjectId { get; set; }

    /// <summary>
    /// ACC project display name (cached for UI display).
    /// </summary>
    public string? AccProjectName { get; set; }

    /// <summary>
    /// ACC folder ID where documents for this project should be uploaded.
    /// </summary>
    public string? AccTargetFolderId { get; set; }

    /// <summary>
    /// Human-readable path to the target folder (cached for UI display).
    /// Example: "/Project Files/Correspondence"
    /// </summary>
    public string? AccTargetFolderPath { get; set; }

    /// <summary>
    /// UTC timestamp when this mapping was last verified against ACC.
    /// Null if never verified.
    /// </summary>
    public DateTime? LastVerifiedUtc { get; set; }

    // === Platform and Docs Status fields ===

    /// <summary>
    /// Which ACC platform this project belongs to (LegacyBim360 or AccNative).
    /// </summary>
    public AccPlatform AccPlatform { get; set; } = AccPlatform.Unknown;

    /// <summary>
    /// Current Docs (Document Management) readiness status.
    /// </summary>
    public DocsStatus DocsStatus { get; set; } = DocsStatus.Unknown;

    /// <summary>
    /// UTC timestamp when DocsStatus was last checked via API.
    /// </summary>
    public DateTime? DocsLastCheckedUtc { get; set; }

    /// <summary>
    /// Error message from last DocsStatus check (null if no error).
    /// </summary>
    public string? DocsLastError { get; set; }

    // === END Platform and Docs Status fields ===

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

    // Navigation properties
    public virtual Project Project { get; set; } = null!;
    public virtual AccHub AccHub { get; set; } = null!;
}
