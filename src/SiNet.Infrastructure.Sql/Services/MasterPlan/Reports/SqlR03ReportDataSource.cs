using System.Data;
using Microsoft.Data.SqlClient;
using SiNet.Application.Identity;
using SiNet.Application.MasterPlan.Reports;

namespace SiNet.Infrastructure.Sql.Services.MasterPlan.Reports;

/// <summary>
/// R03 attendance vs reported hours. Replica-only via
/// <see cref="MasterPlanReportSqlSourceResolver"/> (DEV-025) — no live MP schema query.
/// </summary>
public sealed class SqlR03ReportDataSource(IMasterPlanEmployeeConnectionProvider connectionProvider)
    : IR03ReportDataSource
{
    private readonly IMasterPlanEmployeeConnectionProvider _connectionProvider =
        connectionProvider ?? throw new ArgumentNullException(nameof(connectionProvider));

    public async Task<IReadOnlyList<R03AttendanceRow>> GetAttendanceHoursAsync(
        R03ReportRequest request,
        CancellationToken cancellationToken = default)
    {
        var cs = MasterPlanReportSqlSourceResolver.RequireReplica(_connectionProvider.GetConnectionSettings())
            .ConnectionString;
        var endExclusive = request.EndDate.Date.AddDays(1);
        var sql =
            """
            SELECT EmployeeID, ISNULL(EmployeeName,''), CAST(ReportDateTime AS date), SUM(ISNULL(Duration,0))
            FROM MP_TimeHourReports
            WHERE ReportDateTime >= @StartDate AND ReportDateTime < @EndExclusive
              AND EmployeeID IS NOT NULL
            """;
        if (request.EmployeeIds.Count > 0)
            sql += " AND EmployeeID IN (" + string.Join(",", request.EmployeeIds) + ")";
        sql += " GROUP BY EmployeeID, EmployeeName, CAST(ReportDateTime AS date) ORDER BY EmployeeID, CAST(ReportDateTime AS date)";

        return await QueryAsync(
            cs,
            sql,
            request.StartDate,
            endExclusive,
            r => new R03AttendanceRow(r.GetInt32(0), r.GetString(1), r.GetDateTime(2), r.GetDecimal(3)),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<R03ReportedRow>> GetReportedHoursAsync(
        R03ReportRequest request,
        CancellationToken cancellationToken = default)
    {
        var cs = MasterPlanReportSqlSourceResolver.RequireReplica(_connectionProvider.GetConnectionSettings())
            .ConnectionString;
        var endExclusive = request.EndDate.Date.AddDays(1);
        var sql =
            """
            SELECT EmployeeID, ISNULL(EmployeeName,''), CAST(ReportDate AS date), SUM(ISNULL(Duration,0))
            FROM MP_ProjectHoursExtended
            WHERE ReportDate >= @StartDate AND ReportDate < @EndExclusive
              AND EmployeeID IS NOT NULL
            """;
        if (request.EmployeeIds.Count > 0)
            sql += " AND EmployeeID IN (" + string.Join(",", request.EmployeeIds) + ")";
        sql += " GROUP BY EmployeeID, EmployeeName, CAST(ReportDate AS date) ORDER BY EmployeeID, CAST(ReportDate AS date)";

        return await QueryAsync(
            cs,
            sql,
            request.StartDate,
            endExclusive,
            r => new R03ReportedRow(r.GetInt32(0), r.GetString(1), r.GetDateTime(2), r.GetDecimal(3)),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<R03EmployeeInfo>> GetEmployeesAsync(
        bool activeOnly,
        CancellationToken cancellationToken = default)
    {
        _ = activeOnly;
        var cs = MasterPlanReportSqlSourceResolver.RequireReplica(_connectionProvider.GetConnectionSettings())
            .ConnectionString;
        const string sql =
            """
            SELECT DISTINCT EmployeeID, EmployeeName FROM (
              SELECT EmployeeID, EmployeeName FROM MP_TimeHourReports WHERE EmployeeID IS NOT NULL
              UNION
              SELECT EmployeeID, EmployeeName FROM MP_ProjectHoursExtended WHERE EmployeeID IS NOT NULL
            ) x
            WHERE EmployeeName IS NOT NULL AND LTRIM(RTRIM(EmployeeName)) <> ''
            ORDER BY EmployeeName
            """;

        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<R03EmployeeInfo>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            list.Add(new R03EmployeeInfo(reader.GetInt32(0), reader.GetString(1)));
        return list;
    }

    private static async Task<IReadOnlyList<T>> QueryAsync<T>(
        string cs,
        string sql,
        DateTime start,
        DateTime endExclusive,
        Func<SqlDataReader, T> map,
        CancellationToken cancellationToken)
    {
        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@StartDate", SqlDbType.DateTime2).Value = start;
        cmd.Parameters.Add("@EndExclusive", SqlDbType.DateTime2).Value = endExclusive;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<T>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            list.Add(map(reader));
        return list;
    }
}
