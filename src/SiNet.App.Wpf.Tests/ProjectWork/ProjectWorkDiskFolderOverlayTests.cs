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
        // DEV-013: nested folder is probed for color; files load only after expand.
        Assert.True(manual.HasPhysicalFiles);
        Assert.False(manual.CanDeleteFolder);

        await vm.ExpandAndWaitAsync(manual);
        Assert.Equal(ProjectFolderLoadState.Expanded, manual.LoadState);
        Assert.Contains(manual.Children.OfType<ProjectFileNodeVm>(), f => f.IsUnfiled);
    }

    [Fact]
    public async Task Rescan_removes_user_folder_deleted_outside_app()
    {
        var catalogPath = Path.Combine(_root, "Drawings");
        Directory.CreateDirectory(catalogPath);
        var manualPath = Path.Combine(catalogPath, "ManualNotes");
        Directory.CreateDirectory(manualPath);

        var vm = CreateVm(catalogFolderId: 10, catalogPath);
        await vm.LoadProjectAsync(5);

        var catalog = Assert.IsType<ProjectFolderNodeVm>(Assert.Single(vm.RootFolders));
        Assert.Contains(catalog.Children.OfType<ProjectFolderNodeVm>(), f => f.Title == "ManualNotes");

        Directory.Delete(manualPath, recursive: true);
        await vm.RescanAsync();

        Assert.DoesNotContain(catalog.Children.OfType<ProjectFolderNodeVm>(), f => f.Title == "ManualNotes");
    }

    [Fact]
    public async Task Rescan_adds_user_folder_created_outside_app()
    {
        var catalogPath = Path.Combine(_root, "Drawings");
        Directory.CreateDirectory(catalogPath);

        var vm = CreateVm(catalogFolderId: 10, catalogPath);
        await vm.LoadProjectAsync(5);

        var catalog = Assert.IsType<ProjectFolderNodeVm>(Assert.Single(vm.RootFolders));
        Assert.DoesNotContain(catalog.Children.OfType<ProjectFolderNodeVm>(), f => f.Title == "NewScratch");

        Directory.CreateDirectory(Path.Combine(catalogPath, "NewScratch"));
        await vm.RescanAsync();

        Assert.Contains(catalog.Children.OfType<ProjectFolderNodeVm>(), f => f.Title == "NewScratch" && f.IsUserCreated);
    }

    [Fact]
    public async Task Rescan_adds_and_removes_unfiled_file_in_expanded_folder()
    {
        var catalogPath = Path.Combine(_root, "Drawings");
        Directory.CreateDirectory(catalogPath);
        var loose = Path.Combine(catalogPath, "readme.txt");

        var scanned = new List<ScannedFile>();
        var store = new FakeFileStore(
            FileStorageDestination.FileServer,
            (_, fid) => fid == 10 ? catalogPath : null,
            h => string.Equals(h, catalogPath, StringComparison.OrdinalIgnoreCase)
                ? scanned.ToArray()
                : Array.Empty<ScannedFile>());

        var vm = CreateVm(10, catalogPath, store);
        await vm.LoadProjectAsync(5);

        var catalog = Assert.IsType<ProjectFolderNodeVm>(Assert.Single(vm.RootFolders));
        Assert.DoesNotContain(catalog.Children.OfType<ProjectFileNodeVm>(), f => f.IsUnfiled);

        await File.WriteAllTextAsync(loose, "x");
        scanned.Add(new ScannedFile(
            FileStorageDestination.FileServer,
            "readme.txt",
            loose,
            1,
            DateTime.UtcNow,
            Parsed: null));
        await vm.RescanAsync();

        Assert.Contains(catalog.Children.OfType<ProjectFileNodeVm>(), f => f.IsUnfiled);

        File.Delete(loose);
        scanned.Clear();
        await vm.RescanAsync();

        Assert.DoesNotContain(catalog.Children.OfType<ProjectFileNodeVm>(), f => f.IsUnfiled);
    }

    [Fact]
    public async Task Merge_preserves_file_and_alternative_IsExpanded_and_adds_new_file()
    {
        var catalogPath = Path.Combine(_root, "Drawings");
        Directory.CreateDirectory(catalogPath);
        var first = Path.Combine(catalogPath, "a.txt");
        await File.WriteAllTextAsync(first, "1");

        var scanned = new List<ScannedFile>
        {
            new(
                FileStorageDestination.FileServer,
                "a.txt",
                first,
                1,
                DateTime.UtcNow,
                Parsed: null),
        };
        var store = new FakeFileStore(
            FileStorageDestination.FileServer,
            (_, fid) => fid == 10 ? catalogPath : null,
            h => string.Equals(h, catalogPath, StringComparison.OrdinalIgnoreCase)
                ? scanned.ToArray()
                : Array.Empty<ScannedFile>());

        var vm = CreateVm(10, catalogPath, store);
        await vm.LoadProjectAsync(5);

        var catalog = Assert.IsType<ProjectFolderNodeVm>(Assert.Single(vm.RootFolders));
        var unfiled = catalog.Children.OfType<ProjectFileNodeVm>().Single(f => f.IsUnfiled);
        var alt = Assert.Single(unfiled.Children.OfType<AlternativeNodeVm>());
        unfiled.IsExpanded = true;
        alt.IsExpanded = true;

        var second = Path.Combine(catalogPath, "b.txt");
        await File.WriteAllTextAsync(second, "2");
        scanned.Add(new ScannedFile(
            FileStorageDestination.FileServer,
            "b.txt",
            second,
            1,
            DateTime.UtcNow,
            Parsed: null));

        await vm.MergeExpandedFolderFilesAsync(catalog);

        Assert.Same(unfiled, catalog.Children.OfType<ProjectFileNodeVm>().Single(f => f.IsUnfiled));
        Assert.True(unfiled.IsExpanded);
        Assert.True(alt.IsExpanded);
        Assert.Contains(unfiled.Children.OfType<AlternativeNodeVm>(), a => a.AlternativeName == "b.txt");
        Assert.Equal(2, unfiled.Children.OfType<AlternativeNodeVm>().Count());
    }

    [Fact]
    public async Task Merge_removes_missing_file_server_version_without_replacing_siblings()
    {
        var catalogPath = Path.Combine(_root, "Drawings");
        Directory.CreateDirectory(catalogPath);
        var keepPath = Path.Combine(catalogPath, "keep.txt");
        var dropPath = Path.Combine(catalogPath, "drop.txt");
        await File.WriteAllTextAsync(keepPath, "k");
        await File.WriteAllTextAsync(dropPath, "d");

        var scanned = new List<ScannedFile>
        {
            new(FileStorageDestination.FileServer, "keep.txt", keepPath, 1, DateTime.UtcNow, null),
            new(FileStorageDestination.FileServer, "drop.txt", dropPath, 1, DateTime.UtcNow, null),
        };
        var store = new FakeFileStore(
            FileStorageDestination.FileServer,
            (_, fid) => fid == 10 ? catalogPath : null,
            h => string.Equals(h, catalogPath, StringComparison.OrdinalIgnoreCase)
                ? scanned.ToArray()
                : Array.Empty<ScannedFile>());

        var vm = CreateVm(10, catalogPath, store);
        await vm.LoadProjectAsync(5);

        var catalog = Assert.IsType<ProjectFolderNodeVm>(Assert.Single(vm.RootFolders));
        var unfiled = catalog.Children.OfType<ProjectFileNodeVm>().Single(f => f.IsUnfiled);
        unfiled.IsExpanded = true;
        Assert.Equal(2, unfiled.Children.OfType<AlternativeNodeVm>().Count());

        File.Delete(dropPath);
        scanned.RemoveAll(s => s.FileName == "drop.txt");
        await vm.MergeExpandedFolderFilesAsync(catalog);

        Assert.Same(unfiled, catalog.Children.OfType<ProjectFileNodeVm>().Single(f => f.IsUnfiled));
        Assert.True(unfiled.IsExpanded);
        Assert.Contains(unfiled.Children.OfType<AlternativeNodeVm>(), a => a.AlternativeName == "keep.txt");
        Assert.DoesNotContain(unfiled.Children.OfType<AlternativeNodeVm>(), a => a.AlternativeName == "drop.txt");
    }

    [Fact]
    public async Task Collapse_unloads_file_nodes_but_keeps_folder_and_probe()
    {
        var catalogPath = Path.Combine(_root, "Drawings");
        Directory.CreateDirectory(catalogPath);
        var loose = Path.Combine(catalogPath, "readme.txt");
        await File.WriteAllTextAsync(loose, "x");

        var store = new FakeFileStore(
            FileStorageDestination.FileServer,
            (_, fid) => fid == 10 ? catalogPath : null,
            h => h == catalogPath
                ?
                [
                    new ScannedFile(
                        FileStorageDestination.FileServer,
                        "readme.txt",
                        loose,
                        1,
                        DateTime.UtcNow,
                        Parsed: null),
                ]
                : Array.Empty<ScannedFile>());

        var vm = CreateVm(10, catalogPath, store);
        await vm.LoadProjectAsync(5);

        var catalog = Assert.IsType<ProjectFolderNodeVm>(Assert.Single(vm.RootFolders));
        Assert.Equal(ProjectFolderLoadState.Expanded, catalog.LoadState);
        Assert.Contains(catalog.Children.OfType<ProjectFileNodeVm>(), f => f.IsUnfiled);

        catalog.IsExpanded = false;

        Assert.Equal(ProjectFolderLoadState.Probed, catalog.LoadState);
        Assert.DoesNotContain(catalog.Children.OfType<ProjectFileNodeVm>(), f => f.IsUnfiled);
        Assert.Contains(catalog.Children.OfType<ProjectFileNodeVm>(), f => f.FileId == 100);
        Assert.True(catalog.HasPhysicalFiles);
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
