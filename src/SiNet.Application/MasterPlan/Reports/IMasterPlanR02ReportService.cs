namespace SiNet.Application.MasterPlan.Reports;

/// <summary>Native R02 hours → Google Sheets.</summary>
public interface IMasterPlanR02ReportService
{
    Task<MasterPlanReportGenerationResult> GenerateAsync(
        R02ReportRequest request,
        IProgress<(string Phase, string Message, int Percent)>? progress = null,
        CancellationToken cancellationToken = default);
}
