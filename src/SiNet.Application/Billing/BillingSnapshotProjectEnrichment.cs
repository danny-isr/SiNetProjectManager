namespace SiNet.Application.Billing;

/// <summary>
/// Monthly <c>Db_Mp_SiEng</c> enrichment for one project. Null decimals mean SQL NULL, not zero.
/// </summary>
public sealed record BillingSnapshotProjectEnrichment(
    int ProjectId,
    decimal? Balance,
    decimal? OpenBillSum,
    decimal? ApprovedBillSum,
    decimal? ProgressPercentage,
    string? CustomerName,
    IReadOnlyList<int> SubContractFeeTypeIds);
