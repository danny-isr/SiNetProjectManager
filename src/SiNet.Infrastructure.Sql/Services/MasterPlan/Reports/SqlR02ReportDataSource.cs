using System.Data;
using Microsoft.Data.SqlClient;
using SiNet.Application.Identity;
using SiNet.Application.MasterPlan.Reports;

namespace SiNet.Infrastructure.Sql.Services.MasterPlan.Reports;

/// <summary>
/// R02 hours: prefer MasterPlan HoursReports up to max date, then Replica MP_ProjectHours
/// (parity with GoogleConnector R02DataMerger).
/// <para>
/// MasterPlan date column is <c>DateTime</c> (not <c>ReportDate</c>). Replica uses
/// <c>ReportDate</c> + <c>TotalHours</c> (TimeSpan/ticks — converted to decimal hours).
/// </para>
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
            mpMax = await GetMasterPlanMaxDateAsync(masterPlan, cancellationToken).ConfigureAwait(false);

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

    private static async Task<DateTime?> GetMasterPlanMaxDateAsync(string cs, CancellationToken cancellationToken)
    {
        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        // Legacy MasterPlanR02Repository: MAX(CAST(hr.DateTime AS date))
        await using var cmd = new SqlCommand("SELECT MAX(CAST([DateTime] AS date)) FROM dbo.HoursReports", conn);
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
            SELECT
              CAST(hr.[DateTime] AS date),
              hr.ProjectID,
              p.ProjectNum,
              p.Name,
              hr.EmployeeID,
              LTRIM(RTRIM(ISNULL(e.FirstName,'') + ' ' + ISNULL(e.LastName,''))),
              SUM(ISNULL(hr.Hours,0))
            FROM dbo.HoursReports hr
            LEFT JOIN dbo.Projects p ON hr.ProjectID = p.ID
            LEFT JOIN dbo.Employees e ON hr.EmployeeID = e.ID
            WHERE hr.[DateTime] >= @Start AND hr.[DateTime] < @EndExclusive
            """;
        sql += AppendMasterPlanFilters(request);
        sql += """
             GROUP BY CAST(hr.[DateTime] AS date), hr.ProjectID, p.ProjectNum, p.Name, hr.EmployeeID,
                      e.FirstName, e.LastName
            """;
        return await QueryAsync(cs, sql, start, end.Date.AddDays(1), "MasterPlan", convertHours: false, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<R02HoursRow>> QueryReplicaAsync(
        string cs,
        DateTime start,
        DateTime end,
        R02ReportRequest request,
        CancellationToken cancellationToken)
    {
        // Replica MP_ProjectHours: ReportDate + TotalHours (SQL time / TimeSpan), not Duration.
        var sql =
            """
            SELECT
              CAST(ph.ReportDate AS date),
              ph.ProjectID,
              p.ProjectNum,
              p.Name,
              ph.EmployeeID,
              LTRIM(RTRIM(ISNULL(e.FirstName,'') + ' ' + ISNULL(e.LastName,''))),
              ph.TotalHours
            FROM MP_ProjectHours ph
            LEFT JOIN MP_Projects p ON ph.ProjectID = p.ID
            LEFT JOIN MP_Employees e ON ph.EmployeeID = e.ID
            WHERE ph.ReportDate >= @Start AND ph.ReportDate < @EndExclusive
            """;
        sql += AppendReplicaFilters(request);
        // Aggregate after converting TimeSpan hours in-process (SQL SUM on time is unreliable across schemas).
        var raw = await QueryAsync(cs, sql, start, end.Date.AddDays(1), "Replica", convertHours: true, cancellationToken)
            .ConfigureAwait(false);

        return raw
            .GroupBy(r => new { r.ReportDate, r.ProjectId, r.ProjectNum, r.ProjectName, r.EmployeeId, r.EmployeeName })
            .Select(g => new R02HoursRow(
                g.Key.ReportDate,
                g.Key.ProjectId,
                g.Key.ProjectNum,
                g.Key.ProjectName,
                g.Key.EmployeeId,
                g.Key.EmployeeName,
                g.Sum(x => x.Hours),
                "Replica"))
            .ToList();
    }

    private static string AppendMasterPlanFilters(R02ReportRequest request)
    {
        var sql = "";
        if (request.ProjectIds is { Count: > 0 })
            sql += " AND hr.ProjectID IN (" + string.Join(",", request.ProjectIds) + ")";
        if (request.EmployeeIds is { Count: > 0 })
            sql += " AND hr.EmployeeID IN (" + string.Join(",", request.EmployeeIds) + ")";
        if (request.CustomerIds is { Count: > 0 })
            sql += " AND p.CustomerID IN (" + string.Join(",", request.CustomerIds) + ")";
        return sql;
    }

    private static string AppendReplicaFilters(R02ReportRequest request)
    {
        var sql = "";
        if (request.ProjectIds is { Count: > 0 })
            sql += " AND ph.ProjectID IN (" + string.Join(",", request.ProjectIds) + ")";
        if (request.EmployeeIds is { Count: > 0 })
            sql += " AND ph.EmployeeID IN (" + string.Join(",", request.EmployeeIds) + ")";
        if (request.CustomerIds is { Count: > 0 })
            sql += " AND p.CustomerID IN (" + string.Join(",", request.CustomerIds) + ")";
        return sql;
    }

    private static async Task<IReadOnlyList<R02HoursRow>> QueryAsync(
        string cs,
        string sql,
        DateTime start,
        DateTime endExclusive,
        string source,
        bool convertHours,
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
            var hours = convertHours
                ? ConvertHoursRaw(reader.IsDBNull(6) ? null : reader.GetValue(6))
                : reader.IsDBNull(6) ? 0m : Convert.ToDecimal(reader.GetValue(6));

            list.Add(new R02HoursRow(
                reader.GetDateTime(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                hours,
                source));
        }

        return list;
    }

    /// <summary>Replica TotalHours may be TimeSpan, ticks, or decimal — match GoogleConnector conversion.</summary>
    internal static decimal ConvertHoursRaw(object? hoursRaw)
    {
        if (hoursRaw is null or DBNull)
            return 0m;
        if (hoursRaw is TimeSpan ts)
            return Math.Round((decimal)ts.TotalHours, 2);
        if (hoursRaw is decimal d)
            return Math.Round(d, 2);
        if (hoursRaw is double dbl)
            return Math.Round((decimal)dbl, 2);
        if (hoursRaw is float f)
            return Math.Round((decimal)f, 2);
        if (hoursRaw is long ticks)
            return Math.Round((decimal)TimeSpan.FromTicks(ticks).TotalHours, 2);
        if (hoursRaw is int i)
            return Math.Round((decimal)TimeSpan.FromTicks(i).TotalHours, 2);

        return Math.Round(Convert.ToDecimal(hoursRaw), 2);
    }
}
