namespace SiNet.Application.Billing;

/// <summary>
/// One project row in the billing candidate read model.
/// Snapshot* fields are monthly <c>Db_Mp_SiEng</c> enrichment (B2), never current Replica facts.
/// <see cref="LocalDecision"/> is a SiNet overlay and never changes <see cref="CandidateState"/>.
/// </summary>
public sealed record BillingCandidateRow(
    int ProjectId,
    string? ProjectNumber,
    string? ProjectName,
    string? CustomerName,
    string? ProjectStatus,
    decimal? CurrentFeeSum,
    DateTime? LastWorkDate,
    decimal Hours30,
    decimal Hours60,
    decimal Hours90,
    decimal HoursSinceLastBill,
    int WorkDays30,
    DateTime? LastBillDate,
    int? DaysSinceLastBill,
    int? LatestBillId,
    string? LatestBillNumber,
    int? LatestBillStatusId,
    string? LatestBillStatus,
    decimal? LatestBillSum,
    int BillsInCreation,
    int BillsSubmitted,
    int BillsApproved,
    int NonClosedBills,
    decimal? SnapshotBalance,
    decimal? SnapshotOpenBillSum,
    decimal? SnapshotApprovedBillSum,
    decimal? SnapshotBilledPercent,
    DateTime? SnapshotDate,
    BillingFeeTypeSummary? SnapshotFeeTypes,
    BillingCandidateState CandidateState,
    string CandidateReason,
    BillingLocalDecisionOverlay? LocalDecision = null);

