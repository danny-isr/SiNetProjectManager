namespace SiNetSQL.Services.AccBootstrap;

/// <summary>
/// Contains the resolved ACC identifiers for the Office Inbox system resource.
/// Returned by IAccBootstrapService.EnsureOfficeInboxAsync.
/// </summary>
public class OfficeInboxTargets
{
    /// <summary>
    /// Database primary key for the AccHub row.
    /// </summary>
    public int AccHubDbId { get; init; }

    /// <summary>
    /// Autodesk Hub ID string (e.g., "b.xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx").
    /// </summary>
    public string HubId { get; init; } = string.Empty;

    /// <summary>
    /// ACC Project ID string where the Office Inbox resides.
    /// </summary>
    public string AccProjectId { get; init; } = string.Empty;

    /// <summary>
    /// Root folder ID of the ACC project ("Project Files" folder).
    /// </summary>
    public string AccRootFolderId { get; init; } = string.Empty;

    /// <summary>
    /// Folder ID of the "_Inbox" folder inside the project.
    /// This is where email attachments are uploaded.
    /// </summary>
    public string AccInboxFolderId { get; init; } = string.Empty;
}
