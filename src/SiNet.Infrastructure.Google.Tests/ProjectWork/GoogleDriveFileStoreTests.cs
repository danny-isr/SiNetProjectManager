using SiNet.Application.Abstractions.Logging;
using SiNet.Application.ProjectWork;
using SiNet.Domain.Files;
using SiNet.Infrastructure.Google;
using SiNet.Infrastructure.Google.ProjectWork;
using Xunit;

namespace SiNet.Infrastructure.Google.Tests.ProjectWork;

/// <summary>
/// Offline tests for the real ProjectWork <see cref="GoogleDriveFileStore"/> over a fake
/// <see cref="IGoogleDriveFileService"/>. No live Google API calls.
/// </summary>
public sealed class GoogleDriveFileStoreTests
{
    private static GmailOptions ConfiguredOptions() => new()
    {
        SharedDriveId = "shared-drive-1",
        ProjectsRootFolderId = "projects-root-1",
    };

    private static GoogleDriveFileStore CreateStore(
        FakeGoogleDriveFileService drive,
        FakeDriveFolderResolver? folders = null,
        GmailOptions? options = null)
        => new(drive, folders ?? new FakeDriveFolderResolver(["ProjA", "Drawings"]), options ?? ConfiguredOptions(), new NullLogger());

    [Fact]
    public void Destination_is_google_drive()
    {
        Assert.Equal(FileStorageDestination.GoogleDrive, CreateStore(new FakeGoogleDriveFileService()).Destination);
    }

    [Fact]
    public async Task ResolveFolderHandle_returns_null_when_not_configured()
    {
        var store = CreateStore(new FakeGoogleDriveFileService(), options: new GmailOptions());
        var handle = await store.ResolveFolderHandleAsync(1, 10);
        Assert.Null(handle);
    }

    [Fact]
    public async Task ResolveFolderHandle_ensures_path_under_projects_root()
    {
        var drive = new FakeGoogleDriveFileService();
        var store = CreateStore(drive);

        var handle = await store.ResolveFolderHandleAsync(5, 10);

        Assert.Equal("folder-ProjA/Drawings", handle);
        Assert.Equal(["ProjA", "Drawings"], drive.LastEnsuredSegments);
        Assert.Equal("projects-root-1", drive.LastEnsureRoot);
    }

    [Fact]
    public async Task ListFiles_filters_sidecars_and_skips_duplicates()
    {
        var drive = new FakeGoogleDriveFileService();
        drive.Seed("folder-1",
            new GoogleDriveFileEntry("a", "plan.dwg", 10, DateTime.UtcNow),
            new GoogleDriveFileEntry("b", "plan.dwg", 11, DateTime.UtcNow),
            new GoogleDriveFileEntry("c", "plan.dwg.si.json", 2, DateTime.UtcNow),
            new GoogleDriveFileEntry("d", "other.dwg", 20, DateTime.UtcNow));
        var store = CreateStore(drive);

        var names = new List<string>();
        await foreach (var f in store.ListFilesAsync("folder-1"))
            names.Add(f.FileName);

        Assert.Equal(["other.dwg"], names);
    }

    [Fact]
    public async Task Upload_writes_data_and_sidecar_with_target_name()
    {
        var drive = new FakeGoogleDriveFileService();
        var store = CreateStore(drive);
        var source = Path.Combine(Path.GetTempPath(), $"sinet-drive-src-{Guid.NewGuid():N}.dwg");
        await File.WriteAllTextAsync(source, "content");

        try
        {
            var uploaded = await store.UploadAsync("folder-1", source, "(5)-3-7-1-1-Plan.dwg");

            Assert.Equal("(5)-3-7-1-1-Plan.dwg", uploaded.FileName);
            Assert.Equal(FileStorageDestination.GoogleDrive, uploaded.Source);
            Assert.Contains("(5)-3-7-1-1-Plan.dwg", drive.UploadedNames);
            Assert.Contains("(5)-3-7-1-1-Plan.dwg.si.json", drive.UploadedNames);
        }
        finally
        {
            File.Delete(source);
        }
    }

    [Fact]
    public async Task Upload_refuses_duplicate_name()
    {
        var drive = new FakeGoogleDriveFileService();
        drive.Seed("folder-1", new GoogleDriveFileEntry("x", "plan.dwg", 1, null));
        var store = CreateStore(drive);
        var source = Path.Combine(Path.GetTempPath(), $"sinet-drive-src-{Guid.NewGuid():N}.dwg");
        await File.WriteAllTextAsync(source, "content");

        try
        {
            await Assert.ThrowsAsync<FileStoreConflictException>(
                () => store.UploadAsync("folder-1", source, "plan.dwg"));
        }
        finally
        {
            File.Delete(source);
        }
    }

    [Fact]
    public async Task Delete_trashes_file_and_sidecar()
    {
        var drive = new FakeGoogleDriveFileService();
        drive.Seed("folder-1",
            new GoogleDriveFileEntry("file-1", "plan.dwg", 1, null),
            new GoogleDriveFileEntry("side-1", "plan.dwg.si.json", 1, null));
        drive.Parents["file-1"] = ["folder-1"];
        var store = CreateStore(drive);

        await store.DeleteAsync(new ScannedFile(
            FileStorageDestination.GoogleDrive, "plan.dwg", "file-1", 1, null, null));

        Assert.Contains("file-1", drive.TrashedIds);
        Assert.Contains("side-1", drive.TrashedIds);
    }

    [Fact]
    public async Task Rename_renames_file_and_sidecar()
    {
        var drive = new FakeGoogleDriveFileService();
        drive.Seed("folder-1",
            new GoogleDriveFileEntry("file-1", "old.dwg", 1, null),
            new GoogleDriveFileEntry("side-1", "old.dwg.si.json", 1, null));
        drive.Parents["file-1"] = ["folder-1"];
        var store = CreateStore(drive);

        var renamed = await store.RenameAsync(
            new ScannedFile(FileStorageDestination.GoogleDrive, "old.dwg", "file-1", 1, null, null),
            "new.dwg");

        Assert.Equal("new.dwg", renamed.FileName);
        Assert.Equal("new.dwg", drive.Names["file-1"]);
        Assert.Equal("new.dwg.si.json", drive.Names["side-1"]);
    }

    [Fact]
    public async Task DownloadToLocal_writes_temp_file()
    {
        var drive = new FakeGoogleDriveFileService();
        drive.DownloadPayloads["file-1"] = "hello-drive"u8.ToArray();
        var store = CreateStore(drive);

        var path = await store.DownloadToLocalAsync(new ScannedFile(
            FileStorageDestination.GoogleDrive, "plan.dwg", "file-1", 0, null, null));

        try
        {
            Assert.True(File.Exists(path));
            Assert.Equal("hello-drive", await File.ReadAllTextAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RequestedScopes_include_drive()
    {
        Assert.Contains(global::Google.Apis.Drive.v3.DriveService.Scope.Drive, GmailClientProvider.RequestedScopes);
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }

    private sealed class FakeDriveFolderResolver(IReadOnlyList<string>? segments) : IProjectDriveFolderResolver
    {
        public Task<IReadOnlyList<string>?> ResolveRelativeSegmentsAsync(
            int projectId, int projectFolderId, CancellationToken cancellationToken = default)
            => Task.FromResult(segments);
    }

    private sealed class FakeGoogleDriveFileService : IGoogleDriveFileService
    {
        private readonly Dictionary<string, List<GoogleDriveFileEntry>> _byParent = new(StringComparer.Ordinal);
        public List<string> UploadedNames { get; } = [];
        public List<string> TrashedIds { get; } = [];
        public Dictionary<string, string> Names { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, IReadOnlyList<string>> Parents { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, byte[]> DownloadPayloads { get; } = new(StringComparer.Ordinal);
        public IReadOnlyList<string>? LastEnsuredSegments { get; private set; }
        public string? LastEnsureRoot { get; private set; }

        public void Seed(string parentId, params GoogleDriveFileEntry[] files)
        {
            _byParent[parentId] = files.ToList();
            foreach (var f in files)
                Names[f.Id] = f.Name;
        }

        public Task<string> EnsureFolderPathAsync(IReadOnlyList<string> pathSegments, string rootFolderId, CancellationToken cancellationToken = default)
        {
            LastEnsuredSegments = pathSegments.ToList();
            LastEnsureRoot = rootFolderId;
            return Task.FromResult("folder-" + string.Join('/', pathSegments));
        }

        public Task<IReadOnlyList<GoogleDriveFileEntry>> ListFilesAsync(string parentId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<GoogleDriveFileEntry>>(_byParent.TryGetValue(parentId, out var list) ? list : []);

        public Task<IReadOnlyList<GoogleDriveFileEntry>> FindFilesByNameAsync(string fileName, string parentId, CancellationToken cancellationToken = default)
        {
            var list = _byParent.TryGetValue(parentId, out var files)
                ? files.Where(f => string.Equals(f.Name, fileName, StringComparison.Ordinal)).ToList()
                : [];
            return Task.FromResult<IReadOnlyList<GoogleDriveFileEntry>>(list);
        }

        public Task<GoogleDriveFileEntry> UploadFileAsync(string parentId, string localFilePath, string targetName, CancellationToken cancellationToken = default)
        {
            UploadedNames.Add(targetName);
            var entry = new GoogleDriveFileEntry("up-" + UploadedNames.Count, targetName, new FileInfo(localFilePath).Length, DateTime.UtcNow);
            if (!_byParent.TryGetValue(parentId, out var list))
            {
                list = [];
                _byParent[parentId] = list;
            }
            list.Add(entry);
            Names[entry.Id] = entry.Name;
            return Task.FromResult(entry);
        }

        public Task<GoogleDriveFileEntry> UploadStringAsync(string parentId, string content, string targetName, string mimeType = "application/json", CancellationToken cancellationToken = default)
        {
            UploadedNames.Add(targetName);
            var entry = new GoogleDriveFileEntry("str-" + UploadedNames.Count, targetName, content.Length, DateTime.UtcNow);
            if (!_byParent.TryGetValue(parentId, out var list))
            {
                list = [];
                _byParent[parentId] = list;
            }
            list.Add(entry);
            Names[entry.Id] = entry.Name;
            return Task.FromResult(entry);
        }

        public async Task DownloadFileAsync(string fileId, Stream destination, CancellationToken cancellationToken = default)
        {
            var bytes = DownloadPayloads.TryGetValue(fileId, out var payload) ? payload : Array.Empty<byte>();
            await destination.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }

        public Task TrashFileAsync(string fileId, CancellationToken cancellationToken = default)
        {
            TrashedIds.Add(fileId);
            foreach (var list in _byParent.Values)
                list.RemoveAll(f => f.Id == fileId);
            return Task.CompletedTask;
        }

        public Task<GoogleDriveFileEntry> RenameFileAsync(string fileId, string newName, CancellationToken cancellationToken = default)
        {
            Names[fileId] = newName;
            foreach (var list in _byParent.Values)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    if (list[i].Id == fileId)
                        list[i] = list[i] with { Name = newName };
                }
            }
            return Task.FromResult(new GoogleDriveFileEntry(fileId, newName, 0, DateTime.UtcNow));
        }

        public Task<IReadOnlyList<string>> GetParentIdsAsync(string fileId, CancellationToken cancellationToken = default)
            => Task.FromResult(Parents.TryGetValue(fileId, out var p) ? p : Array.Empty<string>());
    }
}
