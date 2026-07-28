namespace SiNetSQL.Services.AccBootstrap;

/// <summary>
/// Contains the resolved ACC identifiers for a project after provisioning.
/// Returned by IAccProjectProvisioningService.EnsureProjectMappingAsync.
/// </summary>
public class ProjectAccTargets
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
    /// ACC Project ID string for the SI-{Place} project.
    /// </summary>
    public string AccProjectId { get; init; } = string.Empty;

    /// <summary>
    /// ACC project display name (e.g., "SI-אשקלון").
    /// </summary>
    public string AccProjectName { get; init; } = string.Empty;

    /// <summary>
    /// Root folder ID of the ACC project ("Project Files" folder).
    /// </summary>
    public string AccRootFolderId { get; init; } = string.Empty;

    /// <summary>
    /// Folder ID of the project-specific folder inside ACC
    /// (e.g., "(2621)שביל_אופניים_ברחוב_בר_לב").
    /// This is where file tagging destinations are built from.
    /// </summary>
    public string AccTargetFolderId { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable path to the target folder.
    /// </summary>
    public string AccTargetFolderPath { get; init; } = string.Empty;
}
