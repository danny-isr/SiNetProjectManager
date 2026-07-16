using SiNet.Application.ProjectWork;
using SiNet.Domain.Files;
using Xunit;

namespace SiNet.App.Wpf.Tests.ProjectWork;

public sealed class FileIndexServiceTests
{
    [Fact]
    public void GetStore_and_AvailableDestinations_reflect_registered_stores()
    {
        var fs = new FakeFileStore(FileStorageDestination.FileServer, (_, _) => "h", _ => Array.Empty<ScannedFile>());
        var acc = new FakeFileStore(FileStorageDestination.Acc, (_, _) => null, _ => Array.Empty<ScannedFile>());
        var sut = new FileIndexService(new IFileStore[] { fs, acc });

        Assert.Same(fs, sut.GetStore(FileStorageDestination.FileServer));
        Assert.Same(acc, sut.GetStore(FileStorageDestination.Acc));
        Assert.Null(sut.GetStore(FileStorageDestination.GoogleDrive));
        Assert.Contains(FileStorageDestination.FileServer, sut.AvailableDestinations);
        Assert.Contains(FileStorageDestination.Acc, sut.AvailableDestinations);
    }

    [Fact]
    public void InFlight_markers_round_trip_and_raise_events()
    {
        var sut = new FileIndexService(Array.Empty<IFileStore>());
        var changes = new List<InFlightChange>();
        sut.InFlightChanged += changes.Add;

        Assert.False(sut.IsInFlight(1, "a.dwg", FileStorageDestination.Acc));
        sut.MarkInFlight(1, "a.dwg", FileStorageDestination.Acc);
        Assert.True(sut.IsInFlight(1, "a.dwg", FileStorageDestination.Acc));
        sut.ClearInFlight(1, "a.dwg", FileStorageDestination.Acc);
        Assert.False(sut.IsInFlight(1, "a.dwg", FileStorageDestination.Acc));

        Assert.Equal(2, changes.Count);
        Assert.True(changes[0].IsStarting);
        Assert.False(changes[1].IsStarting);
    }

    [Fact]
    public async Task ScanFolderAsync_streams_files_only_from_wanted_destinations()
    {
        var fs = new FakeFileStore(
            FileStorageDestination.FileServer,
            (_, folderId) => folderId == 100 ? "handle-100" : null,
            handle => handle == "handle-100"
                ? new[] { FakeFileStore.FileServerFile("(5)-3-7-1-1-Plan.dwg"), FakeFileStore.FileServerFile("readme.txt") }
                : Array.Empty<ScannedFile>());
        var acc = new FakeFileStore(FileStorageDestination.Acc, (_, _) => "acc-handle",
            _ => throw new InvalidOperationException("ACC should not be scanned when not requested"));

        var sut = new FileIndexService(new IFileStore[] { fs, acc });

        var results = new List<ScannedFile>();
        await foreach (var sf in sut.ScanFolderAsync(5, 100, new[] { FileStorageDestination.FileServer }))
            results.Add(sf);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(FileStorageDestination.FileServer, r.Source));
    }
}
