namespace SiNet.Infrastructure.Google.Reports;

/// <summary>
/// Pivot configs for R02 hours report (column indices must stay aligned with
/// <c>R02HoursRow.GetHeaderRow</c> / <c>ToSheetRow</c>).
/// In R02, MasterPlan ProjectNum/Name = חוזה; SubContract = תת-חוזה.
/// </summary>
public static class R02PivotTableConfigs
{
    public const string SummarySheetName = "סיכום פרויקט-תת-חוזה";
    public const string DetailSheetName = "פירוט דיווחים";

    // Internal Data columns (0-based)
    internal const int InternalReportId = 0;
    internal const int InternalDate = 1;
    internal const int InternalHours = 4;
    internal const int InternalDescription = 5;
    internal const int InternalEmployeeName = 7;
    internal const int InternalProjectNum = 9;
    internal const int InternalProjectName = 10;
    internal const int InternalSubContractName = 15;

    // Client Data columns (0-based)
    internal const int ClientProjectNum = 0;
    internal const int ClientProjectName = 1;
    internal const int ClientSubContractName = 3;
    internal const int ClientStepName = 4;
    internal const int ClientDate = 5;
    internal const int ClientHours = 6;
    internal const int ClientDescription = 7;

    public static PivotTableConfig BuildSummary(bool isClientExport)
        => isClientExport ? BuildClientSummary() : BuildInternalSummary();

    public static PivotTableConfig BuildDetail(bool isClientExport)
        => isClientExport ? BuildClientDetail() : BuildInternalDetail();

    private static PivotTableConfig BuildInternalSummary() => new()
    {
        Rows =
        [
            new PivotFieldConfig { SourceColumnIndex = InternalProjectNum, ShowTotals = true },
            new PivotFieldConfig { SourceColumnIndex = InternalProjectName, ShowTotals = true },
            new PivotFieldConfig { SourceColumnIndex = InternalSubContractName, ShowTotals = true },
        ],
        Values =
        [
            new PivotValueConfig
            {
                SourceColumnIndex = InternalHours,
                SummarizeFunction = "SUM",
                DisplayName = "סה״כ שעות",
            },
            new PivotValueConfig
            {
                SourceColumnIndex = InternalDate,
                SummarizeFunction = "MIN",
                DisplayName = "תאריך מינ׳",
            },
            new PivotValueConfig
            {
                SourceColumnIndex = InternalDate,
                SummarizeFunction = "MAX",
                DisplayName = "תאריך מקס׳",
            },
            new PivotValueConfig
            {
                SourceColumnIndex = InternalHours,
                SummarizeFunction = "MIN",
                DisplayName = "מינ׳ שעות",
            },
            new PivotValueConfig
            {
                SourceColumnIndex = InternalHours,
                SummarizeFunction = "MAX",
                DisplayName = "מקס׳ שעות",
            },
        ],
        Filters =
        [
            new PivotFieldConfig { SourceColumnIndex = InternalProjectNum, ShowTotals = false },
            new PivotFieldConfig { SourceColumnIndex = InternalProjectName, ShowTotals = false },
            new PivotFieldConfig { SourceColumnIndex = InternalEmployeeName, ShowTotals = false },
        ],
    };

    private static PivotTableConfig BuildInternalDetail() => new()
    {
        Rows =
        [
            new PivotFieldConfig { SourceColumnIndex = InternalProjectNum, ShowTotals = true },
            new PivotFieldConfig { SourceColumnIndex = InternalProjectName, ShowTotals = true },
            new PivotFieldConfig { SourceColumnIndex = InternalSubContractName, ShowTotals = true },
            new PivotFieldConfig { SourceColumnIndex = InternalEmployeeName, ShowTotals = true },
            new PivotFieldConfig { SourceColumnIndex = InternalDate, ShowTotals = false },
            new PivotFieldConfig { SourceColumnIndex = InternalReportId, ShowTotals = false },
            new PivotFieldConfig { SourceColumnIndex = InternalDescription, ShowTotals = false },
        ],
        Values =
        [
            new PivotValueConfig
            {
                SourceColumnIndex = InternalHours,
                SummarizeFunction = "SUM",
                DisplayName = "שעות",
            },
        ],
        Filters =
        [
            new PivotFieldConfig { SourceColumnIndex = InternalProjectNum, ShowTotals = false },
            new PivotFieldConfig { SourceColumnIndex = InternalProjectName, ShowTotals = false },
        ],
    };

    private static PivotTableConfig BuildClientSummary() => new()
    {
        Rows =
        [
            new PivotFieldConfig { SourceColumnIndex = ClientProjectNum, ShowTotals = true },
            new PivotFieldConfig { SourceColumnIndex = ClientProjectName, ShowTotals = true },
            new PivotFieldConfig { SourceColumnIndex = ClientSubContractName, ShowTotals = true },
        ],
        Values =
        [
            new PivotValueConfig
            {
                SourceColumnIndex = ClientHours,
                SummarizeFunction = "SUM",
                DisplayName = "סה״כ שעות",
            },
            new PivotValueConfig
            {
                SourceColumnIndex = ClientDate,
                SummarizeFunction = "MIN",
                DisplayName = "תאריך מינ׳",
            },
            new PivotValueConfig
            {
                SourceColumnIndex = ClientDate,
                SummarizeFunction = "MAX",
                DisplayName = "תאריך מקס׳",
            },
            new PivotValueConfig
            {
                SourceColumnIndex = ClientHours,
                SummarizeFunction = "MIN",
                DisplayName = "מינ׳ שעות",
            },
            new PivotValueConfig
            {
                SourceColumnIndex = ClientHours,
                SummarizeFunction = "MAX",
                DisplayName = "מקס׳ שעות",
            },
        ],
        Filters =
        [
            new PivotFieldConfig { SourceColumnIndex = ClientProjectNum, ShowTotals = false },
            new PivotFieldConfig { SourceColumnIndex = ClientProjectName, ShowTotals = false },
        ],
    };

    private static PivotTableConfig BuildClientDetail() => new()
    {
        Rows =
        [
            new PivotFieldConfig { SourceColumnIndex = ClientProjectNum, ShowTotals = true },
            new PivotFieldConfig { SourceColumnIndex = ClientProjectName, ShowTotals = true },
            new PivotFieldConfig { SourceColumnIndex = ClientSubContractName, ShowTotals = true },
            new PivotFieldConfig { SourceColumnIndex = ClientDate, ShowTotals = false },
            new PivotFieldConfig { SourceColumnIndex = ClientStepName, ShowTotals = false },
            new PivotFieldConfig { SourceColumnIndex = ClientDescription, ShowTotals = false },
        ],
        Values =
        [
            new PivotValueConfig
            {
                SourceColumnIndex = ClientHours,
                SummarizeFunction = "SUM",
                DisplayName = "שעות",
            },
        ],
        Filters =
        [
            new PivotFieldConfig { SourceColumnIndex = ClientProjectNum, ShowTotals = false },
            new PivotFieldConfig { SourceColumnIndex = ClientProjectName, ShowTotals = false },
        ],
    };
}
