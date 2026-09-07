namespace SiNet.Application.Billing;

/// <summary>
/// Header KPIs for the billing dashboard.
/// <see cref="SubmittedOpenAmount"/> stays null until Replica-only semantics are proven.
/// </summary>
public sealed record BillingDashboardSummary(
    int ReviewNowCount,
    int BillInPreparationCount,
    decimal? SubmittedOpenAmount,
    decimal? ReceivedThisMonth,
    DateTime? ReplicaLastSyncTime,
    DateTime? MonthlySnapshotDate,
    int AccumulatedWorkCount = 0,
    int CoveredByLatestBillCount = 0,
    int NotUrgentCount = 0);
