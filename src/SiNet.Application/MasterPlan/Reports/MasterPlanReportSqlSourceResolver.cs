using SiNet.Application.Identity;

namespace SiNet.Application.MasterPlan.Reports;

/// <summary>
/// Which SQL database a product MasterPlan report should read (DEV-025).
/// </summary>
public enum MasterPlanReportSqlSourceKind
{
    Replica = 0,
    LiveMasterPlan = 1
}

/// <summary>Resolved connection for a product report query.</summary>
public sealed record MasterPlanReportSqlSource(
    MasterPlanReportSqlSourceKind Kind,
    string ConnectionString);

/// <summary>
/// Shared Replica-first source selection for every MasterPlan product report.
/// Live <c>Db_Mp_SiEng</c> is last-resort only when Replica is not configured
/// and the report still has a live-schema query. Live MP remains for restore/ETL/admin.
/// </summary>
public static class MasterPlanReportSqlSourceResolver
{
    public static MasterPlanReportSqlSource Resolve(MasterPlanEmployeeConnectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!string.IsNullOrWhiteSpace(settings.ReplicaDatabase))
        {
            return new MasterPlanReportSqlSource(
                MasterPlanReportSqlSourceKind.Replica,
                settings.ReplicaDatabase);
        }

        if (!string.IsNullOrWhiteSpace(settings.MasterPlanDatabase))
        {
            return new MasterPlanReportSqlSource(
                MasterPlanReportSqlSourceKind.LiveMasterPlan,
                settings.MasterPlanDatabase);
        }

        throw new InvalidOperationException(
            "ReplicaDatabase is not configured in the vault. Product reports read Replica first (DEV-025).");
    }

    /// <summary>
    /// Reports whose SQL exists only against Replica <c>MP_*</c> tables (e.g. R03).
    /// </summary>
    public static MasterPlanReportSqlSource RequireReplica(MasterPlanEmployeeConnectionSettings settings)
    {
        var resolved = Resolve(settings);
        if (resolved.Kind != MasterPlanReportSqlSourceKind.Replica)
        {
            throw new InvalidOperationException(
                "ReplicaDatabase is not configured in the vault. This report reads Replica only (DEV-025).");
        }

        return resolved;
    }
}
