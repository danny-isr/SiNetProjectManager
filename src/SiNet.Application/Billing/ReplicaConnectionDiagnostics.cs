namespace SiNet.Application.Billing;

/// <summary>
/// Sanitized Replica identity and source stamps. Never includes password or other secrets.
/// Server names are informational — not an allow-list.
/// </summary>
public sealed record ReplicaConnectionDiagnostics(
    string? ConfiguredDataSource,
    string? InitialCatalog,
    string? SqlServerName,
    string? SqlMachineName,
    string? SqlInstanceName,
    string? DatabaseName,
    DateTime? ProjectsSyncTime,
    DateTime? BillsSyncTime,
    DateTime? IntakesSyncTime,
    DateTime? ProjectHoursSyncTime,
    DateTime? ProjectHoursExtendedSyncTime,
    DateTime? MaxHoursReportDate,
    DateTime? MaxProjectsLastUpdated,
    DateTime? MaxBillsLastUpdated,
    DateTime? MaxIntakesLastUpdated)
{
    public static ReplicaConnectionDiagnostics Empty { get; } = new(
        null, null, null, null, null, null,
        null, null, null, null, null,
        null, null, null, null);

    public BillingSourceFreshness ToSourceFreshness() =>
        new(
            ReplicaLastSyncTime: MaxTime(
                ProjectsSyncTime,
                BillsSyncTime,
                IntakesSyncTime,
                ProjectHoursExtendedSyncTime),
            ProjectsSyncTime: ProjectsSyncTime,
            BillsSyncTime: BillsSyncTime,
            IntakesSyncTime: IntakesSyncTime,
            ProjectHoursExtendedSyncTime: ProjectHoursExtendedSyncTime,
            ProjectHoursSyncTime: ProjectHoursSyncTime,
            LatestProjectHoursDate: MaxHoursReportDate,
            MonthlySnapshotDate: null,
            MaxProjectsLastUpdated: MaxProjectsLastUpdated,
            MaxBillsLastUpdated: MaxBillsLastUpdated,
            MaxIntakesLastUpdated: MaxIntakesLastUpdated);

    private static DateTime? MaxTime(params DateTime?[] values)
    {
        DateTime? max = null;
        foreach (var value in values)
        {
            if (value is DateTime t && (max is null || t > max.Value))
                max = t;
        }

        return max;
    }
}
