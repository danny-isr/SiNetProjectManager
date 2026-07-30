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
    private const string RequiredMessage = "יש להעלות את קובץ אומדן הצעה לפני סיום המשימה.";

    private static ProjectFileTreeDto BuildRequiredTree(bool isRequired = true) => new(
        ProjectId: 5,
        ProjectNumber: 5,
        RootFolders: new[]
        {
            new ProjectFolderDto(
                FolderId: 10,
                Name: "ניהול כספי",
                ParentFolderId: null,
                Children: Array.Empty<ProjectFolderDto>(),
                Files: new[]
                {
                    new ProjectFileDefinitionDto(
                        FileId: 100,
                        BaseName: "אומדן הצעה",
                        Extension: ".xlsx",
                        StorageDestination: FileStorageDestination.FileServer,
                        FolderId: 10,
                        ProjectType: 9,
                        Number: 7,
                        TemplateLocation: null,
                        IsRequired: isRequired,
                        Code: "QuoteEstimate"),
                }),
        },
        ProjectNameAndNumber: "(5)בדיקה");

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

        var folder = Assert.IsType<ProjectFolderNodeVm>(Assert.Single(sut.RootFolders));
        var file = Assert.Single(folder.Children.OfType<ProjectFileNodeVm>());
        Assert.True(file.IsRequired);
        Assert.True(file.IsRequiredMissing);
        Assert.False(file.HasPhysicalVersions);
        Assert.True(folder.HasRequiredMissing);
        Assert.False(folder.HasPhysicalFiles);
        Assert.True(folder.HasDefinedFiles);
        Assert.False(sut.HasAllRequiredPhysicalFiles());
    }

    [Fact]
    public async Task Required_file_with_matching_scan_clears_required_missing()
    {
        var sut = CreateTree(
            BuildRequiredTree(),
            FakeFileStore.FileServerFile("(5)-9-7-1-1-Omdan.xlsx"));

        await sut.LoadProjectAsync(5);

        var folder = Assert.IsType<ProjectFolderNodeVm>(Assert.Single(sut.RootFolders));
        var file = folder.Children.OfType<ProjectFileNodeVm>().Single(n => n.FileId == 100);
        Assert.True(file.HasPhysicalVersions);
        Assert.False(file.IsRequiredMissing);
        Assert.False(folder.HasRequiredMissing);
        Assert.True(folder.HasPhysicalFiles);
        Assert.True(sut.HasAllRequiredPhysicalFiles());
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
        Assert.Equal(RequiredMessage, sut.StatusMessage);
    }

    [Fact]
    public async Task CompleteFromTaskAsync_allows_PrepareQuoteCalculation_when_omdan_physical_exists()
    {
        var completion = new RecordingCompletion();
        var tree = CreateTree(
            BuildRequiredTree(),
            FakeFileStore.FileServerFile("(5)-9-7-1-1-Omdan.xlsx"));
        var sut = new ProjectWorkWindowViewModel(completion, tree: tree);
        var context = CreateCalcContext();

        Assert.True(await sut.ApplyContextAsync(context));
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
    public async Task Omdan_seed_is_idempotent_for_general_material_job_type()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new SiNetSQLDbContext(options);
        db.JobTypes.Add(new JobType { Id = 9, Title = "חומר כללי" });
        db.ProjectFolders.Add(new ProjectFolder { Id = 1, Title = "תיקיית הפרויקט" });
        db.ProjectFolders.Add(new ProjectFolder { Id = 2, Title = "תכתובת", Infolderid = 1 });
        db.ProjectFolders.Add(new ProjectFolder { Id = 3, Title = "ניהול כספי", Infolderid = 2 });
        await db.SaveChangesAsync();

        var first = await ProjectFileCatalogSeedData.EnsureAsync(db);
        var second = await ProjectFileCatalogSeedData.EnsureAsync(db);

        Assert.Contains("inserted", first, StringComparison.Ordinal);
        Assert.Contains("unchanged", second, StringComparison.Ordinal);

        Assert.Empty(await db.ProjectFolders.Where(f => f.Title == "הצעת מחיר").ToListAsync());
        var financeFolder = Assert.Single(await db.ProjectFolders.Where(f => f.Title == "ניהול כספי").ToListAsync());
        Assert.Equal(2, financeFolder.Infolderid);

        var rows = await db.ProjectFiles.Where(f => f.IsRequired).ToListAsync();
        Assert.Single(rows);
        Assert.Equal(9, rows[0].TypeProjId);
        Assert.Equal(financeFolder.Id, rows[0].Folderid);
        Assert.Equal(ProjectFileCatalogCodes.QuoteEstimate, rows[0].Code);
        Assert.Equal(ProjectFileRequiredOmdanSeedData.DisplayTitle, rows[0].Title);

        // Title rename must not break Code identity on second ensure.
        rows[0].Title = "שם מותאם";
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
        db.ProjectFolders.Add(new ProjectFolder { Id = 3, Title = "ניהול כספי", Infolderid = 2 });
        db.ProjectFiles.Add(new ProjectFile
        {
            Id = 1,
            Title = "אומדן הצעה",
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
        Assert.Equal("אומדן הצעה", omdan.Title);
        Assert.Equal(3, omdan.Folderid);
    }

    [Fact]
    public async Task Omdan_seed_skips_when_parent_folder_tachtuvet_missing_and_does_not_create_hatzaat_mechir()
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
        Assert.Empty(await db.ProjectFolders.Where(f => f.Title == "הצעת מחיר").ToListAsync());
        Assert.Empty(await db.ProjectFiles.Where(f => f.Code == ProjectFileCatalogCodes.QuoteEstimate).ToListAsync());
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

        var finance = await db.ProjectFolders.SingleAsync(f => f.Title == "ניהול כספי");
        Assert.Equal(2, finance.Infolderid);
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
        db.ProjectFolders.Add(new ProjectFolder { Id = 10, Title = "ניהול כספי", Infolderid = 2 });
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
        Assert.Equal(2, await db.ProjectFiles.CountAsync());
        Assert.NotNull(await db.ProjectFiles.SingleAsync(f => f.Id == 50 && f.Title == "קובץ אחר"));
        var omdan = await db.ProjectFiles.SingleAsync(f => f.Code == ProjectFileCatalogCodes.QuoteEstimate);
        Assert.Equal("אומדן הצעה", omdan.Title);
        Assert.True(omdan.IsRequired);
        Assert.Equal(10, omdan.Folderid);
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
