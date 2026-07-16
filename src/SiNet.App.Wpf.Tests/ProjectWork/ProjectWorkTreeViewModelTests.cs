using Moq;
using SiNet.App.Wpf.Surfaces.ProjectWork;
using SiNet.Application.ProjectWork;
using SiNet.Domain.Files;
using Xunit;

namespace SiNet.App.Wpf.Tests.ProjectWork;

public sealed class ProjectWorkTreeViewModelTests
{
    private static ProjectFileTreeDto BuildTree() => new(
        ProjectId: 5,
        ProjectNumber: 5,
        RootFolders: new[]
        {
            new ProjectFolderDto(
                FolderId: 10,
                Name: "Drawings",
                ParentFolderId: null,
                Children: Array.Empty<ProjectFolderDto>(),
                Files: new[]
                {
                    new ProjectFileDefinitionDto(
                        FileId: 100,
                        BaseName: "Plan",
                        Extension: ".dwg",
                        StorageDestination: FileStorageDestination.FileServer,
                        FolderId: 10,
                        ProjectType: 3,
                        Number: 7,
                        TemplateLocation: null),
                }),
        });

    private static ProjectWorkTreeViewModel CreateSut(params ScannedFile[] scanned)
    {
        var query = new FakeProjectFileQueryService(BuildTree());
        var store = new FakeFileStore(
            FileStorageDestination.FileServer,
            (_, folderId) => folderId == 10 ? "h10" : null,
            handle => handle == "h10" ? scanned : Array.Empty<ScannedFile>());
        var index = new FileIndexService(new IFileStore[] { store });

        return new ProjectWorkTreeViewModel(
            query,
            index,
            Mock.Of<IActiveFileQueryHub>(),
            Mock.Of<IFileOpenHub>());
    }

    [Fact]
    public async Task LoadProjectAsync_builds_db_folder_skeleton_with_file_definition()
    {
        var sut = CreateSut();

        await sut.LoadProjectAsync(5);

        var folder = Assert.IsType<ProjectFolderNodeVm>(Assert.Single(sut.RootFolders));
        Assert.Equal(10, folder.FolderId);
        Assert.Equal("Drawings", folder.Title);
        var fileDef = Assert.Single(folder.Children.OfType<ProjectFileNodeVm>());
        Assert.Equal(100, fileDef.FileId);
    }

    [Fact]
    public async Task LoadProjectAsync_files_matching_scanned_file_under_its_definition()
    {
        var sut = CreateSut(FakeFileStore.FileServerFile("(5)-3-7-1-1-Plan.dwg"));

        await sut.LoadProjectAsync(5);

        var folder = (ProjectFolderNodeVm)sut.RootFolders[0];
        var fileDef = folder.Children.OfType<ProjectFileNodeVm>().Single(n => n.FileId == 100);
        var alt = Assert.Single(fileDef.Children.OfType<AlternativeNodeVm>());
        Assert.Equal("1", alt.AlternativeName);
        var version = Assert.Single(alt.Children.OfType<VersionNodeVm>());
        Assert.Equal(FileStorageDestination.FileServer, version.StorageDestination);
        Assert.Equal(1, version.VersionNumber);
        Assert.False(string.IsNullOrEmpty(version.FullPath));
        Assert.True(folder.HasFiles);
    }

    [Fact]
    public async Task LoadProjectAsync_places_non_matching_file_in_unfiled_bucket()
    {
        var sut = CreateSut(FakeFileStore.FileServerFile("random-notes.txt"));

        await sut.LoadProjectAsync(5);

        var folder = (ProjectFolderNodeVm)sut.RootFolders[0];
        var unfiled = folder.Children.OfType<ProjectFileNodeVm>().Single(n => n.IsUnfiled);
        var alt = Assert.Single(unfiled.Children.OfType<AlternativeNodeVm>());
        Assert.Equal("random-notes.txt", alt.AlternativeName);
        Assert.Single(alt.Children.OfType<VersionNodeVm>());
    }

    [Fact]
    public async Task LoadProjectAsync_with_no_tree_leaves_root_empty_and_unavailable()
    {
        var query = new FakeProjectFileQueryService(null);
        var index = new FileIndexService(Array.Empty<IFileStore>());
        var sut = new ProjectWorkTreeViewModel(query, index, Mock.Of<IActiveFileQueryHub>(), Mock.Of<IFileOpenHub>());

        await sut.LoadProjectAsync(999);

        Assert.Empty(sut.RootFolders);
        Assert.False(sut.IsAvailable);
    }

    [Fact]
    public async Task GetActiveFilesInFolder_exposes_loaded_files_to_other_surfaces()
    {
        var sut = CreateSut(FakeFileStore.FileServerFile("(5)-3-7-1-1-Plan.dwg"));

        await sut.LoadProjectAsync(5);

        var active = sut.GetActiveFilesInFolder(10);
        Assert.Contains(active, f => f.FileId == 100);
    }
}
