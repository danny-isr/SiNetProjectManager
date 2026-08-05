using SiNet.Application.Projects;
using SiNet.Application.ProjectWork;

namespace SiNet.Infrastructure.Google.ProjectWork;

/// <summary>Drive project-root rename under <see cref="GmailOptions.ProjectsRootFolderId"/>.</summary>
public sealed class GoogleDriveProjectRootRenameService(
    IGoogleDriveFileService drive,
    GmailOptions options) : IProjectDriveRootRenameService
{
    private readonly IGoogleDriveFileService _drive = drive ?? throw new ArgumentNullException(nameof(drive));
    private readonly GmailOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    public async Task<ProjectDriveRootRenameOutcome> RenameRootAsync(
        string oldFolderName,
        string newFolderName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldFolderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(newFolderName);

        if (!_options.IsDriveConfigured || string.IsNullOrWhiteSpace(_options.ProjectsRootFolderId))
        {
            return new ProjectDriveRootRenameOutcome(
                ProjectDriveRootRenameStatus.Skipped,
                "Google Drive לא מוגדר — דולג.");
        }

        if (string.Equals(oldFolderName, newFolderName, StringComparison.Ordinal))
        {
            return new ProjectDriveRootRenameOutcome(
                ProjectDriveRootRenameStatus.Skipped,
                "שם שורש Drive ללא שינוי — דולג.");
        }

        var rootId = _options.ProjectsRootFolderId!;
        var folderId = await _drive
            .FindFolderIdByNameAsync(oldFolderName, rootId, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(folderId))
        {
            return new ProjectDriveRootRenameOutcome(
                ProjectDriveRootRenameStatus.Skipped,
                $"לא נמצאה תיקיית Drive '{oldFolderName}' תחת ProjectsRoot — דולג.");
        }

        var conflict = await _drive
            .FindFolderIdByNameAsync(newFolderName, rootId, cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(conflict))
        {
            return new ProjectDriveRootRenameOutcome(
                ProjectDriveRootRenameStatus.Failed,
                $"תיקיית יעד כבר קיימת ב-Drive: '{newFolderName}'.");
        }

        await _drive.RenameFileAsync(folderId, newFolderName, cancellationToken).ConfigureAwait(false);
        return new ProjectDriveRootRenameOutcome(
            ProjectDriveRootRenameStatus.Succeeded,
            $"Drive: '{oldFolderName}' → '{newFolderName}'");
    }
}
