using System.Data;
using Microsoft.Data.SqlClient;
using SiNet.Application.Identity;
using SiNet.Application.MasterPlan.Reports;

namespace SiNet.Infrastructure.Sql.Services.MasterPlan.Reports;

/// <summary>
/// R02 hours: prefer MasterPlan HoursReports up to max date, then Replica MP_ProjectHours
/// (parity with GoogleConnector R02DataMerger).
/// </summary>
public sealed class SqlR02ReportDataSource(IMasterPlanEmployeeConnectionProvider connectionProvider)
    : IR02ReportDataSource
{
    private readonly IMasterPlanEmployeeConnectionProvider _connectionProvider =
        connectionProvider ?? throw new ArgumentNullException(nameof(connectionProvider));

    public async Task<IReadOnlyList<R02HoursRow>> GetMergedHoursAsync(
        R02ReportRequest request,
        CancellationToken cancellationToken = default)
    {
        var settings = _connectionProvider.GetConnectionSettings();
        var replica = settings.ReplicaDatabase;
        var masterPlan = settings.MasterPlanDatabase;
        if (string.IsNullOrWhiteSpace(replica))
            throw new InvalidOperationException("ReplicaDatabase connection string is not configured in the vault.");

        DateTime? mpMax = null;
        if (!string.IsNullOrWhiteSpace(masterPlan))
            mpMax = await GetMaxDateAsync(masterPlan, cancellationToken).ConfigureAwait(false);

        var rows = new List<R02HoursRow>();
        if (mpMax is null || string.IsNullOrWhiteSpace(masterPlan))
        {
            rows.AddRange(await QueryReplicaAsync(replica, request.StartDate, request.EndDate, request, cancellationToken)
                .ConfigureAwait(false));
        }
        else if (request.EndDate.Date <= mpMax.Value.Date)
        {
            rows.AddRange(await QueryMasterPlanAsync(masterPlan, request.StartDate, request.EndDate, request, cancellationToken)
                .ConfigureAwait(false));
        }
        else if (request.StartDate.Date > mpMax.Value.Date)
        {
            rows.AddRange(await QueryReplicaAsync(replica, request.StartDate, request.EndDate, request, cancellationToken)
                .ConfigureAwait(false));
        }
        else
        {
            rows.AddRange(await QueryMasterPlanAsync(masterPlan, request.StartDate, mpMax.Value, request, cancellationToken)
                .ConfigureAwait(false));
            rows.AddRange(await QueryReplicaAsync(replica, mpMax.Value.Date.AddDays(1), request.EndDate, request, cancellationToken)
                .ConfigureAwait(false));
        }

        if (request.ExcludeZeroHours)
            rows = rows.Where(r => r.Hours != 0m).ToList();

        return rows
            .OrderBy(r => r.ReportDate)
            .ThenBy(r => r.ProjectNum)
            .ThenBy(r => r.EmployeeName)
            .ToList();
    }

    private static async Task<DateTime?> GetMaxDateAsync(string cs, CancellationToken cancellationToken)
    {
        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new SqlCommand("SELECT MAX(ReportDate) FROM dbo.HoursReports", conn);
        var value = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is DateTime dt ? dt : null;
    }

    private static async Task<IReadOnlyList<R02HoursRow>> QueryMasterPlanAsync(
        string cs,
        DateTime start,
        DateTime end,
        R02ReportRequest request,
        CancellationToken cancellationToken)
    {
        var sql =
            """
            SELECT CAST(ReportDate AS date), ProjectID, ProjectNum, ProjectName, EmployeeID, EmployeeName, SUM(ISNULL(Hours,0))
            FROM dbo.HoursReports
            WHERE ReportDate >= @Start AND ReportDate < @EndExclusive
            """;
        sql += AppendFilters(request);
        sql += " GROUP BY CAST(ReportDate AS date), ProjectID, ProjectNum, ProjectName, EmployeeID, EmployeeName";
        return await QueryAsync(cs, sql, start, end.Date.AddDays(1), "MasterPlan", cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<R02HoursRow>> QueryReplicaAsync(
        string cs,
        DateTime start,
        DateTime end,
        R02ReportRequest request,
        CancellationToken cancellationToken)
    {
        var sql =
            """
            SELECT CAST(ReportDate AS date), ProjectID, ProjectNum, ProjectName, EmployeeID, EmployeeName, SUM(ISNULL(Duration,0))
            FROM MP_ProjectHours
            WHERE ReportDate >= @Start AND ReportDate < @EndExclusive
            """;
        sql += AppendFilters(request);
        sql += " GROUP BY CAST(ReportDate AS date), ProjectID, ProjectNum, ProjectName, EmployeeID, EmployeeName";
        return await QueryAsync(cs, sql, start, end.Date.AddDays(1), "Replica", cancellationToken).ConfigureAwait(false);
    }

    private static string AppendFilters(R02ReportRequest request)
    {
        var sql = "";
        if (request.ProjectIds is { Count: > 0 })
            sql += " AND ProjectID IN (" + string.Join(",", request.ProjectIds) + ")";
        if (request.EmployeeIds is { Count: > 0 })
            sql += " AND EmployeeID IN (" + string.Join(",", request.EmployeeIds) + ")";
        if (request.CustomerIds is { Count: > 0 })
            sql += " AND CustomerID IN (" + string.Join(",", request.CustomerIds) + ")";
        return sql;
    }

    private static async Task<IReadOnlyList<R02HoursRow>> QueryAsync(
        string cs,
        string sql,
        DateTime start,
        DateTime endExclusive,
        string source,
        CancellationToken cancellationToken)
    {
        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@Start", SqlDbType.DateTime2).Value = start;
        cmd.Parameters.Add("@EndExclusive", SqlDbType.DateTime2).Value = endExclusive;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<R02HoursRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new R02HoursRow(
                reader.GetDateTime(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? 0m : Convert.ToDecimal(reader.GetValue(6)),
                source));
        }

        return list;
    }
}
