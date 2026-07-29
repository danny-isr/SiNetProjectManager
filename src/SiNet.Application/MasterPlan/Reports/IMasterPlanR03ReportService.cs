namespace SiNet.Application.MasterPlan.Reports;

/// <summary>Native R03 attendance comparison — in-app preview and optional Google Sheets export.</summary>
public interface IMasterPlanR03ReportService
{
    Task<IReadOnlyList<R03EmployeeInfo>> GetEmployeesAsync(
        bool activeOnly,
        CancellationToken cancellationToken = default);

    /// <summary>Builds flat daily rows for the in-app DataGrid (Replica only; no Google).</summary>
    Task<R03PreviewResult> PreviewAsync(
        R03ReportRequest request,
        CancellationToken cancellationToken = default);

    Task<MasterPlanReportGenerationResult> GenerateAsync(
        R03ReportRequest request,
        IProgress<(string Phase, string Message, int Percent)>? progress = null,
        CancellationToken cancellationToken = default);
}
