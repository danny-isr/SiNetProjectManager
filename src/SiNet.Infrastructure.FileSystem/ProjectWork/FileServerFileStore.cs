using System.IO;
using System.Runtime.CompilerServices;
using SiNet.Application.ProjectWork;
using SiNet.Domain.Files;

namespace SiNet.Infrastructure.FileSystem.ProjectWork;

/// <summary>
/// <see cref="IFileStore"/> over the local / network file server. <c>NativeId</c> == absolute file
/// path. Clean-layer port of the legacy <c>SiNetSQL.FileIndex.Stores.FileServerStore</c>; folder-path
/// resolution is delegated to <see cref="IProjectFolderPathResolver"/> so this store stays free of
/// <c>DbContext</c>.
/// </summary>
public sealed class FileServerFileStore : IFileStore
{
    private readonly IProjectFolderPathResolver _folderPathResolver;

    public FileServerFileStore(IProjectFolderPathResolver folderPathResolver)
    {
        ArgumentNullException.ThrowIfNull(folderPathResolver);
        _folderPathResolver = folderPathResolver;
    }

    /// <inheritdoc />
    public FileStorageDestination Destination => FileStorageDestination.FileServer;

    /// <inheritdoc />
    public Task<string?> ResolveFolderHandleAsync(int projectId, int projectFolderId, CancellationToken cancellationToken = default)
        => _folderPathResolver.ResolveFileServerFolderPathAsync(projectId, projectFolderId, cancellationToken);

    /// <inheritdoc />
    public async IAsyncEnumerable<ScannedFile> ListFilesAsync(
        string folderHandle,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderHandle) || !Directory.Exists(folderHandle))
            yield break;

        foreach (var path in Directory.EnumerateFiles(folderHandle))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            FileInfo fi;
            try
            {
                fi = new FileInfo(path);
            }
            catch
            {
                continue;
            }

            // Sidecar / metadata-companion JSON files are metadata for their sibling data file — skip.
            if (FileServerSidecarMetadata.IsMetadataCompanion(fi.FullName))
                continue;

            var parsed = ProjectFileNameParser.TryParse(fi.Name);
            var sourceFileName = FileServerSidecarMetadata.TryReadSourceFileName(fi.FullName);

            yield return new ScannedFile(
                Source: FileStorageDestination.FileServer,
                FileName: fi.Name,
                NativeId: fi.FullName,
                SizeBytes: fi.Exists ? fi.Length : 0,
                LastModified: fi.Exists ? fi.LastWriteTime : null,
                Parsed: parsed,
                SourceFileName: sourceFileName);

            await Task.Yield();
        }
    }

    /// <inheritdoc />
    public Task<string> DownloadToLocalAsync(ScannedFile file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        // FileServer files are already local; the native id is the path.
        return Task.FromResult(file.NativeId);
    }

    /// <inheritdoc />
    public Task<ScannedFile> UploadAsync(
        string folderHandle,
        string localSourcePath,
        string targetFileName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderHandle))
            throw new ArgumentException("folderHandle is required.", nameof(folderHandle));
        if (!File.Exists(localSourcePath))
            throw new FileNotFoundException("Source file not found.", localSourcePath);

        var fileName = string.IsNullOrWhiteSpace(targetFileName)
            ? Path.GetFileName(localSourcePath)
            : targetFileName;
        var destPath = Path.Combine(folderHandle, fileName);

        if (!string.Equals(
                Path.GetFullPath(localSourcePath),
                Path.GetFullPath(destPath),
                StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(folderHandle);
            ClearReadOnly(destPath);
            File.Copy(localSourcePath, destPath, overwrite: true);
        }

        // Record the original dropped file name in the sidecar so the tree can surface provenance.
        FileServerSidecarMetadata.WriteSourceFileName(destPath, Path.GetFileName(localSourcePath));

        return Task.FromResult(Describe(new FileInfo(destPath)));
    }

    /// <inheritdoc />
    public Task DeleteAsync(ScannedFile file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        var path = file.NativeId;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            ClearReadOnly(path);
            File.Delete(path);
        }

        var sidecar = path + FileServerSidecarMetadata.SidecarSuffix;
        if (File.Exists(sidecar))
        {
            try { File.Delete(sidecar); }
            catch { /* sidecar cleanup is best-effort */ }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<ScannedFile> RenameAsync(ScannedFile file, string newFileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (string.IsNullOrWhiteSpace(newFileName))
            throw new ArgumentException("newFileName is required.", nameof(newFileName));

        var oldPath = file.NativeId;
        if (string.IsNullOrWhiteSpace(oldPath) || !File.Exists(oldPath))
            throw new FileNotFoundException("File to rename not found.", oldPath);

        var dir = Path.GetDirectoryName(oldPath)!;
        var newPath = Path.Combine(dir, newFileName);
        if (File.Exists(newPath))
            throw new IOException($"A file named '{newFileName}' already exists in the target folder.");

        ClearReadOnly(oldPath);
        File.Move(oldPath, newPath);

        var oldSidecar = oldPath + FileServerSidecarMetadata.SidecarSuffix;
        if (File.Exists(oldSidecar))
        {
            try { File.Move(oldSidecar, newPath + FileServerSidecarMetadata.SidecarSuffix, overwrite: true); }
            catch { /* sidecar move is best-effort */ }
        }

        return Task.FromResult(Describe(new FileInfo(newPath)));
    }

    private static ScannedFile Describe(FileInfo fi) => new(
        Source: FileStorageDestination.FileServer,
        FileName: fi.Name,
        NativeId: fi.FullName,
        SizeBytes: fi.Exists ? fi.Length : 0,
        LastModified: fi.Exists ? fi.LastWriteTime : null,
        Parsed: ProjectFileNameParser.TryParse(fi.Name),
        SourceFileName: FileServerSidecarMetadata.TryReadSourceFileName(fi.FullName));

    private static void ClearReadOnly(string path)
    {
        try
        {
            if (!File.Exists(path))
                return;
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
        }
        catch
        {
            // Non-fatal: the subsequent copy/move will surface any real access error.
        }
    }
}
