namespace SiNet.Application.MasterPlan.Reports;

public sealed record R02HoursRow(
    DateTime ReportDate,
    int? ProjectId,
    string? ProjectNum,
    string? ProjectName,
    int? EmployeeId,
    string? EmployeeName,
    decimal Hours,
    string Source);

/// <summary>Merged MasterPlan + Replica hours for R02.</summary>
public interface IR02ReportDataSource
{
    Task<IReadOnlyList<R02HoursRow>> GetMergedHoursAsync(
        R02ReportRequest request,
        CancellationToken cancellationToken = default);
}
