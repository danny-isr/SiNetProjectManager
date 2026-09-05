using Microsoft.EntityFrameworkCore;
using SiNet.Infrastructure.Google.Inspection;
using SiNetSQL.Data;
using SiNetSQL.Models;
using SiNetSQL.Services.InspectionSync;
using Xunit;

namespace SiNet.App.Wpf.Tests.Inspection;

public sealed class GoogleSheetsInspectionReportExportPortTests
{
    [Fact]
    public async Task WhenExportSucceedsThenOnlyArtifactIdentityIsPersisted()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var factory = new StubDbContextFactory(options);

        await using (var seed = factory.CreateDbContext())
        {
            seed.InspectionReports.Add(new InspectionReport
            {
                ReportId = 17,
                ProjectId = 136,
                ReportNumber = 1,
                InspectionDate = new DateTime(2026, 9, 5),
                SourceFileUrn = "template-id",
            });
            await seed.SaveChangesAsync();
        }

        var sut = new GoogleSheetsInspectionReportExportPort(
            new SuccessfulExportService(),
            factory);

        var result = await sut.ExportAsync(17);

        await using var verify = factory.CreateDbContext();
        var report = await verify.InspectionReports.SingleAsync(r => r.ReportId == 17);
        Assert.Equal(
            (true, "export-id", "https://docs.google.com/spreadsheets/d/export-id", null, false),
            (result.Succeeded, report.SentSpreadsheetId, report.SentSpreadsheetUrl, report.SentAt, report.IsLockedAfterSend));
    }

    private sealed class SuccessfulExportService : IInspectionGoogleReportExportService
    {
        public Task<GoogleInspectionExportResult> ExportReportAsync(
            int reportId,
            string templateSpreadsheetId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GoogleInspectionExportResult
            {
                IsSuccess = true,
                DestinationSpreadsheetId = "export-id",
                DestinationUrl = "https://docs.google.com/spreadsheets/d/export-id",
            });

        public Task<IReadOnlyList<TemplateScanTag>> ScanTemplateAsync(
            string templateSpreadsheetId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TemplateScanTag>>([]);

        public Task<GoogleAnyoneWithLinkShareResult> ShareReportAnyoneWithLinkAsync(
            string spreadsheetId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GoogleAnyoneWithLinkShareResult { IsSuccess = true });
    }

    private sealed class StubDbContextFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);
    }
}
