namespace SiNet.Application.MasterPlan.Reports;

/// <summary>One day row for the in-app R03 DataGrid (no Google).</summary>
public sealed record R03DailyPreviewRow(
    int EmployeeId,
    string EmployeeName,
    DateTime Date,
    string DayName,
    decimal AttendanceHours,
    decimal ReportedHours)
{
    public decimal Difference => Math.Round(ReportedHours - AttendanceHours, 2);

    public bool IsNegativeDifference => Difference < 0;
}

/// <summary>Result of <see cref="IMasterPlanR03ReportService.PreviewAsync"/>.</summary>
public sealed record R03PreviewResult(
    bool Success,
    string? Error,
    IReadOnlyList<R03DailyPreviewRow> Rows,
    decimal TotalAttendance,
    decimal TotalReported)
{
    public decimal TotalDifference => Math.Round(TotalReported - TotalAttendance, 2);

    public static R03PreviewResult Fail(string error)
        => new(false, error, Array.Empty<R03DailyPreviewRow>(), 0, 0);

    public static R03PreviewResult Ok(
        IReadOnlyList<R03DailyPreviewRow> rows,
        decimal totalAttendance,
        decimal totalReported)
        => new(true, null, rows, totalAttendance, totalReported);
}
