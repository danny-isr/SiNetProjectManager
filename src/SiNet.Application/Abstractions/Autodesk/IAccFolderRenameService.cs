namespace SiNet.Application.Abstractions.Autodesk;

/// <summary>Outcome of renaming an ACC Docs folder (DEV-008 Layer A).</summary>
public enum AccFolderRenameStatus
{
    Succeeded = 0,
    Skipped = 1,
    Failed = 2,
}

/// <summary>Result of <see cref="IAccFolderRenameService.RenameFolderAsync"/>.</summary>
public sealed record AccFolderRenameOutcome(AccFolderRenameStatus Status, string Message);

/// <summary>
/// Renames an ACC Docs folder by id. The folder URN stays stable; only the display name changes.
/// Required for verified project rename (FileServer / ACC / Drive / DB).
/// </summary>
public interface IAccFolderRenameService
{
    Task<AccFolderRenameOutcome> RenameFolderAsync(
        string accProjectId,
        string folderId,
        string newFolderName,
        CancellationToken cancellationToken = default);
}
