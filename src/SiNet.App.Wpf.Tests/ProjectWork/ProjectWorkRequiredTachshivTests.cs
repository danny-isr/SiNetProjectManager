using Moq;
using SiNet.App.Wpf.Surfaces.ProjectWork;
using SiNet.Application.ProjectWork;
using SiNet.Application.Tasks;
using SiNet.Application.WorkSurfaces;
using SiNet.Domain.Files;
using SiNet.Infrastructure.Sql.Services.SeedData;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;
using FileStorageDestination = SiNet.Domain.Files.FileStorageDestination;

namespace SiNet.App.Wpf.Tests.ProjectWork;

public sealed class ProjectWorkRequiredTachshivTests
{
    private const string RequiredMessage = "יש להעלות את קובץ התחשיב לפני סיום המשימה.";

    private static ProjectFileTreeDto BuildRequiredTree(bool isRequired = true) => new(
        ProjectId: 5,
        ProjectNumber: 5,
        RootFolders: new[]
        {
            new ProjectFolderDto(
                FolderId: 10,
                Name: "Docs",
                ParentFolderId: null,
                Children: Array.Empty<ProjectFolderDto>(),
                Files: new[]
                {
                    new ProjectFileDefinitionDto(
                        FileId: 100,
                        BaseName: "תחשיב",
                        Extension: ".xlsx",
                        StorageDestination: FileStorageDestination.FileServer,
                        FolderId: 10,
                        ProjectType: 3,
                        Number: 7,
                        TemplateLocation: null,
                        IsRequired: isRequired),
                }),
        });

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
        Assert.True(file.ShowAddFileButton);
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
            FakeFileStore.FileServerFile("(5)-3-7-1-1-Tachshiv.xlsx"));

        await sut.LoadProjectAsync(5);

        var folder = Assert.IsType<ProjectFolderNodeVm>(Assert.Single(sut.RootFolders));
        var file = folder.Children.OfType<ProjectFileNodeVm>().Single(n => n.FileId == 100);
        Assert.True(file.HasPhysicalVersions);
        Assert.False(file.IsRequiredMissing);
        Assert.False(file.ShowAddFileButton);
        Assert.False(folder.HasRequiredMissing);
        Assert.True(folder.HasPhysicalFiles);
        Assert.True(sut.HasAllRequiredPhysicalFiles());
    }

    [Fact]
    public async Task CompleteFromTaskAsync_blocks_PrepareQuoteCalculation_when_tachshiv_missing()
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
    public async Task CompleteFromTaskAsync_allows_PrepareQuoteCalculation_when_tachshiv_physical_exists()
    {
        var completion = new RecordingCompletion();
        var tree = CreateTree(
            BuildRequiredTree(),
            FakeFileStore.FileServerFile("(5)-3-7-1-1-Tachshiv.xlsx"));
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
        var tree = CreateTree(BuildRequiredTree()); // required missing, but task is not calc
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
    public async Task Tachshiv_seed_is_idempotent_by_type()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new SiNetSQLDbContext(options);
        db.JobTypes.Add(new JobType { Id = 3, Title = "בדיקה" });
        db.ProjectFolders.Add(new ProjectFolder { Id = 10, Title = "מסמכים" });
        db.TypeOfProjectInProjects.Add(new TypeOfProjectInProject
        {
            Id = 1,
            ProjectId = 5,
            ProjectTypeId = 3,
        });
        await db.SaveChangesAsync();

        var first = await ProjectFileRequiredTachshivSeedData.EnsureAsync(db);
        var second = await ProjectFileRequiredTachshivSeedData.EnsureAsync(db);

        Assert.Contains("inserted=", first, StringComparison.Ordinal);
        Assert.Contains("inserted=0", second, StringComparison.Ordinal);

        var rows = await db.ProjectFiles.Where(f => f.IsRequired).ToListAsync();
        Assert.Single(rows);
        Assert.Equal(3, rows[0].TypeProjId);
        Assert.True(ProjectFileRequiredTachshivSeedData.IsTachshivCatalogTitle(rows[0].Title));
        Assert.Equal(10, rows[0].Folderid);
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
