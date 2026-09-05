using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Infrastructure.Sql.Services.Inspection;
using SiNetSQL.Data;
using SiNetSQL.Models;
using SiNetSQL.Services.InspectionSync;
using Xunit;

namespace SiNet.App.Wpf.Tests.Inspection;

/// <summary>
/// Behavioral regression for template → scan → sync → notes create/hydrate fail-closed contract.
/// </summary>
public sealed class InspectionTemplateCreatePipelineTests
{
    private static IReadOnlyList<IReadOnlyList<object?>> ValidSheetRows() =>
    [
        ["header"],
        [
            "<<1.1 כותרת פרק א [שאלה ראשונה]>>",
            "<<1.1 $>>",
            $"<<{TemplateTagValidator.PlannerResponseTagLabel}>>",
        ],
        [
            "<<שם פרויקט>>",
        ],
    ];

    [Fact]
    public async Task Valid_template_creates_report_with_chapters_sections_and_notes()
    {
        var (factory, reader) = await CreateHarnessAsync(ValidSheetRows());
        var sut = new SqlInspectionReportCommandService(factory, new TemplateSyncService(factory), reader);

        var result = await sut.CreateReportAsync(
            projectId: 100,
            templateUrl: "https://docs.google.com/spreadsheets/d/sheet-A/edit",
            spreadsheetId: "sheet-A");

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.True(result.ReportId is > 0);

        await using var db = await factory.CreateDbContextAsync();
        var report = await db.InspectionReports.SingleAsync(r => r.ReportId == result.ReportId);
        Assert.Equal(100, report.ProjectId);
        Assert.NotNull(report.SeriesId);

        var series = await db.InspectionSeries.SingleAsync(s => s.SeriesId == report.SeriesId);
        Assert.Equal("sheet-A", series.TemplateSpreadsheetId);

        var chapters = await db.Chapters.CountAsync(c => c.SeriesId == report.SeriesId);
        var sections = await db.Sections.CountAsync(s => s.IsActive && s.Chapter!.SeriesId == report.SeriesId);
        var notes = await db.InspectionNotes.CountAsync(n => n.ReportId == report.ReportId);

        Assert.True(chapters > 0);
        Assert.True(sections > 0);
        Assert.True(notes > 0);
        Assert.Equal(sections, notes);
    }

    [Fact]
    public async Task Zero_sync_rows_does_not_create_report()
    {
        // Planner-response tag alone validates, but is filtered out of SyncRows → 0 rows.
        IReadOnlyList<IReadOnlyList<object?>> plannerOnly =
        [
            [$"<<{TemplateTagValidator.PlannerResponseTagLabel}>>"],
        ];
        var (factory, reader) = await CreateHarnessAsync(plannerOnly);
        var sut = new SqlInspectionReportCommandService(factory, new TemplateSyncService(factory), reader);

        var result = await sut.CreateReportAsync(
            projectId: 100,
            templateUrl: "https://docs.google.com/spreadsheets/d/sheet-empty/edit",
            spreadsheetId: "sheet-empty");

        Assert.False(result.Succeeded);
        Assert.Contains("לא נמצאו סעיפים תקינים", result.ErrorMessage);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(0, await db.InspectionReports.CountAsync());
        Assert.Equal(0, await db.InspectionNotes.CountAsync());
    }

    [Fact]
    public async Task Validation_error_does_not_create_report()
    {
        // Numbered tags without planner-response tag → validation fail.
        IReadOnlyList<IReadOnlyList<object?>> bad =
        [
            ["<<1.1 כותרת [טקסט]>>", "<<1.1 $>>"],
        ];
        var (factory, reader) = await CreateHarnessAsync(bad);
        var sut = new SqlInspectionReportCommandService(factory, new TemplateSyncService(factory), reader);

        var result = await sut.CreateReportAsync(
            projectId: 100,
            templateUrl: "https://docs.google.com/spreadsheets/d/sheet-bad/edit",
            spreadsheetId: "sheet-bad");

        Assert.False(result.Succeeded);
        Assert.Contains("שגיאות בתבנית", result.ErrorMessage);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(0, await db.InspectionReports.CountAsync());
    }

    [Fact]
    public async Task Sheet_read_failure_does_not_create_report()
    {
        var options = NewDb();
        var factory = new StubDbFactory(options);
        var reader = new StubSheetReader(InspectionTemplateSheetReadResult.Fail("קריאת התבנית נכשלה — בדיקה."));
        var sut = new SqlInspectionReportCommandService(factory, new TemplateSyncService(factory), reader);

        var result = await sut.CreateReportAsync(
            projectId: 100,
            templateUrl: "https://docs.google.com/spreadsheets/d/sheet-x/edit",
            spreadsheetId: "sheet-x");

        Assert.False(result.Succeeded);
        Assert.Contains("קריאת התבנית נכשלה", result.ErrorMessage);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(0, await db.InspectionReports.CountAsync());
    }

    [Fact]
    public async Task Native_empty_template_url_is_rejected()
    {
        var options = NewDb();
        var factory = new StubDbFactory(options);
        var reader = new StubSheetReader(InspectionTemplateSheetReadResult.Ok(ValidSheetRows()));
        var sut = new SqlInspectionReportCommandService(factory, new TemplateSyncService(factory), reader);

        var result = await sut.CreateReportAsync(
            projectId: 100,
            templateUrl: "native://empty-template",
            spreadsheetId: "ignored");

        Assert.False(result.Succeeded);
        Assert.Contains("תבנית Google תקינה", result.ErrorMessage);
    }

    [Fact]
    public async Task Template_B_does_not_reuse_series_of_template_A()
    {
        var options = NewDb();
        var factory = new StubDbFactory(options);
        var sync = new TemplateSyncService(factory);

        // Pre-seed Series A for project 100
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.InspectionSeries.Add(new InspectionSeries
            {
                ProjectId = 100,
                TemplateSpreadsheetId = "sheet-A",
                TemplateUrl = "https://docs.google.com/spreadsheets/d/sheet-A/edit",
                Created = DateTime.UtcNow,
                Modified = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var reader = new StubSheetReader(InspectionTemplateSheetReadResult.Ok(ValidSheetRows()));
        var sut = new SqlInspectionReportCommandService(factory, sync, reader);

        var result = await sut.CreateReportAsync(
            projectId: 100,
            templateUrl: "https://docs.google.com/spreadsheets/d/sheet-B/edit",
            spreadsheetId: "sheet-B");

        Assert.True(result.Succeeded, result.ErrorMessage);
        await using var verify = await factory.CreateDbContextAsync();
        var report = await verify.InspectionReports.SingleAsync(r => r.ReportId == result.ReportId);
        var series = await verify.InspectionSeries.SingleAsync(s => s.SeriesId == report.SeriesId);
        Assert.Equal("sheet-B", series.TemplateSpreadsheetId);
        Assert.Equal(2, await verify.InspectionSeries.CountAsync(s => s.ProjectId == 100));
    }

    [Fact]
    public async Task Same_template_reuses_existing_series()
    {
        var (factory, reader) = await CreateHarnessAsync(ValidSheetRows());
        var sut = new SqlInspectionReportCommandService(factory, new TemplateSyncService(factory), reader);

        var first = await sut.CreateReportAsync(
            projectId: 100,
            templateUrl: "https://docs.google.com/spreadsheets/d/sheet-A/edit",
            spreadsheetId: "sheet-A");
        var second = await sut.CreateReportAsync(
            projectId: 100,
            templateUrl: "https://docs.google.com/spreadsheets/d/sheet-A/edit",
            spreadsheetId: "sheet-A");

        Assert.True(first.Succeeded && second.Succeeded);
        await using var db = await factory.CreateDbContextAsync();
        var r1 = await db.InspectionReports.SingleAsync(r => r.ReportId == first.ReportId);
        var r2 = await db.InspectionReports.SingleAsync(r => r.ReportId == second.ReportId);
        Assert.Equal(r1.SeriesId, r2.SeriesId);
        Assert.Equal(1, await db.InspectionSeries.CountAsync(s => s.ProjectId == 100));
    }

    [Fact]
    public async Task Two_templates_keep_reports_series_scoped()
    {
        var (factory, reader) = await CreateHarnessAsync(ValidSheetRows());
        var sut = new SqlInspectionReportCommandService(factory, new TemplateSyncService(factory), reader);

        var a = await sut.CreateReportAsync(
            projectId: 100,
            templateUrl: "https://docs.google.com/spreadsheets/d/sheet-A/edit",
            spreadsheetId: "sheet-A");
        var b = await sut.CreateReportAsync(
            projectId: 100,
            templateUrl: "https://docs.google.com/spreadsheets/d/sheet-B/edit",
            spreadsheetId: "sheet-B");

        Assert.True(a.Succeeded && b.Succeeded);
        await using var db = await factory.CreateDbContextAsync();
        var ra = await db.InspectionReports.SingleAsync(r => r.ReportId == a.ReportId);
        var rb = await db.InspectionReports.SingleAsync(r => r.ReportId == b.ReportId);
        Assert.NotEqual(ra.SeriesId, rb.SeriesId);
    }

    [Fact]
    public async Task Hydrate_empty_unsent_report_populates_notes()
    {
        var (factory, reader) = await CreateHarnessAsync(ValidSheetRows());
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.InspectionReports.Add(new InspectionReport
            {
                ReportId = 4,
                ProjectId = 100,
                SeriesId = null,
                ReportNumber = 1,
                InspectionDate = DateTime.UtcNow,
                SourceFileUrn = "https://docs.google.com/spreadsheets/d/sheet-hydrate/edit",
                IsLockedAfterSend = false,
            });
            await db.SaveChangesAsync();
        }

        var sut = new SqlInspectionReportCommandService(factory, new TemplateSyncService(factory), reader);
        var result = await sut.HydrateEmptyReportFromTemplateAsync(4);

        Assert.True(result.Succeeded, result.ErrorMessage);
        await using var verify = await factory.CreateDbContextAsync();
        var report = await verify.InspectionReports.SingleAsync(r => r.ReportId == 4);
        Assert.NotNull(report.SeriesId);
        var notes = await verify.InspectionNotes.CountAsync(n => n.ReportId == 4);
        Assert.True(notes > 0);
    }

    [Fact]
    public async Task Hydrate_twice_does_not_duplicate_notes()
    {
        var (factory, reader) = await CreateHarnessAsync(ValidSheetRows());
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.InspectionReports.Add(new InspectionReport
            {
                ReportId = 4,
                ProjectId = 100,
                SeriesId = null,
                ReportNumber = 1,
                InspectionDate = DateTime.UtcNow,
                SourceFileUrn = "https://docs.google.com/spreadsheets/d/sheet-hydrate/edit",
                IsLockedAfterSend = false,
            });
            await db.SaveChangesAsync();
        }

        var sut = new SqlInspectionReportCommandService(factory, new TemplateSyncService(factory), reader);
        Assert.True((await sut.HydrateEmptyReportFromTemplateAsync(4)).Succeeded);
        await using var mid = await factory.CreateDbContextAsync();
        var count1 = await mid.InspectionNotes.CountAsync(n => n.ReportId == 4);

        Assert.True((await sut.HydrateEmptyReportFromTemplateAsync(4)).Succeeded);
        await using var end = await factory.CreateDbContextAsync();
        var count2 = await end.InspectionNotes.CountAsync(n => n.ReportId == 4);
        Assert.Equal(count1, count2);
    }

    [Fact]
    public async Task Hydrate_refuses_sent_or_locked_report()
    {
        var (factory, reader) = await CreateHarnessAsync(ValidSheetRows());
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.InspectionReports.Add(new InspectionReport
            {
                ReportId = 9,
                ProjectId = 100,
                ReportNumber = 1,
                InspectionDate = DateTime.UtcNow,
                SourceFileUrn = "https://docs.google.com/spreadsheets/d/sheet-x/edit",
                SentAt = DateTime.UtcNow,
                IsLockedAfterSend = true,
            });
            await db.SaveChangesAsync();
        }

        var sut = new SqlInspectionReportCommandService(factory, new TemplateSyncService(factory), reader);
        var result = await sut.HydrateEmptyReportFromTemplateAsync(9);
        Assert.False(result.Succeeded);
        Assert.Contains("נשלח או ננעל", result.ErrorMessage);
    }

    private static DbContextOptions<SiNetSQLDbContext> NewDb() =>
        new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static async Task<(StubDbFactory Factory, StubSheetReader Reader)> CreateHarnessAsync(
        IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        var options = NewDb();
        var factory = new StubDbFactory(options);
        // Ensure DB exists
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        return (factory, new StubSheetReader(InspectionTemplateSheetReadResult.Ok(rows)));
    }

    private sealed class StubSheetReader(InspectionTemplateSheetReadResult result) : IInspectionTemplateSheetReader
    {
        public Task<InspectionTemplateSheetReadResult> ReadFirstSheetAsync(
            string spreadsheetId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class StubDbFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);
        public Task<SiNetSQLDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
