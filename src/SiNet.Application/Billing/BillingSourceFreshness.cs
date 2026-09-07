namespace SiNet.Application.Billing;

/// <summary>Replica / snapshot freshness stamps surfaced on the dashboard.</summary>
public sealed record BillingSourceFreshness(
    DateTime? ReplicaLastSyncTime,
    DateTime? ProjectsSyncTime,
    DateTime? BillsSyncTime,
    DateTime? IntakesSyncTime,
    DateTime? ProjectHoursExtendedSyncTime,
    DateTime? ProjectHoursSyncTime,
    DateTime? LatestProjectHoursDate,
    DateTime? MonthlySnapshotDate,
    DateTime? MaxProjectsLastUpdated = null,
    DateTime? MaxBillsLastUpdated = null,
    DateTime? MaxIntakesLastUpdated = null);
