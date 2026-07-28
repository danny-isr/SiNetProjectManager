namespace SiNet.Application.MasterPlan.Reports;

/// <summary>Outcome of generating a MasterPlan Google Sheet report.</summary>
public sealed record MasterPlanReportGenerationResult(
    bool Success,
    string? SpreadsheetId,
    string? FileName,
    string? Url,
    int RowCount,
    string? Error)
{
    public static MasterPlanReportGenerationResult Ok(
        string spreadsheetId,
        string fileName,
        string? url,
        int rowCount)
        => new(true, spreadsheetId, fileName, url, rowCount, null);

    public static MasterPlanReportGenerationResult Fail(string error)
        => new(false, null, null, null, 0, error);
}
