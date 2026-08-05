using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Settings;
using SiNet.Infrastructure.Sql;
using SiNet.Infrastructure.Sql.Services.Ai;
using SiNet.Infrastructure.Sql.Services.Inspection;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Inspection;

public sealed class SqlInspectionWorkspaceTests
{
    [Fact]
    public async Task Workspace_loads_series_reports_notes_and_tree()
    {
        var factory = await SeedAsync();
        var sut = new SqlInspectionWorkspace(factory);

        var series = await sut.GetSeriesAsync(10);
        Assert.Single(series);
        Assert.Equal("סדרת בדיקה", series[0].Name);

        var reports = await sut.GetReportsAsync(10, series[0].SeriesId);
        Assert.Single(reports);
        Assert.Equal(1, reports[0].ReportNumber);

        var detail = await sut.GetReportDetailAsync(reports[0].ReportId);
        Assert.NotNull(detail);
        Assert.Equal("1", detail!.ReviewedVersion);

        var notes = await sut.GetNotesAsync(reports[0].ReportId);
        Assert.Equal(3, notes.Count);

        var tree = await sut.GetQuestionnaireTreeAsync(reports[0].ReportId);
        Assert.Single(tree);
        Assert.Equal(1, tree[0].ChapterNumber);
        Assert.Single(tree[0].Sections);
        Assert.Single(tree[0].Sections[0].Notes);
        Assert.Equal("1.1.1", tree[0].Sections[0].Notes[0].Number);

        var general = await sut.GetGeneralFieldsAsync(reports[0].ReportId);
        Assert.Single(general);
        Assert.Equal("שם פרויקט", general[0].Label);
        Assert.False(general[0].IsManualOverride);

        var drawings = await sut.GetDrawingsAsync(reports[0].ReportId);
        Assert.Single(drawings);

        var reviewed = await sut.GetReviewedFilesAsync(reports[0].ReportId);
        Assert.Single(reviewed);
    }

    [Fact]
    public async Task Note_command_saves_text()
    {
        var factory = await SeedAsync();
        var notes = new SqlInspectionNoteCommandService(factory);
        var workspace = new SqlInspectionWorkspace(factory);
        var reportNotes = await workspace.GetNotesAsync(1);
        var noteId = reportNotes[0].NoteId;

        var result = await notes.SaveNoteTextAsync(noteId, "טקסט מעודכן");
        Assert.True(result.Succeeded);

        var reloaded = await workspace.GetNotesAsync(1);
        Assert.Equal("טקסט מעודכן", reloaded[0].Text);
    }

    [Fact]
    public void AddSiNetInspectionSql_registers_ports()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<SiNetSQLDbContext>>(new StubDbFactory(options));
        services.AddSiNetInspectionSql();

        using var sp = services.BuildServiceProvider();
        Assert.IsType<SqlInspectionWorkspace>(sp.GetRequiredService<IInspectionWorkspace>());
        Assert.NotNull(sp.GetRequiredService<IInspectionNoteCommandService>());
        Assert.NotNull(sp.GetRequiredService<IInspectionReportCommandService>());
        Assert.NotNull(sp.GetRequiredService<IInspectionDrawingCommandService>());
        Assert.NotNull(sp.GetRequiredService<IInspectionReportExportPort>());
    }

    [Fact]
    public async Task Ai_reviewer_fails_gracefully_when_ollama_unavailable()
    {
        var settings = new StubSettings();
        var sut = new OllamaInspectionNoteAiReviewer(settings);
        var result = await sut.ReviewAsync("טקסט לבדיקה");
        Assert.True(result.HasError);
        Assert.Equal("טקסט לבדיקה", result.OriginalText);
    }

    private static async Task<IDbContextFactory<SiNetSQLDbContext>> SeedAsync()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new SiNetSQLDbContext(options);

        var chapterName = new ChapterName { Id = 1, Name = "כללי" };
        var sectionName = new SectionName { Id = 1, Name = "פרטי פרויקט" };
        db.ChapterNames.Add(chapterName);
        db.SectionNames.Add(sectionName);

        var series = new InspectionSeries
        {
            SeriesId = 1,
            ProjectId = 10,
            SeriesName = "סדרת בדיקה",
            Created = DateTime.UtcNow,
            Modified = DateTime.UtcNow,
        };
        db.InspectionSeries.Add(series);

        var chapter = new Chapter
        {
            ChapterId = 1,
            SeriesId = 1,
            ChapterNumber = 1,
            ChapterNameId = 1,
            ChapterName = chapterName,
        };
        db.Chapters.Add(chapter);

        var section = new Section
        {
            SectionId = 1,
            ChapterId = 1,
            SectionCode = 1,
            SectionNameId = 1,
            SectionName = sectionName,
            Chapter = chapter,
            IsActive = true,
        };
        db.Sections.Add(section);

        var report = new InspectionReport
        {
            ReportId = 1,
            ProjectId = 10,
            SeriesId = 1,
            ReportNumber = 1,
            InspectionDate = new DateTime(2026, 7, 1),
            InspectorName = "בודק",
            ReviewedVersion = "1",
        };
        db.InspectionReports.Add(report);

        db.InspectionNotes.Add(new InspectionNote
        {
            NoteId = 1,
            ReportId = 1,
            SectionId = 1,
            NoteSubIndex = "1.1.1",
            NoteText = "הערה ראשונה",
            NoteStatus = "Failed",
            Section = section,
        });

        // Chapter 0 general field
        var generalChapterName = new ChapterName { Id = 2, Name = "נתונים כלליים" };
        var generalSectionName = new SectionName { Id = 2, Name = "שם פרויקט" };
        db.ChapterNames.Add(generalChapterName);
        db.SectionNames.Add(generalSectionName);

        var generalChapter = new Chapter
        {
            ChapterId = 2,
            SeriesId = 1,
            ChapterNumber = 0,
            ChapterNameId = 2,
            ChapterName = generalChapterName,
        };
        db.Chapters.Add(generalChapter);

        var generalSection = new Section
        {
            SectionId = 2,
            ChapterId = 2,
            SectionCode = 1,
            SectionNameId = 2,
            SectionName = generalSectionName,
            Chapter = generalChapter,
            IsActive = true,
        };
        db.Sections.Add(generalSection);

        db.InspectionNotes.Add(new InspectionNote
        {
            NoteId = 2,
            ReportId = 1,
            SectionId = 2,
            NoteSubIndex = "1",
            NoteText = null,
            NoteStatus = null,
            Section = generalSection,
        });

        // Section-level placeholder (should be filtered from numbered tree)
        db.InspectionNotes.Add(new InspectionNote
        {
            NoteId = 3,
            ReportId = 1,
            SectionId = 1,
            NoteSubIndex = "1.1",
            NoteText = "placeholder",
            NoteStatus = null,
            Section = section,
        });

        db.InspectionReportDrawings.Add(new InspectionReportDrawing
        {
            Id = 1,
            ReportId = 1,
            FileName = "plan.dwf",
            SourceFilePath = @"C:\temp\plan.dwf",
            FileType = DrawingFileType.Dwf,
        });

        db.InspectionReportReviewedFiles.Add(new InspectionReportReviewedFile
        {
            Id = 1,
            ReportId = 1,
            FileName = "plan.dwf",
            SortOrder = 0,
        });

        await db.SaveChangesAsync();
        return new StubDbFactory(options);
    }

    private sealed class StubDbFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);

        public Task<SiNetSQLDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class StubSettings : ISystemSettingsQueryService
    {
        public Task<SystemSettingsDto> GetSystemSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SystemSettingsDto(
                new EmailOfficeSystemSettingsDto(
                    SystemSettingsDefaults.DefaultProjectTitle,
                    SystemSettingsDefaults.OfficeManagementProjectId,
                    SystemSettingsDefaults.HourPriceDefault,
                    SystemSettingsDefaults.InboxFolderNameFallback,
                    null,
                    10),
                new AccSystemSettingsDto(string.Empty, string.Empty, string.Empty, string.Empty, SystemSettingsDefaults.AccManualUploadAllowedExtensions),
                new InspectionSystemSettingsDto(string.Empty, string.Empty, string.Empty, string.Empty),
                new InspectionStatusLabelsDto(
                    SystemSettingsDefaults.StatusLabelPassed,
                    SystemSettingsDefaults.StatusLabelFailed,
                    SystemSettingsDefaults.StatusLabelRecurringFailed,
                    SystemSettingsDefaults.StatusLabelNotApplicable),
                new AiSystemSettingsDto(
                    "http://127.0.0.1:9",
                    "test-model",
                    new AiModelLevelSelectionDto("test-model", "Ollama"),
                    new AiModelLevelSelectionDto("test-model", "Ollama"),
                    new AiModelLevelSelectionDto("test-model", "Ollama"),
                    new AiModelLevelSelectionDto("test-model", "Ollama"),
                    string.Empty),
                new CentralLoggingSettingsDto(
                    null,
                    14,
                    90,
                    new AppLogLevelsDto(LogLevelDto.Information, LogLevelDto.Warning),
                    new AppLogLevelsDto(LogLevelDto.Information, LogLevelDto.Warning),
                    new AppLogLevelsDto(LogLevelDto.Information, LogLevelDto.Warning),
                    false),
                new WorkflowSystemSettingsDto(SystemSettingsDefaults.WorkflowMaxOpenChildInstances),
                new ProjectWorkSystemSettingsDto(SystemSettingsDefaults.ProjectWorkScanExclusionRules),
                SystemSettingsDefaults.Diagnostics));
    }
}
