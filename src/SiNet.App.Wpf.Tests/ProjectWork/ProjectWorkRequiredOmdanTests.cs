using System.IO;
using Moq;
using SiNet.App.Wpf.Surfaces.ProjectWork;
using SiNet.Application.ProjectWork;
using SiNet.Application.Tasks;
using SiNet.Application.WorkSurfaces;
using SiNet.Domain.Files;
using SiNet.Infrastructure.Sql.Constants;
using SiNet.Infrastructure.Sql.Services.SeedData;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;
using FileStorageDestination = SiNet.Domain.Files.FileStorageDestination;

namespace SiNet.App.Wpf.Tests.ProjectWork;

public sealed class ProjectWorkRequiredOmdanTests
{
    private const string RequiredOmdanMessage = "יש להעלות את קובץ אומדן הצעה לפני סיום המשימה.";
    private const string RequiredQuoteMessage = "יש להעלות את קובץ הצעת מחיר לפני סיום המשימה.";

    private static (ProjectFileTreeDto Tree, ScannedFile[] Scanned) BuildTreeWithCodes(
        bool estimateRequired = true,
        bool estimatePhysical = false,
        bool documentRequired = false,
        bool documentPhysical = false)
    {
        var files = new List<ProjectFileDefinitionDto>
        {
            new(
                FileId: 100,
                BaseName: "אומדן הצעה",
                Extension: ".xlsx",
                StorageDestination: FileStorageDestination.FileServer,
                FolderId: 10,
                ProjectType: 9,
                Number: 7,
                TemplateLocation: null,
                OutSidData: null,
                IsRequired: estimateRequired,
                Code: ProjectFileCatalogCodes.QuoteEstimate),
        };
        if (documentRequired || documentPhysical)
        {
            files.Add(new ProjectFileDefinitionDto(
                FileId: 101,
                BaseName: "הצעת מחיר",
                Extension: ".docx",
                StorageDestination: FileStorageDestination.FileServer,
                FolderId: 10,
                ProjectType: 9,
                Number: 8,
                TemplateLocation: null,
                OutSidData: false,
                IsRequired: documentRequired,
                Code: ProjectFileCatalogCodes.QuoteDocument));
        }

        var scanned = new List<ScannedFile>();
        if (estimatePhysical)
            scanned.Add(FakeFileStore.FileServerFile("(5)-9-7-1-1-Omdan.xlsx"));
        if (documentPhysical)
            scanned.Add(FakeFileStore.FileServerFile("(5)-9-8-1-1-Quote.docx"));

        var tree = new ProjectFileTreeDto(
            ProjectId: 5,
            ProjectNumber: 5,
            RootFolders: new[]
            {
                new ProjectFolderDto(
                    FolderId: 10,
                    Name: "ניהול כספי",
                    ParentFolderId: null,
                    Children: Array.Empty<ProjectFolderDto>(),
                    Files: files),
            },
            ProjectNameAndNumber: "(5)בדיקה");
        return (tree, scanned.ToArray());
    }

    private static ProjectFileTreeDto BuildRequiredTree(bool isRequired = true) =>
        BuildTreeWithCodes(estimateRequired: isRequired).Tree;

    private static ProjectWorkTreeViewModel CreateTree(ProjectFileTreeDto tree, params ScannedFile[] scanned)
    {
        var query = new FakeProjectFileQueryService(tree);
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
    public async Task Required_file_without_scan_marks_required_missing_and_folder_orange_flag()
    {
        var sut = CreateTree(BuildRequiredTree());

        await sut.LoadProjectAsync(5);
        sut.SetActiveRequiredCatalogCodes(
            new HashSet<string>(StringComparer.Ordinal) { ProjectFileCatalogCodes.QuoteEstimate });

        var folder = Assert.IsType<ProjectFolderNodeVm>(Assert.Single(sut.RootFolders));
        var file = Assert.Single(folder.Children.OfType<ProjectFileNodeVm>());
        Assert.True(file.IsRequired);
        Assert.True(file.IsActiveCompletionGate);
        Assert.True(file.IsRequiredMissing);
        Assert.False(file.HasPhysicalVersions);
        Assert.True(folder.HasRequiredMissing);
        Assert.False(folder.HasPhysicalFiles);
        Assert.True(folder.HasDefinedFiles);
        Assert.False(sut.HasAllRequiredPhysicalFiles());
        Assert.False(sut.HasRequiredPhysicalFile(ProjectFileCatalogCodes.QuoteEstimate));
    }

    [Fact]
    public async Task Catalog_required_without_active_gate_is_not_orange()
    {
        var sut = CreateTree(BuildRequiredTree());

        await sut.LoadProjectAsync(5);
        // MaterialCheck / browse: no active gate codes.
        sut.SetActiveRequiredCatalogCodes(null);

        var folder = Assert.IsType<ProjectFolderNodeVm>(Assert.Single(sut.RootFolders));
        var file = Assert.Single(folder.Children.OfType<ProjectFileNodeVm>());
        Assert.True(file.IsRequired);
        Assert.False(file.IsActiveCompletionGate);
        Assert.False(file.IsRequiredMissing);
        Assert.False(folder.HasRequiredMissing);
    }

    [Fact]
    public async Task Active_gate_missing_keeps_folder_orange_when_sibling_has_physical()
    {
        var (tree, scanned) = BuildTreeWithCodes(
            estimateRequired: true,
            estimatePhysical: false,
            documentRequired: true,
            documentPhysical: true);
        var sut = CreateTree(tree, scanned);

        await sut.LoadProjectAsync(5);
        sut.SetActiveRequiredCatalogCodes(
            new HashSet<string>(StringComparer.Ordinal) { ProjectFileCatalogCodes.QuoteEstimate });

        var folder = Assert.IsType<ProjectFolderNodeVm>(Assert.Single(sut.RootFolders));
        var estimate = folder.Children.OfType<ProjectFileNodeVm>().Single(n => n.FileId == 100);
        var document = folder.Children.OfType<ProjectFileNodeVm>().Single(n => n.FileId == 101);
        Assert.True(estimate.IsRequiredMissing);
        Assert.False(document.IsRequiredMissing);
        Assert.True(document.HasPhysicalVersions);
        Assert.True(folder.HasPhysicalFiles);
        Assert.True(folder.HasRequiredMissing);
    }

    [Fact]
    public async Task Required_file_with_matching_scan_clears_required_missing()
    {
        var sut = CreateTree(
            BuildRequiredTree(),
            FakeFileStore.FileServerFile("(5)-9-7-1-1-Omdan.xlsx"));

        await sut.LoadProjectAsync(5);
        sut.SetActiveRequiredCatalogCodes(
            new HashSet<string>(StringComparer.Ordinal) { ProjectFileCatalogCodes.QuoteEstimate });

        var folder = Assert.IsType<ProjectFolderNodeVm>(Assert.Single(sut.RootFolders));
        var file = folder.Children.OfType<ProjectFileNodeVm>().Single(n => n.FileId == 100);
        Assert.True(file.HasPhysicalVersions);
        Assert.False(file.IsRequiredMissing);
        Assert.False(folder.HasRequiredMissing);
        Assert.True(folder.HasPhysicalFiles);
        Assert.True(sut.HasAllRequiredPhysicalFiles());
        Assert.True(sut.HasRequiredPhysicalFile(ProjectFileCatalogCodes.QuoteEstimate));
    }

    [Theory]
    [InlineData("PrepareQuoteCalculation", ProjectWorkActiveRequiredCatalog.QuoteEstimate)]
    [InlineData("PrepareQuoteDocument", ProjectWorkActiveRequiredCatalog.QuoteDocument)]
    [InlineData("FollowQuoteApproval", ProjectWorkActiveRequiredCatalog.QuoteClientApproval)]
    [InlineData("CheckQuoteMaterialCompleteness", null)]
    [InlineData(null, null)]
    public void Resolve_active_codes_matches_completion_gates(string? taskType, string? expectedCode)
    {
        WorkSurfaceContext? context = taskType is null
            ? null
            : CreateCalcContext() with { TaskTypeCode = taskType, AllowedResultCodes = Array.Empty<string>() };

        var codes = ProjectWorkActiveRequiredCatalog.Resolve(context);
        if (expectedCode is null)
            Assert.Empty(codes);
        else
            Assert.Equal(new[] { expectedCode }, codes);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_blocks_PrepareQuoteCalculation_when_omdan_missing()
    {
        var completion = new RecordingCompletion();
        var tree = CreateTree(BuildRequiredTree());
        var sut = new ProjectWorkWindowViewModel(completion, tree: tree);
        var context = CreateCalcContext();

        Assert.True(await sut.ApplyContextAsync(context));
        var ok = await sut.CompleteFromTaskAsync();

        Assert.False(ok);
        Assert.Equal(0, completion.CallCount);
        Assert.Equal(RequiredOmdanMessage, sut.StatusMessage);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_allows_PrepareQuoteCalculation_when_omdan_physical_exists_even_if_quote_doc_missing()
    {
        var (treeDto, _) = BuildTreeWithCodes(
            estimateRequired: true,
            estimatePhysical: true,
            documentRequired: true,
            documentPhysical: false);
        var completion = new RecordingCompletion();
        var tree = CreateTree(treeDto, FakeFileStore.FileServerFile("(5)-9-7-1-1-Omdan.xlsx"));
        var sut = new ProjectWorkWindowViewModel(completion, tree: tree);

        Assert.True(await sut.ApplyContextAsync(CreateCalcContext()));
        var ok = await sut.CompleteFromTaskAsync();

        Assert.True(ok);
        Assert.Equal(1, completion.CallCount);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_blocks_PrepareQuoteDocument_when_quote_doc_missing()
    {
        var (treeDto, _) = BuildTreeWithCodes(
            estimateRequired: true,
            estimatePhysical: true,
            documentRequired: true,
            documentPhysical: false);
        var completion = new RecordingCompletion();
        var tree = CreateTree(treeDto, FakeFileStore.FileServerFile("(5)-9-7-1-1-Omdan.xlsx"));
        var sut = new ProjectWorkWindowViewModel(completion, tree: tree);

        Assert.True(await sut.ApplyContextAsync(CreatePrepContext()));
        var ok = await sut.CompleteFromTaskAsync();

        Assert.False(ok);
        Assert.Equal(0, completion.CallCount);
        Assert.Equal(RequiredQuoteMessage, sut.StatusMessage);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_allows_PrepareQuoteDocument_when_quote_doc_physical_exists()
    {
        var (treeDto, scanned) = BuildTreeWithCodes(
            estimateRequired: true,
            estimatePhysical: true,
            documentRequired: true,
            documentPhysical: true);
        var completion = new RecordingCompletion();
        var tree = CreateTree(treeDto, scanned);
        var sut = new ProjectWorkWindowViewModel(completion, tree: tree);

        Assert.True(await sut.ApplyContextAsync(CreatePrepContext()));
        var ok = await sut.CompleteFromTaskAsync();

        Assert.True(ok);
        Assert.Equal(1, completion.CallCount);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_does_not_gate_non_calc_tasks()
    {
        var completion = new RecordingCompletion();
        var tree = CreateTree(BuildRequiredTree());
        var sut = new ProjectWorkWindowViewModel(completion, tree: tree);
        var context = CreateCalcContext() with
        {
            TaskTypeCode = "CheckQuoteMaterialCompleteness",
            CompletionEventCode = "ReviewMaterialCheckCompleted",
            AllowedResultCodes = ["MaterialComplete"],
        };

        Assert.True(await sut.ApplyContextAsync(context));
        sut.SelectedResultCode = "MaterialComplete";
        var ok = await sut.CompleteFromTaskAsync();

        Assert.True(ok);
        Assert.Equal(1, completion.CallCount);
    }

    [Fact]
    public async Task Catalog_seed_inserts_estimate_and_quote_document_under_finance_folder()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new SiNetSQLDbContext(options);
        db.JobTypes.Add(new JobType { Id = 9, Title = "חומר כללי" });
        db.ProjectFolders.Add(new ProjectFolder { Id = 1, Title = "תיקיית הפרויקט" });
        db.ProjectFolders.Add(new ProjectFolder { Id = 2, Title = "תכתובת", Infolderid = 1 });
        db.ProjectFolders.Add(new ProjectFolder { Id = 3, Title = "ניהול_כספי", Infolderid = 2 });
        await db.SaveChangesAsync();

        var first = await ProjectFileCatalogSeedData.EnsureAsync(db);
        var second = await ProjectFileCatalogSeedData.EnsureAsync(db);

        Assert.Contains("inserted", first, StringComparison.Ordinal);
        Assert.Contains("unchanged", second, StringComparison.Ordinal);

        var financeFolder = Assert.Single(await db.ProjectFolders.Where(f => f.Title == "ניהול_כספי").ToListAsync());
        Assert.Equal(2, financeFolder.Infolderid);
        var quoteFolder = Assert.Single(await db.ProjectFolders.Where(f => f.Title == "הצעת_מחיר").ToListAsync());
        Assert.Equal(financeFolder.Id, quoteFolder.Infolderid);

        var rows = await db.ProjectFiles.Where(f => f.IsRequired).OrderBy(f => f.Code).ToListAsync();
        Assert.Equal(4, rows.Count);
        Assert.Contains(rows, r => r.Code == ProjectFileCatalogCodes.QuoteEstimate && r.Title == ProjectFileRequiredOmdanSeedData.DisplayTitle);
        var quote = Assert.Single(rows, r => r.Code == ProjectFileCatalogCodes.QuoteDocument);
        Assert.Equal("הצעת_מחיר", quote.Title);
        Assert.Equal(".docx", quote.Typefile);
        Assert.False(quote.OutSidData);
        Assert.Equal(financeFolder.Id, quote.Folderid);
        var clientApproval = Assert.Single(rows, r => r.Code == ProjectFileCatalogCodes.QuoteClientApproval);
        Assert.Equal("אישור_לקוח_להצעה", clientApproval.Title);
        Assert.Equal(".pdf", clientApproval.Typefile);
        Assert.Equal(financeFolder.Id, clientApproval.Folderid);
        var clientRequest = Assert.Single(rows, r => r.Code == ProjectFileCatalogCodes.QuoteClientRequest);
        Assert.Equal("דרישת_המזמין_להצעת_מחיר", clientRequest.Title);
        Assert.Equal(".pdf", clientRequest.Typefile);
        Assert.True(clientRequest.OutSidData);
        Assert.Equal(quoteFolder.Id, clientRequest.Folderid);

        var sendDoc = Assert.Single(await db.ProjectFiles.Where(f => f.Code == ProjectFileCatalogCodes.QuoteSendDocument).ToListAsync());
        Assert.Equal("הצעה_לשליחה", sendDoc.Title);
        Assert.Equal(".pdf", sendDoc.Typefile);
        Assert.False(sendDoc.IsRequired);
        Assert.False(sendDoc.OutSidData);
        Assert.Equal(financeFolder.Id, sendDoc.Folderid);

        // Title rename must not break Code identity on second ensure.
        var omdan = rows.Single(r => r.Code == ProjectFileCatalogCodes.QuoteEstimate);
        omdan.Title = "שם מותאם";
        await db.SaveChangesAsync();
        var afterRename = await ProjectFileCatalogSeedData.EnsureAsync(db);
        Assert.Contains("unchanged", afterRename, StringComparison.Ordinal);
        var again = Assert.Single(await db.ProjectFiles.Where(f => f.Code == ProjectFileCatalogCodes.QuoteEstimate).ToListAsync());
        Assert.Equal("שם מותאם", again.Title);
        Assert.True(again.IsRequired);
    }

    [Fact]
    public async Task Omdan_seed_inserts_even_when_same_title_exists_on_another_job_type()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new SiNetSQLDbContext(options);
        db.JobTypes.Add(new JobType { Id = 9, Title = "חומר כללי" });
        db.JobTypes.Add(new JobType { Id = 2, Title = "אחר" });
        db.ProjectFolders.Add(new ProjectFolder { Id = 1, Title = "תיקיית הפרויקט" });
        db.ProjectFolders.Add(new ProjectFolder { Id = 2, Title = "תכתובת", Infolderid = 1 });
        db.ProjectFolders.Add(new ProjectFolder { Id = 3, Title = "ניהול_כספי", Infolderid = 2 });
        db.ProjectFiles.Add(new ProjectFile
        {
            Id = 1,
            Title = "אומדן_הצעה",
            Number = 1,
            TypeProjId = 2,
            Folderid = 1,
        });
        await db.SaveChangesAsync();

        var result = await ProjectFileCatalogSeedData.EnsureAsync(db);

        Assert.DoesNotContain("skipped", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inserted", result, StringComparison.Ordinal);
        var omdan = await db.ProjectFiles.SingleAsync(f => f.Code == ProjectFileCatalogCodes.QuoteEstimate);
        Assert.Equal(9, omdan.TypeProjId);
        Assert.Equal("אומדן_הצעה", omdan.Title);
        Assert.Equal(3, omdan.Folderid);
        Assert.NotNull(await db.ProjectFiles.SingleOrDefaultAsync(f => f.Code == ProjectFileCatalogCodes.QuoteDocument));
        Assert.NotNull(await db.ProjectFiles.SingleOrDefaultAsync(f => f.Code == ProjectFileCatalogCodes.QuoteClientApproval));
        Assert.NotNull(await db.ProjectFiles.SingleOrDefaultAsync(f => f.Code == ProjectFileCatalogCodes.QuoteClientRequest));
        Assert.NotNull(await db.ProjectFiles.SingleOrDefaultAsync(f => f.Code == ProjectFileCatalogCodes.QuoteSendDocument));
        var quoteFolder = Assert.Single(await db.ProjectFolders.Where(f => f.Title == "הצעת_מחיר").ToListAsync());
        Assert.Equal(3, quoteFolder.Infolderid);
    }

    [Fact]
    public async Task Omdan_seed_skips_when_parent_folder_tachtuvet_missing_and_does_not_create_hatzaat_mechir_folder()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new SiNetSQLDbContext(options);
        db.JobTypes.Add(new JobType { Id = 9, Title = "חומר כללי" });
        db.ProjectFolders.Add(new ProjectFolder { Id = 1, Title = "תיקיית הפרויקט" });
        await db.SaveChangesAsync();

        var result = await ProjectFileCatalogSeedData.EnsureAsync(db);

        Assert.Contains("skipped", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("תכתובת", result, StringComparison.Ordinal);
        Assert.Empty(await db.ProjectFolders.Where(f => f.Title == "הצעת_מחיר").ToListAsync());
        Assert.Empty(await db.ProjectFiles.Where(f => f.Code == ProjectFileCatalogCodes.QuoteEstimate).ToListAsync());
        Assert.Empty(await db.ProjectFiles.Where(f => f.Code == ProjectFileCatalogCodes.QuoteDocument).ToListAsync());
        Assert.Empty(await db.ProjectFiles.Where(f => f.Code == ProjectFileCatalogCodes.QuoteClientApproval).ToListAsync());
        Assert.Empty(await db.ProjectFiles.Where(f => f.Code == ProjectFileCatalogCodes.QuoteClientRequest).ToListAsync());
        Assert.Empty(await db.ProjectFiles.Where(f => f.Code == ProjectFileCatalogCodes.QuoteSendDocument).ToListAsync());
        Assert.Empty(await db.ProjectFolders.Where(f => f.Title == "הצעת מחיר").ToListAsync());
    }

    [Fact]
    public async Task Omdan_seed_reparents_finance_folder_under_tachtuvet_when_orphaned()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new SiNetSQLDbContext(options);
        db.JobTypes.Add(new JobType { Id = 9, Title = "חומר כללי" });
        db.ProjectFolders.Add(new ProjectFolder { Id = 1, Title = "תיקיית הפרויקט" });
        db.ProjectFolders.Add(new ProjectFolder { Id = 2, Title = "תכתובת", Infolderid = 1 });
        db.ProjectFolders.Add(new ProjectFolder { Id = 10, Title = "ניהול כספי", Infolderid = null });
        await db.SaveChangesAsync();

        _ = await ProjectFileCatalogSeedData.EnsureAsync(db);

        // Space-named orphan is renamed to underscore canonical and reparented.
        var finance = await db.ProjectFolders.SingleAsync(f => f.Title == "ניהול_כספי");
        Assert.Equal(2, finance.Infolderid);
        Assert.Empty(await db.ProjectFolders.Where(f => f.Title == "ניהול כספי").ToListAsync());
    }

    [Fact]
    public async Task Omdan_seed_renames_known_legacy_title_and_does_not_delete_other_files()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new SiNetSQLDbContext(options);
        db.JobTypes.Add(new JobType { Id = 9, Title = "חומר כללי" });
        db.ProjectFolders.Add(new ProjectFolder { Id = 1, Title = "תיקיית הפרויקט" });
        db.ProjectFolders.Add(new ProjectFolder { Id = 2, Title = "תכתובת", Infolderid = 1 });
        db.ProjectFolders.Add(new ProjectFolder { Id = 10, Title = "ניהול_כספי", Infolderid = 2 });
        db.ProjectFiles.Add(new ProjectFile
        {
            Id = 50,
            Title = "קובץ אחר",
            Number = 1,
            TypeProjId = 9,
            Folderid = 10,
            Typefile = ".pdf",
        });
        db.ProjectFiles.Add(new ProjectFile
        {
            Id = 51,
            Code = ProjectFileCatalogCodes.QuoteEstimate,
            Title = "אומדן הצעת מחיר",
            Number = 2,
            TypeProjId = 9,
            Folderid = 10,
            Typefile = ".xlsx",
            IsRequired = false,
        });
        await db.SaveChangesAsync();

        var result = await ProjectFileCatalogSeedData.EnsureAsync(db);

        Assert.Contains("updated", result, StringComparison.Ordinal);
        Assert.Equal(6, await db.ProjectFiles.CountAsync());
        Assert.NotNull(await db.ProjectFiles.SingleAsync(f => f.Id == 50 && f.Title == "קובץ אחר"));
        var omdan = await db.ProjectFiles.SingleAsync(f => f.Code == ProjectFileCatalogCodes.QuoteEstimate);
        Assert.Equal("אומדן_הצעה", omdan.Title);
        Assert.True(omdan.IsRequired);
        Assert.Equal(10, omdan.Folderid);
        Assert.NotNull(await db.ProjectFiles.SingleOrDefaultAsync(f => f.Code == ProjectFileCatalogCodes.QuoteDocument));
        Assert.NotNull(await db.ProjectFiles.SingleOrDefaultAsync(f => f.Code == ProjectFileCatalogCodes.QuoteClientApproval));
        Assert.NotNull(await db.ProjectFiles.SingleOrDefaultAsync(f => f.Code == ProjectFileCatalogCodes.QuoteSendDocument));
        var clientRequest = await db.ProjectFiles.SingleAsync(f => f.Code == ProjectFileCatalogCodes.QuoteClientRequest);
        Assert.Equal("דרישת_המזמין_להצעת_מחיר", clientRequest.Title);
        var quoteFolder = await db.ProjectFolders.SingleAsync(f => f.Title == "הצעת_מחיר");
        Assert.Equal(10, quoteFolder.Infolderid);
        Assert.Equal(quoteFolder.Id, clientRequest.Folderid);
    }

    [Fact]
    public async Task Catalog_seed_prefers_underscore_template_row_and_removes_space_duplicate()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new SiNetSQLDbContext(options);
        db.JobTypes.Add(new JobType { Id = 9, Title = "חומר כללי" });
        db.ProjectFolders.Add(new ProjectFolder { Id = 1, Title = "תיקיית הפרויקט" });
        db.ProjectFolders.Add(new ProjectFolder { Id = 2, Title = "תכתובת", Infolderid = 1 });
        db.ProjectFolders.Add(new ProjectFolder { Id = 3, Title = "ניהול_כספי", Infolderid = 2 });
        db.ProjectFolders.Add(new ProjectFolder { Id = 4, Title = "ניהול כספי", Infolderid = 2 });
        db.ProjectFiles.Add(new ProjectFile
        {
            Id = 10,
            Title = "הצעת_מחיר",
            Number = 1,
            TypeProjId = 9,
            Folderid = 3,
            Typefile = ".docx",
            TemplateLocation = @"C:\templates\quote.docx",
        });
        db.ProjectFiles.Add(new ProjectFile
        {
            Id = 11,
            Code = ProjectFileCatalogCodes.QuoteDocument,
            Title = "הצעת מחיר",
            Number = 2,
            TypeProjId = 9,
            Folderid = 4,
            Typefile = ".docx",
            IsRequired = true,
            OutSidData = false,
        });
        await db.SaveChangesAsync();

        var result = await ProjectFileCatalogSeedData.EnsureAsync(db);

        Assert.Contains("cleanup", result, StringComparison.OrdinalIgnoreCase);
        var keeper = await db.ProjectFiles.SingleAsync(f => f.Code == ProjectFileCatalogCodes.QuoteDocument);
        Assert.Equal(10, keeper.Id);
        Assert.Equal("הצעת_מחיר", keeper.Title);
        Assert.Equal(@"C:\templates\quote.docx", keeper.TemplateLocation);
        Assert.Equal(3, keeper.Folderid);
        Assert.Null(await db.ProjectFiles.FirstOrDefaultAsync(f => f.Id == 11));
        Assert.Empty(await db.ProjectFolders.Where(f => f.Title == "ניהול כספי").ToListAsync());
    }

    [Fact]
    public async Task Catalog_seed_preserves_template_when_coded_row_lacks_it_and_space_alias_has_it()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new SiNetSQLDbContext(options);
        db.JobTypes.Add(new JobType { Id = 9, Title = "חומר כללי" });
        db.ProjectFolders.Add(new ProjectFolder { Id = 1, Title = "תיקיית הפרויקט" });
        db.ProjectFolders.Add(new ProjectFolder { Id = 2, Title = "תכתובת", Infolderid = 1 });
        db.ProjectFolders.Add(new ProjectFolder { Id = 3, Title = "ניהול_כספי", Infolderid = 2 });
        // Coded keeper from a bad prior seed — no template.
        db.ProjectFiles.Add(new ProjectFile
        {
            Id = 20,
            Code = ProjectFileCatalogCodes.QuoteDocument,
            Title = "הצעת_מחיר",
            Number = 1,
            TypeProjId = 9,
            Folderid = 3,
            Typefile = ".docx",
            IsRequired = true,
            OutSidData = false,
            TemplateLocation = null,
        });
        // Office row that still holds the template under the space alias.
        db.ProjectFiles.Add(new ProjectFile
        {
            Id = 21,
            Title = "הצעת מחיר",
            Number = 2,
            TypeProjId = 9,
            Folderid = 3,
            Typefile = ".docx",
            TemplateLocation = @"D:\office\templates\hatzaat_mechir.docx",
        });
        await db.SaveChangesAsync();

        _ = await ProjectFileCatalogSeedData.EnsureAsync(db);

        var keeper = await db.ProjectFiles.SingleAsync(f => f.Code == ProjectFileCatalogCodes.QuoteDocument);
        Assert.Equal(@"D:\office\templates\hatzaat_mechir.docx", keeper.TemplateLocation);
        Assert.Equal("הצעת_מחיר", keeper.Title);
        Assert.Empty(await db.ProjectFiles.Where(f => f.Title == "הצעת מחיר").ToListAsync());
    }

    [Fact]
    public void Alternative_name_prompt_uses_dialog_not_silent_auto_assign()
    {
        var cs = File.ReadAllText(Path.Combine(
            RepoRoot,
            "src",
            "SiNet.App.Wpf",
            "Surfaces",
            "ProjectWork",
            "ProjectWorkTreeViewModel.cs"));
        Assert.Contains("StringPromptDialog.Prompt", cs, StringComparison.Ordinal);
        Assert.Contains("AddVersionFromTemplateAsync", cs, StringComparison.Ordinal);
        Assert.DoesNotContain("Default to the next free numeric alternative label", cs, StringComparison.Ordinal);
    }

    private static WorkSurfaceContext CreateCalcContext() =>
        new(
            TaskId: 19,
            ProjectId: 5,
            WorkflowInstanceId: 1,
            ComponentKey: WorkSurfaceComponentKeys.ProjectWork,
            PrimaryWorkTargetEntityId: null,
            AllowedResultCodes: ["QuoteCalculationCompleted"],
            CompletionEventCode: "Review.QuoteCalculationCompleted",
            ActingUserId: 7,
            TaskTypeCode: "PrepareQuoteCalculation");

    private static WorkSurfaceContext CreatePrepContext() =>
        new(
            TaskId: 20,
            ProjectId: 5,
            WorkflowInstanceId: 1,
            ComponentKey: WorkSurfaceComponentKeys.ProjectWork,
            PrimaryWorkTargetEntityId: null,
            AllowedResultCodes: ["QuotePrepared"],
            CompletionEventCode: "Review.QuotePrepared",
            ActingUserId: 7,
            TaskTypeCode: "PrepareQuoteDocument");

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private sealed class RecordingCompletion : ITaskCompletionService
    {
        public int CallCount { get; private set; }

        public ValueTask<TaskCompletionResultDto> CompleteAsync(CompleteTaskCommand command, CancellationToken ct)
        {
            CallCount++;
            return ValueTask.FromResult(new TaskCompletionResultDto(
                Success: true,
                TaskClosed: true,
                WorkflowAdvanced: true));
        }
    }
}
