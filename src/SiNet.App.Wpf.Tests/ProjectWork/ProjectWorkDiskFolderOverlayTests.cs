using System.IO;
using Moq;
using SiNet.App.Wpf.Surfaces.ProjectWork;
using SiNet.Application.ProjectWork;
using SiNet.Domain.Files;
using Xunit;

namespace SiNet.App.Wpf.Tests.ProjectWork;

public sealed class ProjectWorkDiskFolderOverlayTests : IDisposable
{
    private readonly string _root;

    public ProjectWorkDiskFolderOverlayTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "SiNetPwDisk_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public async Task Disk_only_subdir_appears_as_user_folder_and_is_deletable_when_empty()
    {
        var catalogPath = Path.Combine(_root, "Drawings");
        Directory.CreateDirectory(catalogPath);
        var manualPath = Path.Combine(catalogPath, "ManualNotes");
        Directory.CreateDirectory(manualPath);

        var vm = CreateVm(catalogFolderId: 10, catalogPath);
        await vm.LoadProjectAsync(5);

        var catalog = Assert.IsType<ProjectFolderNodeVm>(Assert.Single(vm.RootFolders));
        Assert.False(catalog.IsUserCreated);
        Assert.False(catalog.CanDeleteFolder);

        var manual = catalog.Children.OfType<ProjectFolderNodeVm>()
            .Single(f => f.Title == "ManualNotes");
        Assert.True(manual.IsUserCreated);
        Assert.True(manual.IsEmpty);
        Assert.True(manual.CanDeleteFolder);
        Assert.False(manual.HasPhysicalFiles);
    }

    [Fact]
    public async Task Unmatched_file_in_catalog_folder_goes_to_unfiled_bucket()
    {
        var catalogPath = Path.Combine(_root, "Drawings");
        Directory.CreateDirectory(catalogPath);
        var loose = Path.Combine(catalogPath, "readme.txt");
        await File.WriteAllTextAsync(loose, "x");

        var store = new FakeFileStore(
            FileStorageDestination.FileServer,
            (_, fid) => fid == 10 ? catalogPath : null,
            h => h == catalogPath
                ? new[]
                {
                    new ScannedFile(
                        FileStorageDestination.FileServer,
                        "readme.txt",
                        loose,
                        1,
                        DateTime.UtcNow,
                        Parsed: null),
                }
                : Array.Empty<ScannedFile>());

        var vm = CreateVm(10, catalogPath, store);
        await vm.LoadProjectAsync(5);

        var catalog = Assert.IsType<ProjectFolderNodeVm>(Assert.Single(vm.RootFolders));
        var unfiled = catalog.Children.OfType<ProjectFileNodeVm>().Single(f => f.IsUnfiled);
        Assert.Equal("קובץ שאינו שייך לפרויקט", unfiled.Title);
        Assert.True(catalog.HasPhysicalFiles);
        Assert.False(catalog.CanDeleteFolder);
    }

    [Fact]
    public async Task File_inside_user_folder_marks_folder_non_empty_and_not_deletable()
    {
        var catalogPath = Path.Combine(_root, "Drawings");
        Directory.CreateDirectory(catalogPath);
        var manualPath = Path.Combine(catalogPath, "Scratch");
        Directory.CreateDirectory(manualPath);
        var loose = Path.Combine(manualPath, "note.txt");
        await File.WriteAllTextAsync(loose, "y");

        var store = new FakeFileStore(
            FileStorageDestination.FileServer,
            (_, fid) => fid == 10 ? catalogPath : null,
            h =>
            {
                if (string.Equals(h, manualPath, StringComparison.OrdinalIgnoreCase))
                {
                    return
                    [
                        new ScannedFile(
                            FileStorageDestination.FileServer,
                            "note.txt",
                            loose,
                            1,
                            DateTime.UtcNow,
                            Parsed: null),
                    ];
                }

                return Array.Empty<ScannedFile>();
            });

        var vm = CreateVm(10, catalogPath, store);
        await vm.LoadProjectAsync(5);

        var catalog = Assert.IsType<ProjectFolderNodeVm>(Assert.Single(vm.RootFolders));
        var manual = catalog.Children.OfType<ProjectFolderNodeVm>().Single(f => f.Title == "Scratch");
        Assert.True(manual.IsUserCreated);
        Assert.True(manual.HasPhysicalFiles);
        Assert.False(manual.CanDeleteFolder);
        Assert.Contains(manual.Children.OfType<ProjectFileNodeVm>(), f => f.IsUnfiled);
    }

    private ProjectWorkTreeViewModel CreateVm(
        int catalogFolderId,
        string catalogPath,
        FakeFileStore? store = null)
    {
        store ??= new FakeFileStore(
            FileStorageDestination.FileServer,
            (_, fid) => fid == catalogFolderId ? catalogPath : null,
            _ => Array.Empty<ScannedFile>());

        var tree = new ProjectFileTreeDto(
            ProjectId: 5,
            ProjectNumber: 5,
            RootFolders:
            [
                new ProjectFolderDto(
                    FolderId: catalogFolderId,
                    Name: "Drawings",
                    ParentFolderId: null,
                    Children: [],
                    Files:
                    [
                        new ProjectFileDefinitionDto(
                            100, "Plan", ".dwg", FileStorageDestination.FileServer,
                            catalogFolderId, 3, 7, null),
                    ]),
            ]);

        var pathResolver = new Mock<IProjectFolderPathResolver>();
        pathResolver
            .Setup(r => r.ResolveFileServerFolderPathAsync(
                It.IsAny<int>(),
                catalogFolderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogPath);

        return new ProjectWorkTreeViewModel(
            new FakeProjectFileQueryService(tree),
            new FileIndexService([store]),
            Mock.Of<IActiveFileQueryHub>(),
            Mock.Of<IFileOpenHub>(),
            folderPathResolver: pathResolver.Object);
    }
}
