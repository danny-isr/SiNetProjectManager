namespace SiNet.Application.Billing;

/// <summary>Replica project fact used by the candidate engine.</summary>
public sealed record BillingProjectFact(
    int ProjectId,
    string? ProjectNumber,
    string? ProjectName,
    string? CustomerName,
    int? CustomerId,
    string? ProjectStatus,
    bool IsActive,
    decimal? CurrentFeeSum);

/// <summary>Replica bill fact. Timeline is <c>SubmitDate ?? LastUpdated</c>.</summary>
public sealed record BillingBillFact(
    int BillId,
    int ProjectId,
    string? BillNumber,
    decimal? Sum,
    int? StatusId,
    string? Status,
    DateTime? SubmitDate,
    DateTime? LastUpdated);

/// <summary>Already-normalized hour row (same conversion as R02).</summary>
public sealed record BillingHourFact(
    int ProjectId,
    DateTime ReportDate,
    decimal Hours);
