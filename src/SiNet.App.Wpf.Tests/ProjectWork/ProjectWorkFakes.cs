using System.Runtime.CompilerServices;
using SiNet.Application.ProjectWork;
using SiNet.Domain.Files;

namespace SiNet.App.Wpf.Tests.ProjectWork;

/// <summary>In-memory <see cref="IFileStore"/> for coordinator / tree tests.</summary>
internal sealed class FakeFileStore : IFileStore
{
    private readonly Func<int, int, string?> _resolve;
    private readonly Func<string, IReadOnlyList<ScannedFile>> _list;

    public FakeFileStore(
        FileStorageDestination destination,
        Func<int, int, string?> resolve,
        Func<string, IReadOnlyList<ScannedFile>> list)
    {
        Destination = destination;
        _resolve = resolve;
        _list = list;
    }

    public FileStorageDestination Destination { get; }

    public Task<string?> ResolveFolderHandleAsync(int projectId, int projectFolderId, CancellationToken cancellationToken = default)
        => Task.FromResult(_resolve(projectId, projectFolderId));

    public async IAsyncEnumerable<ScannedFile> ListFilesAsync(
        string folderHandle,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var sf in _list(folderHandle))
        {
            await Task.Yield();
            yield return sf;
        }
    }

    public Task<string> DownloadToLocalAsync(ScannedFile file, CancellationToken cancellationToken = default)
        => Task.FromResult(file.NativeId);

    /// <summary>Records (folderHandle, localSourcePath, targetFileName) for each upload.</summary>
    public List<(string Handle, string Source, string TargetName)> Uploads { get; } = new();

    /// <summary>Records deleted files.</summary>
    public List<ScannedFile> Deletes { get; } = new();

    /// <summary>Records (file, newName) rename calls.</summary>
    public List<(ScannedFile File, string NewName)> Renames { get; } = new();

    public Task<ScannedFile> UploadAsync(string folderHandle, string localSourcePath, string targetFileName, CancellationToken cancellationToken = default)
    {
        Uploads.Add((folderHandle, localSourcePath, targetFileName));
        return Task.FromResult(new ScannedFile(
            Source: Destination,
            FileName: targetFileName,
            NativeId: Destination switch
            {
                FileStorageDestination.FileServer => System.IO.Path.Combine(folderHandle, targetFileName),
                FileStorageDestination.GoogleDrive => "drive-" + targetFileName,
                _ => "acc-item-" + targetFileName,
            },
            SizeBytes: 1024,
            LastModified: DateTime.Now,
            Parsed: ProjectFileNameParser.TryParse(targetFileName)));
    }

    public Task DeleteAsync(ScannedFile file, CancellationToken cancellationToken = default)
    {
        Deletes.Add(file);
        return Task.CompletedTask;
    }

    public Task<ScannedFile> RenameAsync(ScannedFile file, string newFileName, CancellationToken cancellationToken = default)
    {
        Renames.Add((file, newFileName));
        return Task.FromResult(file with { FileName = newFileName, Parsed = ProjectFileNameParser.TryParse(newFileName) });
    }

    public static ScannedFile FileServerFile(string fileName) => new(
        Source: FileStorageDestination.FileServer,
        FileName: fileName,
        NativeId: @"C:\\fake\\" + fileName,
        SizeBytes: 1024,
        LastModified: DateTime.Now,
        Parsed: ProjectFileNameParser.TryParse(fileName));
}

/// <summary>Returns a preset tree snapshot.</summary>
internal sealed class FakeProjectFileQueryService : IProjectFileQueryService
{
    private readonly ProjectFileTreeDto? _tree;

    public FakeProjectFileQueryService(ProjectFileTreeDto? tree) => _tree = tree;

    public Task<ProjectFileTreeDto?> GetProjectFileTreeAsync(int projectId, CancellationToken cancellationToken = default)
        => Task.FromResult(_tree);
}
