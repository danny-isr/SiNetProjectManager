using System.IO;
using SiNet.Application.Email.QuoteSend;
using SiNet.Application.ProjectWork;
using SiNet.Domain.Files;
using SiNet.App.Wpf.Tests.ProjectWork;
using Xunit;

namespace SiNet.App.Wpf.Tests.Email;

public sealed class QuoteSendAttachmentServiceTests
{
    private static ProjectFileTreeDto CreateTree(
        int projectNumber = 3142,
        bool includeSendDocument = true) =>
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
                            Files: includeSendDocument
                                ?
                                [
                                    new ProjectFileDefinitionDto(
                                        FileId: 100,
                                        BaseName: "הצעה_לשליחה",
                                        Extension: ".pdf",
                                        StorageDestination: FileStorageDestination.FileServer,
                                        FolderId: 20,
                                        ProjectType: 9,
                                        Number: 42,
                                        TemplateLocation: null,
                                        OutSidData: false,
                                        IsRequired: false,
                                        Code: IQuoteSendAttachmentService.CatalogCode),
                                ]
                                :
                                [
                                    new ProjectFileDefinitionDto(
                                        FileId: 101,
                                        BaseName: "הצעת_מחיר",
                                        Extension: ".docx",
                                        StorageDestination: FileStorageDestination.FileServer,
                                        FolderId: 20,
                                        ProjectType: 9,
                                        Number: 7,
                                        TemplateLocation: null,
                                        OutSidData: false,
                                        IsRequired: true,
                                        Code: "QuoteDocument"),
                                ]),
                    ],
                    Files: Array.Empty<ProjectFileDefinitionDto>()),
            ]);

    [Fact]
    public async Task ResolveAttachInitialDirectory_returns_finance_folder_path()
    {
        var dir = Directory.CreateTempSubdirectory("qs-finance-");
        try
        {
            var query = new FakeProjectFileQueryService(CreateTree());
            var store = new FakeFileStore(
                FileStorageDestination.FileServer,
                (_, folderId) => folderId == 20 ? dir.FullName : null,
                _ => Array.Empty<ScannedFile>());
            var sut = new QuoteSendAttachmentService(query, new FileIndexService([store]));

            var path = await sut.ResolveAttachInitialDirectoryAsync(1);

            Assert.Equal(dir.FullName, path);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task EnsureFiledIfNeeded_skips_when_selected_file_is_already_send_document()
    {
        var existingName = "(3142)-9-42-1-1-הצעה_לשליחה.pdf";
        var temp = Path.Combine(Path.GetTempPath(), existingName);
        await File.WriteAllBytesAsync(temp, [0x25, 0x50, 0x44, 0x46]);
        try
        {
            var existing = new ScannedFile(
                Source: FileStorageDestination.FileServer,
                FileName: existingName,
                NativeId: temp,
                SizeBytes: 4,
                LastModified: DateTime.Now,
                Parsed: ProjectFileNameParser.TryParse(existingName));
            var query = new FakeProjectFileQueryService(CreateTree());
            var store = new FakeFileStore(
                FileStorageDestination.FileServer,
                (_, _) => Path.GetDirectoryName(temp)!,
                _ => [existing]);
            var sut = new QuoteSendAttachmentService(query, new FileIndexService([store]));

            var result = await sut.EnsureFiledIfNeededAsync(1, temp);

            Assert.True(result.Success);
            Assert.True(result.AlreadyFiled);
            Assert.False(result.RequiresNewAlternative);
            Assert.Empty(store.Uploads);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public async Task EnsureFiledIfNeeded_requires_new_alternative_when_other_pdf_selected()
    {
        var existing = FakeFileStore.FileServerFile("(3142)-9-42-1-1-הצעה_לשליחה.pdf");
        var query = new FakeProjectFileQueryService(CreateTree());
        var store = new FakeFileStore(
            FileStorageDestination.FileServer,
            (_, _) => @"C:\finance",
            _ => [existing]);
        var sut = new QuoteSendAttachmentService(query, new FileIndexService([store]));

        var temp = Path.Combine(Path.GetTempPath(), $"qs-other-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(temp, [0x25, 0x50, 0x44, 0x46]);
        try
        {
            var result = await sut.EnsureFiledIfNeededAsync(1, temp);

            Assert.False(result.Success);
            Assert.True(result.RequiresNewAlternative);
            Assert.Contains("1", result.ExistingAlternatives);
            Assert.Equal("2", result.SuggestedAlternative);
            Assert.Empty(store.Uploads);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public async Task EnsureFiledIfNeeded_places_under_new_alternative_when_supplied()
    {
        var existing = FakeFileStore.FileServerFile("(3142)-9-42-1-1-הצעה_לשליחה.pdf");
        var query = new FakeProjectFileQueryService(CreateTree());
        var store = new FakeFileStore(
            FileStorageDestination.FileServer,
            (_, _) => @"C:\finance",
            _ => [existing]);
        var sut = new QuoteSendAttachmentService(query, new FileIndexService([store]));

        var temp = Path.Combine(Path.GetTempPath(), $"qs-other-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(temp, [0x25, 0x50, 0x44, 0x46]);
        try
        {
            var result = await sut.EnsureFiledIfNeededAsync(1, temp, alternativeName: "2");

            Assert.True(result.Success);
            Assert.True(result.FiledNow);
            var upload = Assert.Single(store.Uploads);
            Assert.Contains("-2-1-", upload.TargetName, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public async Task EnsureFiledIfNeeded_places_alt1_when_slot_empty()
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
            Assert.True(result.FiledNow);
            var upload = Assert.Single(store.Uploads);
            Assert.Equal("(3142)-9-42-1-1-הצעה_לשליחה.pdf", upload.TargetName);
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
