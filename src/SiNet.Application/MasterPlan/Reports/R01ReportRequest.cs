namespace SiNet.Application.MasterPlan.Reports;

/// <summary>R01 project portfolio dashboard request.</summary>
public sealed record R01ReportRequest(
    bool ActiveOnly = true,
    IReadOnlyList<int>? ProjectIds = null,
    IReadOnlyList<int>? CustomerIds = null,
    decimal HourPrice = 280m);
