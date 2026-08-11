namespace SiNet.Infrastructure.Google.Reports;

/// <summary>Configuration for creating a Google Sheets pivot table.</summary>
public sealed class PivotTableConfig
{
    public List<PivotFieldConfig> Rows { get; init; } = [];
    public List<PivotFieldConfig> Columns { get; init; } = [];
    public List<PivotValueConfig> Values { get; init; } = [];
    public List<PivotFieldConfig> Filters { get; init; } = [];
}

/// <summary>Row, column, or filter field in a pivot (0-based source column).</summary>
public sealed class PivotFieldConfig
{
    public required int SourceColumnIndex { get; init; }
    public bool ShowTotals { get; init; } = true;
}

/// <summary>Value aggregation in a pivot (SUM, MIN, MAX, …).</summary>
public sealed class PivotValueConfig
{
    public required int SourceColumnIndex { get; init; }
    public string SummarizeFunction { get; init; } = "SUM";
    public string? DisplayName { get; init; }
}

/// <summary>Result of <see cref="NativeGoogleSheetsWriter.CreatePivotTableAsync"/>.</summary>
public sealed class PivotTableResult
{
    public bool Success { get; set; }
    public int? PivotSheetId { get; set; }
    public string? PivotSheetName { get; set; }
    public int? SourceSheetId { get; set; }
    public List<string> Errors { get; } = [];
    public List<string> LogEntries { get; } = [];
}
