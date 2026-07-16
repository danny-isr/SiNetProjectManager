using Moq;
using SiNet.App.Wpf.Surfaces.ProjectWork;
using SiNet.Application.ProjectWork;
using SiNet.Domain.Files;
using Xunit;

namespace SiNet.App.Wpf.Tests.ProjectWork;

public sealed class ProjectWorkTreeViewModelWriteTests
{
    private static ProjectFileTreeDto Tree(FileStorageDestination dest, int folderId, int fileId) => new(
        ProjectId: 5,
        ProjectNumber: 5,
        RootFolders: new[]
        {
            new ProjectFolderDto(
                FolderId: folderId,
                Name: "Drawings",
                ParentFolderId: null,
                Children: Array.Empty<ProjectFolderDto>(),
                Files: new[]
                {
                    new ProjectFileDefinitionDto(fileId, "Plan", ".dwg", dest, folderId, 3, 7, null),
                }),
        });

    private static (ProjectWorkTreeViewModel Vm, FakeFileStore Store) CreateSut(
        FileStorageDestination dest,
        int folderId,
        int fileId,
        IAccWritePolicy? policy,
        params ScannedFile[] scanned)
    {
        var handle = dest switch
        {
            FileStorageDestination.Acc => "acc.proj|folder",
            FileStorageDestination.GoogleDrive => "drive-folder-" + folderId,
            _ => "h" + folderId,
        };
        var store = new FakeFileStore(
            dest,
            (_, fid) => fid == folderId ? handle : null,
            h => h == handle ? scanned : Array.Empty<ScannedFile>());
        var index = new FileIndexService(new IFileStore[] { store });
        var vm = new ProjectWorkTreeViewModel(
            new FakeProjectFileQueryService(Tree(dest, folderId, fileId)),
            index,
            Mock.Of<IActiveFileQueryHub>(),
            Mock.Of<IFileOpenHub>(),
            writePolicy: policy);
        return (vm, store);
    }

    private static ProjectFileNodeVm FileNode(ProjectWorkTreeViewModel vm, int fileId)
        => ((ProjectFolderNodeVm)vm.RootFolders[0]).Children.OfType<ProjectFileNodeVm>().Single(n => n.FileId == fileId);

    [Fact]
    public async Task AddVersionAsync_fileserver_uploads_canonical_name_and_succeeds()
    {
        var (vm, store) = CreateSut(FileStorageDestination.FileServer, folderId: 10, fileId: 100, policy: null);
        await vm.LoadProjectAsync(5);

        var outcome = await vm.AddVersionAsync(FileNode(vm, 100), "1", @"C:\src\drawing.dwg");

        Assert.Equal(FileWriteStatus.Success, outcome.Status);
        var upload = Assert.Single(store.Uploads);
        Assert.Equal("h10", upload.Handle);
        Assert.Equal(@"C:\src\drawing.dwg", upload.Source);
        Assert.Equal("(5)-3-7-1-1-Plan.dwg", upload.TargetName);
    }

    [Fact]
    public async Task AddVersionAsync_to_acc_is_blocked_when_gate_closed()
    {
        var (vm, store) = CreateSut(FileStorageDestination.Acc, folderId: 20, fileId: 200, policy: new StaticAccWritePolicy(false));
        await vm.LoadProjectAsync(5);

        var outcome = await vm.AddVersionAsync(FileNode(vm, 200), "1", @"C:\src\model.dwg");

        Assert.Equal(FileWriteStatus.Gated, outcome.Status);
        Assert.Empty(store.Uploads);
    }

    [Fact]
    public async Task AddVersionAsync_to_acc_uploads_when_gate_open()
    {
        var (vm, store) = CreateSut(FileStorageDestination.Acc, folderId: 20, fileId: 200, policy: new StaticAccWritePolicy(true));
        await vm.LoadProjectAsync(5);

        var outcome = await vm.AddVersionAsync(FileNode(vm, 200), "1", @"C:\src\model.dwg");

        Assert.Equal(FileWriteStatus.Success, outcome.Status);
        var upload = Assert.Single(store.Uploads);
        Assert.Equal("(5)-3-7-1-1-Plan.dwg", upload.TargetName);
    }

    [Fact]
    public async Task DeleteVersionAsync_fileserver_deletes_through_store()
    {
        var (vm, store) = CreateSut(
            FileStorageDestination.FileServer, folderId: 10, fileId: 100, policy: null,
            FakeFileStore.FileServerFile("(5)-3-7-1-1-Plan.dwg"));
        await vm.LoadProjectAsync(5);

        var version = FileNode(vm, 100).Children.OfType<AlternativeNodeVm>()
            .Single().Children.OfType<VersionNodeVm>().Single();

        var outcome = await vm.DeleteVersionAsync(version);

        Assert.Equal(FileWriteStatus.Success, outcome.Status);
        Assert.Single(store.Deletes);
    }

    [Fact]
    public async Task DeleteVersionAsync_acc_is_blocked_when_gate_closed()
    {
        var accFile = new ScannedFile(
            FileStorageDestination.Acc, "(5)-3-7-1-1-Plan.dwg", "acc-item-1", 0, DateTime.Now,
            ProjectFileNameParser.TryParse("(5)-3-7-1-1-Plan.dwg"), AccProjectId: "acc.proj", AccViewerUrl: "https://acc/x");
        var (vm, store) = CreateSut(FileStorageDestination.Acc, folderId: 20, fileId: 200, policy: new StaticAccWritePolicy(false), accFile);
        await vm.LoadProjectAsync(5);

        var version = FileNode(vm, 200).Children.OfType<AlternativeNodeVm>()
            .Single().Children.OfType<VersionNodeVm>().Single();

        var outcome = await vm.DeleteVersionAsync(version);

        Assert.Equal(FileWriteStatus.Gated, outcome.Status);
        Assert.Empty(store.Deletes);
    }

    [Fact]
    public async Task ReplaceVersionAsync_on_acc_is_not_supported()
    {
        var accFile = new ScannedFile(
            FileStorageDestination.Acc, "(5)-3-7-1-1-Plan.dwg", "acc-item-1", 0, DateTime.Now,
            ProjectFileNameParser.TryParse("(5)-3-7-1-1-Plan.dwg"), AccProjectId: "acc.proj", AccViewerUrl: "https://acc/x");
        var (vm, _) = CreateSut(FileStorageDestination.Acc, folderId: 20, fileId: 200, policy: new StaticAccWritePolicy(true), accFile);
        await vm.LoadProjectAsync(5);

        var version = FileNode(vm, 200).Children.OfType<AlternativeNodeVm>()
            .Single().Children.OfType<VersionNodeVm>().Single();

        var outcome = await vm.ReplaceVersionAsync(version, @"C:\src\new.dwg");
        Assert.Equal(FileWriteStatus.NotSupported, outcome.Status);
    }

    [Fact]
    public async Task RenameVersionAsync_fileserver_renames_through_store()
    {
        var (vm, store) = CreateSut(
            FileStorageDestination.FileServer, folderId: 10, fileId: 100, policy: null,
            FakeFileStore.FileServerFile("(5)-3-7-1-1-Plan.dwg"));
        await vm.LoadProjectAsync(5);

        var version = FileNode(vm, 100).Children.OfType<AlternativeNodeVm>()
            .Single().Children.OfType<VersionNodeVm>().Single();

        var outcome = await vm.RenameVersionAsync(version, "NewTitle");

        Assert.Equal(FileWriteStatus.Success, outcome.Status);
        var rename = Assert.Single(store.Renames);
        Assert.Equal("(5)-3-7-1-1-NewTitle.dwg", rename.NewName);
    }

    [Fact]
    public async Task HandleFileDropAsync_on_alternative_adds_version()
    {
        var (vm, store) = CreateSut(
            FileStorageDestination.FileServer, folderId: 10, fileId: 100, policy: null,
            FakeFileStore.FileServerFile("(5)-3-7-1-1-Plan.dwg"));
        await vm.LoadProjectAsync(5);

        var alt = FileNode(vm, 100).Children.OfType<AlternativeNodeVm>().Single();
        var outcome = await vm.HandleFileDropAsync(alt, @"C:\src\revised.dwg");

        Assert.Equal(FileWriteStatus.Success, outcome.Status);
        // Existing version was v1, so the drop places v2.
        Assert.Equal("(5)-3-7-1-2-Plan.dwg", Assert.Single(store.Uploads).TargetName);
    }

    [Fact]
    public async Task LoadProjectAsync_sets_DriveFileId_for_drive_scanned_files()
    {
        var driveFile = new ScannedFile(
            FileStorageDestination.GoogleDrive,
            "(5)-3-7-1-1-Plan.dwg",
            "drive-file-42",
            1024,
            DateTime.UtcNow,
            ProjectFileNameParser.TryParse("(5)-3-7-1-1-Plan.dwg"));
        var (vm, _) = CreateSut(FileStorageDestination.GoogleDrive, folderId: 30, fileId: 300, policy: null, driveFile);
        await vm.LoadProjectAsync(5);

        var version = FileNode(vm, 300).Children.OfType<AlternativeNodeVm>()
            .Single().Children.OfType<VersionNodeVm>().Single();

        Assert.True(version.IsDrive);
        Assert.Equal("drive-file-42", version.DriveFileId);
        Assert.Null(version.FullPath);
        Assert.Null(version.AccItemId);
    }

    [Fact]
    public async Task AddVersionAsync_to_drive_uploads_through_store()
    {
        var (vm, store) = CreateSut(FileStorageDestination.GoogleDrive, folderId: 30, fileId: 300, policy: null);
        await vm.LoadProjectAsync(5);

        var outcome = await vm.AddVersionAsync(FileNode(vm, 300), "1", @"C:\src\drawing.dwg");

        Assert.Equal(FileWriteStatus.Success, outcome.Status);
        var upload = Assert.Single(store.Uploads);
        Assert.Equal("(5)-3-7-1-1-Plan.dwg", upload.TargetName);
    }

    [Fact]
    public async Task DeleteVersionAsync_drive_deletes_through_store()
    {
        var driveFile = new ScannedFile(
            FileStorageDestination.GoogleDrive,
            "(5)-3-7-1-1-Plan.dwg",
            "drive-file-42",
            1024,
            DateTime.UtcNow,
            ProjectFileNameParser.TryParse("(5)-3-7-1-1-Plan.dwg"));
        var (vm, store) = CreateSut(FileStorageDestination.GoogleDrive, folderId: 30, fileId: 300, policy: null, driveFile);
        await vm.LoadProjectAsync(5);

        var version = FileNode(vm, 300).Children.OfType<AlternativeNodeVm>()
            .Single().Children.OfType<VersionNodeVm>().Single();

        var outcome = await vm.DeleteVersionAsync(version);

        Assert.Equal(FileWriteStatus.Success, outcome.Status);
        Assert.Equal("drive-file-42", Assert.Single(store.Deletes).NativeId);
    }
}
