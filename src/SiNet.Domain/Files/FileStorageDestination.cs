namespace SiNet.Domain.Files;

/// <summary>
/// Where a project file is physically stored.
/// <para>
/// Canonical clean-layer definition for the ProjectWork file model. The legacy persistence enum
/// (<c>SiNetSQL.Models.FileStorageDestination</c>) currently remains the EF-mapped type; the numeric
/// values here are kept identical so the infrastructure boundary can map between the two with a
/// simple cast. The legacy type is scheduled to be collapsed onto this one during the full
/// ProjectWork migration.
/// </para>
/// </summary>
public enum FileStorageDestination
{
    /// <summary>Files stored on the network file server (legacy default).</summary>
    FileServer = 0,

    /// <summary>Files stored in Autodesk Construction Cloud (ACC / BIM 360).</summary>
    Acc = 1,

    /// <summary>Files stored in Google Drive.</summary>
    GoogleDrive = 2,
}
