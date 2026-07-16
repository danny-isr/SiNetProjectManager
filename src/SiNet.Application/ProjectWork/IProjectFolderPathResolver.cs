namespace SiNet.Application.ProjectWork;

/// <summary>
/// Resolves the absolute FileServer path of a DB project folder within a specific project, by combining
/// the project's file-server root with the folder-title segments walked up the folder hierarchy. Lets
/// the FileServer <see cref="IFileStore"/> stay free of <c>DbContext</c> — the EF-backed implementation
/// lives in <c>SiNet.Infrastructure.Sql</c>. Clean-layer port of the folder-resolution half of the
/// legacy <c>SiNetSQL.FileIndex.Stores.FileServerStore.ResolveFolderHandleAsync</c>.
/// </summary>
public interface IProjectFolderPathResolver
{
    /// <summary>
    /// Resolves the absolute path for <paramref name="projectFolderId"/> within
    /// <paramref name="projectId"/>, or <see langword="null"/> when the folder/project cannot be
    /// resolved (unknown folder, unmapped project, or a synthetic negative folder id).
    /// </summary>
    Task<string?> ResolveFileServerFolderPathAsync(int projectId, int projectFolderId, CancellationToken cancellationToken = default);
}
