namespace SiNet.Application.ProjectWork;

/// <summary>
/// Creates user-managed project folders in the DB catalog (and on disk when a FileServer path resolves).
/// Does not delete or rename catalog folders.
/// <para>
/// DEV-012: the ProjectWork tree context menu creates <b>disk-only</b> user folders and does not call
/// this service. Keep the registration for catalog/admin callers that still need DB rows.
/// </para>
/// </summary>
public interface IProjectFolderWriteService
{
    /// <summary>
    /// Creates a child folder under <paramref name="parentFolderId"/> with the given title.
    /// </summary>
    /// <returns>New folder id, or failure message.</returns>
    Task<CreateProjectFolderResult> CreateChildFolderAsync(
        int parentFolderId,
        string folderTitle,
        int? projectId = null,
        CancellationToken cancellationToken = default);
}

public sealed record CreateProjectFolderResult(bool Success, int? FolderId, string? ErrorMessage)
{
    public static CreateProjectFolderResult Ok(int folderId) => new(true, folderId, null);

    public static CreateProjectFolderResult Fail(string message) => new(false, null, message);
}
