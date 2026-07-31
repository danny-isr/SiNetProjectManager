using System.IO;
using SiNet.Application.Email.QuoteSend;
using SiNet.Application.ProjectWork;
using SiNet.Domain.Files;
using SiNet.App.Wpf.Tests.ProjectWork;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

public sealed class QuoteSendAttachmentServiceTests
{
    private static ProjectFileTreeDto CreateTree(int projectNumber = 3142) =>
        new(
            ProjectId: 1,
            ProjectNumber: projectNumber,
            RootFolders:
            [
                new ProjectFolderDto(
                    FolderId: 10,
                    Name: "תכתובת",
                    ParentFolderId: null,
                    Children:
                    [
                        new ProjectFolderDto(
                            FolderId: 20,
                            Name: "ניהול_כספי",
                            ParentFolderId: 10,
                            Children: Array.Empty<ProjectFolderDto>(),
                            Files:
                            [
                                new ProjectFileDefinitionDto(
                                    FileId: 100,
                                    BaseName: "הצעת_מחיר_לשליחה",
                                    Extension: ".pdf",
                                    StorageDestination: FileStorageDestination.FileServer,
                                    FolderId: 20,
                                    ProjectType: 9,
                                    Number: 42,
                                    TemplateLocation: null,
                                    OutSidData: false,
                                    IsRequired: false,
                                    Code: IQuoteSendAttachmentService.CatalogCode),
                            ]),
                    ],
                    Files: Array.Empty<ProjectFileDefinitionDto>()),
            ]);

    [Fact]
    public async Task ResolveAttachInitialDirectory_returns_finance_folder_path()
    {
        var query = new FakeProjectFileQueryService(CreateTree());
        var store = new FakeFileStore(
            FileStorageDestination.FileServer,
            (_, folderId) => folderId == 20 ? @"C:\projects\3142\ניהול_כספי" : null,
            _ => Array.Empty<ScannedFile>());
        var sut = new QuoteSendAttachmentService(query, new FileIndexService([store]));

        var path = await sut.ResolveAttachInitialDirectoryAsync(1);

        Assert.Equal(@"C:\projects\3142\ניהול_כספי", path);
    }

    [Fact]
    public async Task EnsureFiledIfNeeded_skips_when_slot_already_has_physical_match()
    {
        var existing = FakeFileStore.FileServerFile("(3142)-9-42-1-1-הצעת_מחי.pdf");
        var query = new FakeProjectFileQueryService(CreateTree());
        var store = new FakeFileStore(
            FileStorageDestination.FileServer,
            (_, _) => @"C:\finance",
            _ => [existing]);
        var sut = new QuoteSendAttachmentService(query, new FileIndexService([store]));

        var temp = Path.Combine(Path.GetTempPath(), $"qs-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(temp, [0x25, 0x50, 0x44, 0x46]); // %PDF
        try
        {
            var result = await sut.EnsureFiledIfNeededAsync(1, temp);

            Assert.True(result.Success);
            Assert.True(result.AlreadyFiled);
            Assert.False(result.FiledNow);
            Assert.Empty(store.Uploads);
            Assert.Equal(existing.NativeId, result.FiledCanonicalPath);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public async Task EnsureFiledIfNeeded_places_when_slot_empty()
    {
        var query = new FakeProjectFileQueryService(CreateTree());
        var store = new FakeFileStore(
            FileStorageDestination.FileServer,
            (_, _) => @"C:\finance",
            _ => Array.Empty<ScannedFile>());
        var sut = new QuoteSendAttachmentService(query, new FileIndexService([store]));

        var temp = Path.Combine(Path.GetTempPath(), $"qs-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(temp, [0x25, 0x50, 0x44, 0x46]);
        try
        {
            var result = await sut.EnsureFiledIfNeededAsync(1, temp);

            Assert.True(result.Success);
            Assert.False(result.AlreadyFiled);
            Assert.True(result.FiledNow);
            var upload = Assert.Single(store.Uploads);
            Assert.Equal(temp, upload.Source);
            Assert.StartsWith("(3142)-9-42-1-1-", upload.TargetName, StringComparison.Ordinal);
            Assert.EndsWith(".pdf", upload.TargetName, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public async Task EnsureFiledIfNeeded_rejects_non_pdf()
    {
        var query = new FakeProjectFileQueryService(CreateTree());
        var store = new FakeFileStore(
            FileStorageDestination.FileServer,
            (_, _) => @"C:\finance",
            _ => Array.Empty<ScannedFile>());
        var sut = new QuoteSendAttachmentService(query, new FileIndexService([store]));

        var temp = Path.Combine(Path.GetTempPath(), $"qs-{Guid.NewGuid():N}.docx");
        await File.WriteAllBytesAsync(temp, [1, 2, 3]);
        try
        {
            var result = await sut.EnsureFiledIfNeededAsync(1, temp);

            Assert.False(result.Success);
            Assert.Contains("PDF", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(store.Uploads);
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
