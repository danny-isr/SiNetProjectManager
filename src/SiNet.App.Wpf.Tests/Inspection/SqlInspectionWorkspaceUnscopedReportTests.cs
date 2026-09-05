using Microsoft.EntityFrameworkCore;
using SiNet.Infrastructure.Sql.Services.Inspection;
using SiNetSQL.Data;
using SiNetSQL.Models;
using Xunit;

namespace SiNet.App.Wpf.Tests.Inspection;

public sealed class SqlInspectionWorkspaceUnscopedReportTests
{
    [Fact]
    public async Task GetReportsAsync_seriesId_zero_returns_null_series_reports()
    {
        var options = new DbContextOptionsBuilder<SiNetSQLDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var factory = new StubFactory(options);
        await using (var db = factory.CreateDbContext())
        {
            db.InspectionReports.AddRange(
                new InspectionReport
                {
                    ReportId = 4,
                    ProjectId = 136,
                    ReportNumber = 1,
                    SeriesId = null,
                    InspectionDate = DateTime.UtcNow,
                    InspectorName = "שירלי",
                },
                new InspectionReport
                {
                    ReportId = 5,
                    ProjectId = 136,
                    ReportNumber = 2,
                    SeriesId = 9,
                    InspectionDate = DateTime.UtcNow,
                    InspectorName = "אחר",
                });
            await db.SaveChangesAsync();
        }

        var sut = new SqlInspectionWorkspace(factory);
        var unscoped = await sut.GetReportsAsync(136, seriesId: 0);
        Assert.Single(unscoped);
        Assert.Equal(4, unscoped[0].ReportId);

        var series = await sut.GetReportsAsync(136, seriesId: 9);
        Assert.Single(series);
        Assert.Equal(5, series[0].ReportId);
    }

    private sealed class StubFactory(DbContextOptions<SiNetSQLDbContext> options)
        : IDbContextFactory<SiNetSQLDbContext>
    {
        public SiNetSQLDbContext CreateDbContext() => new(options);

        public Task<SiNetSQLDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
