namespace SiNet.Application.MasterPlan.Reports;

public sealed record R03AttendanceRow(int EmployeeId, string EmployeeName, DateTime ReportDate, decimal TotalHours);

public sealed record R03ReportedRow(int EmployeeId, string EmployeeName, DateTime ReportDate, decimal TotalHours);

/// <summary>Replica data for R03 (no Google dependency).</summary>
public interface IR03ReportDataSource
{
    Task<IReadOnlyList<R03AttendanceRow>> GetAttendanceHoursAsync(
        R03ReportRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<R03ReportedRow>> GetReportedHoursAsync(
        R03ReportRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<R03EmployeeInfo>> GetEmployeesAsync(
        bool activeOnly,
        CancellationToken cancellationToken = default);
}
