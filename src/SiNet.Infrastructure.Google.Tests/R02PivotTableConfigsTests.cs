using SiNet.Application.MasterPlan.Reports;
using SiNet.Infrastructure.Google.Reports;
using Xunit;

namespace SiNet.Infrastructure.Google.Tests;

public sealed class R02PivotTableConfigsTests
{
    [Fact]
    public void BuildSummary_internal_rows_contract_then_subcontract_with_contract_and_employee_filters()
    {
        var config = R02PivotTableConfigs.BuildSummary(isClientExport: false);

        Assert.Equal(
            [
                R02PivotTableConfigs.InternalProjectNum,
                R02PivotTableConfigs.InternalProjectName,
                R02PivotTableConfigs.InternalSubContractName,
            ],
            config.Rows.Select(r => r.SourceColumnIndex).ToArray());
        Assert.Equal(5, config.Values.Count);
        Assert.Contains(config.Values, v => v is { SourceColumnIndex: R02PivotTableConfigs.InternalHours, SummarizeFunction: "SUM" });
        Assert.Contains(config.Values, v => v is { SourceColumnIndex: R02PivotTableConfigs.InternalDate, SummarizeFunction: "MIN" });
        Assert.Contains(config.Values, v => v is { SourceColumnIndex: R02PivotTableConfigs.InternalDate, SummarizeFunction: "MAX" });
        Assert.Contains(config.Values, v => v is { SourceColumnIndex: R02PivotTableConfigs.InternalHours, SummarizeFunction: "MIN" });
        Assert.Contains(config.Values, v => v is { SourceColumnIndex: R02PivotTableConfigs.InternalHours, SummarizeFunction: "MAX" });
        Assert.Equal(
            [
                R02PivotTableConfigs.InternalProjectNum,
                R02PivotTableConfigs.InternalProjectName,
                R02PivotTableConfigs.InternalEmployeeName,
            ],
            config.Filters.Select(f => f.SourceColumnIndex).ToArray());
    }

    [Fact]
    public void BuildDetail_internal_contract_then_employee_then_date_with_contract_filters()
    {
        var config = R02PivotTableConfigs.BuildDetail(isClientExport: false);

        Assert.Equal(
            [
                R02PivotTableConfigs.InternalProjectNum,
                R02PivotTableConfigs.InternalProjectName,
                R02PivotTableConfigs.InternalSubContractName,
                R02PivotTableConfigs.InternalEmployeeName,
                R02PivotTableConfigs.InternalDate,
                R02PivotTableConfigs.InternalReportId,
                R02PivotTableConfigs.InternalDescription,
            ],
            config.Rows.Select(r => r.SourceColumnIndex).ToArray());
        Assert.Single(config.Values);
        Assert.Equal(R02PivotTableConfigs.InternalHours, config.Values[0].SourceColumnIndex);
        Assert.Equal("SUM", config.Values[0].SummarizeFunction);
        Assert.Equal(
            [
                R02PivotTableConfigs.InternalProjectNum,
                R02PivotTableConfigs.InternalProjectName,
            ],
            config.Filters.Select(f => f.SourceColumnIndex).ToArray());
    }

    [Fact]
    public void BuildDetail_client_contract_then_date_with_contract_filters()
    {
        var config = R02PivotTableConfigs.BuildDetail(isClientExport: true);

        Assert.Equal(
            [
                R02PivotTableConfigs.ClientProjectNum,
                R02PivotTableConfigs.ClientProjectName,
                R02PivotTableConfigs.ClientSubContractName,
                R02PivotTableConfigs.ClientDate,
                R02PivotTableConfigs.ClientStepName,
                R02PivotTableConfigs.ClientDescription,
            ],
            config.Rows.Select(r => r.SourceColumnIndex).ToArray());
        Assert.Equal(R02PivotTableConfigs.ClientHours, config.Values[0].SourceColumnIndex);
        Assert.Equal(
            [
                R02PivotTableConfigs.ClientProjectNum,
                R02PivotTableConfigs.ClientProjectName,
            ],
            config.Filters.Select(f => f.SourceColumnIndex).ToArray());

        var summary = R02PivotTableConfigs.BuildSummary(isClientExport: true);
        Assert.Equal(
            [
                R02PivotTableConfigs.ClientProjectNum,
                R02PivotTableConfigs.ClientProjectName,
                R02PivotTableConfigs.ClientSubContractName,
            ],
            summary.Rows.Select(r => r.SourceColumnIndex).ToArray());
        Assert.Equal(
            [
                R02PivotTableConfigs.ClientProjectNum,
                R02PivotTableConfigs.ClientProjectName,
            ],
            summary.Filters.Select(f => f.SourceColumnIndex).ToArray());
    }

    [Fact]
    public void Column_indices_match_R02HoursRow_headers()
    {
        var internalHeaders = R02HoursRow.GetHeaderRow(false);
        Assert.Equal("מספר פרויקט", internalHeaders[R02PivotTableConfigs.InternalProjectNum]);
        Assert.Equal("שם פרויקט", internalHeaders[R02PivotTableConfigs.InternalProjectName]);
        Assert.Equal("שם תת-חוזה", internalHeaders[R02PivotTableConfigs.InternalSubContractName]);
        Assert.Equal("שעות (עשרוני)", internalHeaders[R02PivotTableConfigs.InternalHours]);
        Assert.Equal("תאריך", internalHeaders[R02PivotTableConfigs.InternalDate]);
        Assert.Equal("שם עובד", internalHeaders[R02PivotTableConfigs.InternalEmployeeName]);
        Assert.Equal("מזהה דיווח", internalHeaders[R02PivotTableConfigs.InternalReportId]);
        Assert.Equal("תיאור", internalHeaders[R02PivotTableConfigs.InternalDescription]);

        var clientHeaders = R02HoursRow.GetHeaderRow(true);
        Assert.Equal("מספר פרויקט", clientHeaders[R02PivotTableConfigs.ClientProjectNum]);
        Assert.Equal("שם פרויקט", clientHeaders[R02PivotTableConfigs.ClientProjectName]);
        Assert.Equal("שם תת-חוזה", clientHeaders[R02PivotTableConfigs.ClientSubContractName]);
        Assert.Equal("שעות (עשרוני)", clientHeaders[R02PivotTableConfigs.ClientHours]);
        Assert.Equal("תאריך", clientHeaders[R02PivotTableConfigs.ClientDate]);
        Assert.Equal("תיאור", clientHeaders[R02PivotTableConfigs.ClientDescription]);
    }
}
