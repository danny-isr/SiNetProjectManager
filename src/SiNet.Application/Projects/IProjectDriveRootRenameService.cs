namespace SiNet.Application.Projects;

/// <summary>Renames the project root folder under Drive ProjectsRoot (optional host capability).</summary>
public interface IProjectDriveRootRenameService
{
    /// <summary>
    /// Renames <paramref name="oldFolderName"/> → <paramref name="newFolderName"/> under ProjectsRoot.
    /// Returns skipped when Drive is not configured or the source folder is missing.
    /// </summary>
    Task<ProjectDriveRootRenameOutcome> RenameRootAsync(
        string oldFolderName,
        string newFolderName,
        CancellationToken cancellationToken = default);
}

public enum ProjectDriveRootRenameStatus
{
    Succeeded,
    Skipped,
    Failed,
}

public sealed record ProjectDriveRootRenameOutcome(
    ProjectDriveRootRenameStatus Status,
    string Message);
