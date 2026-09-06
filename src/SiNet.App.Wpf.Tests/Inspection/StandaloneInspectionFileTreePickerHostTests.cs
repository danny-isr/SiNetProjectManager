using SiNet.App.Wpf.Surfaces.Inspection;
using SiNet.Application.ProjectWork;
using SiNet.Domain.Files;
using Xunit;

namespace SiNet.App.Wpf.Tests.Inspection;

public sealed class StandaloneInspectionFileTreePickerHostTests
{
    [Fact]
    public void BuildPickerRootsFromProjectTree_maps_files_without_active_hub()
    {
        var tree = new ProjectFileTreeDto(
            ProjectId: 136,
            ProjectNumber: 136,
            RootFolders:
            [
                new ProjectFolderDto(
                    FolderId: 1,
                    Name: "תוכניות",
                    ParentFolderId: null,
                    Children: [],
                    Files:
                    [
                        new ProjectFileDefinitionDto(
                            FileId: 10,
                            BaseName: "תוכנית אדריכלית",
                            Extension: ".pdf",
                            StorageDestination: FileStorageDestination.FileServer,
                            FolderId: 1,
                            ProjectType: 1,
                            Number: 1,
                            TemplateLocation: null),
                    ]),
            ]);

        var roots = StandaloneInspectionFileTreePickerHost.BuildPickerRootsFromProjectTree(tree);

        Assert.Single(roots);
        Assert.Equal("תוכניות", roots[0].Title);
        Assert.Single(roots[0].Children);
        Assert.Equal("תוכנית אדריכלית.pdf", roots[0].Children[0].Title);
        Assert.True(roots[0].Children[0].IsSelectable);
    }

    [Fact]
    public void BuildPickerRootsFromProjectTree_returns_empty_when_null()
    {
        var roots = StandaloneInspectionFileTreePickerHost.BuildPickerRootsFromProjectTree(null);
        Assert.Empty(roots);
    }
}
