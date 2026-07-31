using System.IO;
using Moq;
using SiNet.Application.ProjectWork;
using SiNet.Domain.Files;
using SiNet.Infrastructure.FileSystem.ProjectWork;
using Xunit;

namespace SiNet.App.Wpf.Tests.ProjectWork;

public sealed class FileServerFileStoreTests : IDisposable
{
    private readonly string _dir;

    public FileServerFileStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "sinet_fsstore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public void Destination_is_file_server()
    {
        var store = new FileServerFileStore(Mock.Of<IProjectFolderPathResolver>());
        Assert.Equal(FileStorageDestination.FileServer, store.Destination);
    }

    [Fact]
    public async Task ResolveFolderHandleAsync_delegates_to_resolver()
    {
        var resolver = new Mock<IProjectFolderPathResolver>();
        resolver.Setup(r => r.ResolveFileServerFolderPathAsync(42, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_dir);
        var store = new FileServerFileStore(resolver.Object);

        Assert.Equal(_dir, await store.ResolveFolderHandleAsync(42, 100));
    }

    [Fact]
    public async Task ListFilesAsync_yields_data_files_and_skips_metadata_companions()
    {
        File.WriteAllText(Path.Combine(_dir, "(5)-3-7-1-1-Plan.dwg"), "d");
        File.WriteAllText(Path.Combine(_dir, "data.txt"), "d");
        File.WriteAllText(Path.Combine(_dir, "data.txt.json"), "{}");        // companion → skipped
        File.WriteAllText(Path.Combine(_dir, "notes.si.json"), "{}");         // sidecar → skipped
        File.WriteAllText(Path.Combine(_dir, "~$data.txt"), "lock");          // Office owner → skipped

        var store = new FileServerFileStore(Mock.Of<IProjectFolderPathResolver>());

        var results = new List<ScannedFile>();
        await foreach (var sf in store.ListFilesAsync(_dir))
            results.Add(sf);

        var names = results.Select(r => r.FileName).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "(5)-3-7-1-1-Plan.dwg", "data.txt" }, names);

        var plan = results.Single(r => r.FileName.StartsWith("(5)", StringComparison.Ordinal));
        Assert.NotNull(plan.Parsed);
        Assert.Equal(7, plan.Parsed!.Number);
        Assert.Equal(FileStorageDestination.FileServer, plan.Source);
    }

    [Fact]
    public async Task ListFilesAsync_empty_for_missing_folder()
    {
        var store = new FileServerFileStore(Mock.Of<IProjectFolderPathResolver>());
        var any = false;
        await foreach (var _ in store.ListFilesAsync(Path.Combine(_dir, "does-not-exist")))
            any = true;
        Assert.False(any);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
