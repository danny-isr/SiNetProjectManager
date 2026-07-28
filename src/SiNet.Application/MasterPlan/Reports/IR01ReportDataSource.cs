namespace SiNet.Application.MasterPlan.Reports;

public sealed record R01PortfolioRow(
    int ProjectId,
    string? ProjectNum,
    string? ProjectName,
    string? CustomerName,
    string? StatusName,
    decimal? FeeSum,
    decimal? HoursSum,
    bool IsActive);

/// <summary>Replica / MasterPlan portfolio rows for R01.</summary>
public interface IR01ReportDataSource
{
    Task<IReadOnlyList<R01PortfolioRow>> GetPortfolioAsync(
        R01ReportRequest request,
        CancellationToken cancellationToken = default);
}
