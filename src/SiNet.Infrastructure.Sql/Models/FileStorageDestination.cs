namespace SiNetSQL.Models;

/// <summary>
/// Indicates where files of a given ProjectFile type are physically stored.
/// Applied at the ProjectFile level to indicate the default storage location.
/// </summary>
public enum FileStorageDestination
{
    /// <summary>
    /// Files are stored on the network file server (legacy default).
    /// Path is derived from Project.ProjectPath + folder hierarchy.
    /// </summary>
    FileServer = 0,

    /// <summary>
    /// Files are stored in Autodesk Construction Cloud (ACC/BIM 360).
    /// Synced via AccFileSyncService.
    /// </summary>
    Acc = 1,

    /// <summary>
    /// Files are stored in Google Drive.
    /// </summary>
    GoogleDrive = 2
}
