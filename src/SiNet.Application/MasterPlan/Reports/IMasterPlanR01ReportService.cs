namespace SiNet.Application.MasterPlan.Reports;

/// <summary>Native R01 portfolio → Google Sheets.</summary>
public interface IMasterPlanR01ReportService
{
    Task<MasterPlanReportGenerationResult> GenerateAsync(
        R01ReportRequest request,
        IProgress<(string Phase, string Message, int Percent)>? progress = null,
        CancellationToken cancellationToken = default);
}
