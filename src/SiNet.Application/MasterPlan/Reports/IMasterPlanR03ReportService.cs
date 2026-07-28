namespace SiNet.Application.MasterPlan.Reports;

/// <summary>Native R03 attendance comparison → Google Sheets.</summary>
public interface IMasterPlanR03ReportService
{
    Task<IReadOnlyList<R03EmployeeInfo>> GetEmployeesAsync(
        bool activeOnly,
        CancellationToken cancellationToken = default);

    Task<MasterPlanReportGenerationResult> GenerateAsync(
        R03ReportRequest request,
        IProgress<(string Phase, string Message, int Percent)>? progress = null,
        CancellationToken cancellationToken = default);
}
