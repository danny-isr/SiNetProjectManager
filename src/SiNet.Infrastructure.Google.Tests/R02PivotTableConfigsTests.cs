using SiNet.Application.MasterPlan.Reports;
using SiNet.Infrastructure.Google.Reports;
using Xunit;

namespace SiNet.Infrastructure.Google.Tests;

public sealed class R02PivotTableConfigsTests
{
    [Fact]
    public void BuildSummary_internal_has_project_subcontract_sum_min_max_and_employee_filter()
    {
        var config = R02PivotTableConfigs.BuildSummary(isClientExport: false);

        Assert.Equal(
            [R02PivotTableConfigs.InternalProjectName, R02PivotTableConfigs.InternalSubContractName],
            config.Rows.Select(r => r.SourceColumnIndex).ToArray());
        Assert.Equal(5, config.Values.Count);
        Assert.Contains(config.Values, v => v is { SourceColumnIndex: R02PivotTableConfigs.InternalHours, SummarizeFunction: "SUM" });
        Assert.Contains(config.Values, v => v is { SourceColumnIndex: R02PivotTableConfigs.InternalDate, SummarizeFunction: "MIN" });
        Assert.Contains(config.Values, v => v is { SourceColumnIndex: R02PivotTableConfigs.InternalDate, SummarizeFunction: "MAX" });
        Assert.Contains(config.Values, v => v is { SourceColumnIndex: R02PivotTableConfigs.InternalHours, SummarizeFunction: "MIN" });
        Assert.Contains(config.Values, v => v is { SourceColumnIndex: R02PivotTableConfigs.InternalHours, SummarizeFunction: "MAX" });
        Assert.Single(config.Filters);
        Assert.Equal(R02PivotTableConfigs.InternalEmployeeName, config.Filters[0].SourceColumnIndex);
    }

    [Fact]
    public void BuildDetail_internal_lists_each_report_under_hierarchy()
    {
        var config = R02PivotTableConfigs.BuildDetail(isClientExport: false);

        Assert.Equal(
            [
                R02PivotTableConfigs.InternalProjectName,
                R02PivotTableConfigs.InternalSubContractName,
                R02PivotTableConfigs.InternalDate,
                R02PivotTableConfigs.InternalEmployeeName,
                R02PivotTableConfigs.InternalReportId,
                R02PivotTableConfigs.InternalDescription,
            ],
            config.Rows.Select(r => r.SourceColumnIndex).ToArray());
        Assert.Single(config.Values);
        Assert.Equal(R02PivotTableConfigs.InternalHours, config.Values[0].SourceColumnIndex);
        Assert.Equal("SUM", config.Values[0].SummarizeFunction);
        Assert.Empty(config.Filters);
    }

    [Fact]
    public void BuildDetail_client_omits_employee_and_report_id()
    {
        var config = R02PivotTableConfigs.BuildDetail(isClientExport: true);

        Assert.Equal(
            [
                R02PivotTableConfigs.ClientProjectName,
                R02PivotTableConfigs.ClientSubContractName,
                R02PivotTableConfigs.ClientDate,
                R02PivotTableConfigs.ClientStepName,
                R02PivotTableConfigs.ClientDescription,
            ],
            config.Rows.Select(r => r.SourceColumnIndex).ToArray());
        Assert.Equal(R02PivotTableConfigs.ClientHours, config.Values[0].SourceColumnIndex);
        Assert.Empty(R02PivotTableConfigs.BuildSummary(isClientExport: true).Filters);
    }

    [Fact]
    public void Column_indices_match_R02HoursRow_headers()
    {
        var internalHeaders = R02HoursRow.GetHeaderRow(false);
        Assert.Equal("שם פרויקט", internalHeaders[R02PivotTableConfigs.InternalProjectName]);
        Assert.Equal("שם תת-חוזה", internalHeaders[R02PivotTableConfigs.InternalSubContractName]);
        Assert.Equal("שעות (עשרוני)", internalHeaders[R02PivotTableConfigs.InternalHours]);
        Assert.Equal("תאריך", internalHeaders[R02PivotTableConfigs.InternalDate]);
        Assert.Equal("שם עובד", internalHeaders[R02PivotTableConfigs.InternalEmployeeName]);
        Assert.Equal("מזהה דיווח", internalHeaders[R02PivotTableConfigs.InternalReportId]);
        Assert.Equal("תיאור", internalHeaders[R02PivotTableConfigs.InternalDescription]);

        var clientHeaders = R02HoursRow.GetHeaderRow(true);
        Assert.Equal("שם פרויקט", clientHeaders[R02PivotTableConfigs.ClientProjectName]);
        Assert.Equal("שם תת-חוזה", clientHeaders[R02PivotTableConfigs.ClientSubContractName]);
        Assert.Equal("שעות (עשרוני)", clientHeaders[R02PivotTableConfigs.ClientHours]);
        Assert.Equal("תאריך", clientHeaders[R02PivotTableConfigs.ClientDate]);
        Assert.Equal("תיאור", clientHeaders[R02PivotTableConfigs.ClientDescription]);
    }
}
