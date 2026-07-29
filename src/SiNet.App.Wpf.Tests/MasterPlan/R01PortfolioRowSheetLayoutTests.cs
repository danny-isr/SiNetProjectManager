using SiNet.Application.MasterPlan.Reports;
using SiNet.Infrastructure.Sql.Services.MasterPlan.Reports;
using Xunit;

namespace SiNet.App.Wpf.Tests.MasterPlan;

public sealed class R01PortfolioRowSheetLayoutTests
{
    [Fact]
    public void WhenToSheetRowThenHeaderAndValuesHaveMatchingColumnCounts()
    {
        var headers = R01PortfolioRow.GetHeaderRow();
        var row = new R01PortfolioRow(
            ProjectId: 1,
            ProjectNum: "P-1",
            ProjectName: "Test",
            IsActive: true,
            StartDate: new DateTime(2024, 1, 1),
            EndDate: null,
            StatusId: 2,
            StatusName: "פעיל",
            CustomerId: 3,
            CustomerName: "לקוח",
            FeeSum: 1000m,
            OpenBillSum: 0m,
            ApprovedBillSum: 500m,
            Balance: 500m,
            LastBillDate: null,
            HourReported: 10m,
            HourAllotted: 20m,
            ProgressPercentage: 50m,
            LastUpdated: DateTime.UtcNow,
            DataSource: "MasterPlan");

        var values = row.ToSheetRow(3);

        Assert.Equal(headers.Count, values.Count);
        Assert.True(headers.Count >= 30, "Expected full legacy column set, not the truncated 9-column stub.");
        Assert.Contains("מאזן", headers.Cast<string>());
        Assert.Contains("שעות מדווחות", headers.Cast<string>());
        Assert.Equal("=IFERROR(K3/Parameters!$B$1,\"\")", values[17]);
    }

    [Fact]
    public void WhenReplicaTotalHoursIsTimeSpanThenConvertHoursRawReturnsDecimalHours()
    {
        var converted = SqlR02ReportDataSource.ConvertHoursRaw(TimeSpan.FromHours(2.5));
        Assert.Equal(2.5m, converted);
    }
}
