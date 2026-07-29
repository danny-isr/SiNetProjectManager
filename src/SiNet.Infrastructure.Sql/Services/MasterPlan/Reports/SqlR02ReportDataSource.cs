using System.Data;
using Microsoft.Data.SqlClient;
using SiNet.Application.Identity;
using SiNet.Application.MasterPlan.Reports;

namespace SiNet.Infrastructure.Sql.Services.MasterPlan.Reports;

/// <summary>
/// R02 hours: prefer MasterPlan HoursReports up to max date, then Replica MP_ProjectHours
/// (parity with GoogleConnector R02DataMerger). One sheet row per hour report — not aggregated —
/// so Description / SubContract / Step remain available.
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
            .ThenBy(r => r.HourReportId)
            .ToList();
    }

    private static async Task<DateTime?> GetMasterPlanMaxDateAsync(string cs, CancellationToken cancellationToken)
    {
        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
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
              hr.ID,
              CAST(hr.[DateTime] AS date),
              CAST(hr.StartTime AS time),
              CAST(hr.EndTime AS time),
              hr.Hours,
              hr.Description,
              hr.EmployeeID,
              LTRIM(RTRIM(ISNULL(e.FirstName,'') + ' ' + ISNULL(e.LastName,''))),
              hr.ProjectID,
              p.ProjectNum,
              p.Name,
              p.CustomerID,
              ISNULL(co.Name, ''),
              hr.SubContractID,
              sc.SubContractNum,
              sc.Name,
              hr.SubContractStepID,
              scs.Name
            FROM dbo.HoursReports hr
            INNER JOIN dbo.Employees e ON hr.EmployeeID = e.ID
            INNER JOIN dbo.Projects p ON hr.ProjectID = p.ID
            LEFT JOIN dbo.Contacts ct ON p.CustomerID = ct.ID
            LEFT JOIN dbo.Companies co ON ct.CompanyID = co.ID
            LEFT JOIN dbo.SubContracts sc ON hr.SubContractID = sc.ID
            LEFT JOIN dbo.SubContractSteps scs ON hr.SubContractStepID = scs.ID
            WHERE hr.[DateTime] >= @Start AND hr.[DateTime] < @EndExclusive
            """;
        sql += AppendMasterPlanFilters(request);

        return await QueryAsync(
                cs,
                sql,
                start,
                end.Date.AddDays(1),
                "MasterPlan",
                convertHours: false,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<R02HoursRow>> QueryReplicaAsync(
        string cs,
        DateTime start,
        DateTime end,
        R02ReportRequest request,
        CancellationToken cancellationToken)
    {
        // Prefer Extended (Description + SubContract); fall back to basic MP_ProjectHours.
        await using var probe = new SqlConnection(cs);
        await probe.OpenAsync(cancellationToken).ConfigureAwait(false);
        var useExtended = await TableExistsAsync(probe, "MP_ProjectHoursExtended", cancellationToken)
            .ConfigureAwait(false);

        string sql;
        bool convertHours;
        if (useExtended)
        {
            convertHours = false; // Duration is already decimal hours
            sql =
                """
                SELECT
                  ph.ID,
                  CAST(ph.ReportDate AS date),
                  ph.StartTime,
                  ph.EndTime,
                  ISNULL(ph.Duration, 0),
                  ph.Description,
                  ph.EmployeeID,
                  COALESCE(
                    NULLIF(LTRIM(RTRIM(ph.EmployeeName)), ''),
                    LTRIM(RTRIM(ISNULL(e.FirstName,'') + ' ' + ISNULL(e.LastName,'')))),
                  ph.ProjectID,
                  COALESCE(NULLIF(LTRIM(RTRIM(ph.ProjectNumber)), ''), p.ProjectNum),
                  COALESCE(NULLIF(LTRIM(RTRIM(ph.ProjectName)), ''), p.Name),
                  p.CustomerID,
                  p.CustomerName,
                  ph.SubContractID,
                  CAST(NULL AS nvarchar(50)),
                  ph.SubContractName,
                  ph.SubContractStepID,
                  COALESCE(ph.SubContractStepName, ph.StepName)
                FROM MP_ProjectHoursExtended ph
                LEFT JOIN MP_Employees e ON ph.EmployeeID = e.ID
                LEFT JOIN MP_Projects p ON ph.ProjectID = p.ID
                WHERE ph.ReportDate >= @Start AND ph.ReportDate < @EndExclusive
                """;
            sql += AppendReplicaExtendedFilters(request);
        }
        else
        {
            convertHours = true;
            sql =
                """
                SELECT
                  ph.ID,
                  CAST(ph.ReportDate AS date),
                  ph.StartTime,
                  ph.EndTime,
                  ph.TotalHours,
                  ph.Description,
                  ph.EmployeeID,
                  LTRIM(RTRIM(ISNULL(e.FirstName,'') + ' ' + ISNULL(e.LastName,''))),
                  ph.ProjectID,
                  p.ProjectNum,
                  p.Name,
                  p.CustomerID,
                  p.CustomerName,
                  CAST(NULL AS int),
                  CAST(NULL AS nvarchar(50)),
                  CAST(NULL AS nvarchar(200)),
                  CAST(NULL AS int),
                  ph.StepName
                FROM MP_ProjectHours ph
                INNER JOIN MP_Employees e ON ph.EmployeeID = e.ID
                INNER JOIN MP_Projects p ON ph.ProjectID = p.ID
                WHERE ph.ReportDate >= @Start AND ph.ReportDate < @EndExclusive
                """;
            sql += AppendReplicaFilters(request);
        }

        return await QueryAsync(
                cs,
                sql,
                start,
                end.Date.AddDays(1),
                "Replica",
                convertHours,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<bool> TableExistsAsync(
        SqlConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var cmd = new SqlCommand(
            """
            SELECT CASE WHEN EXISTS (
              SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @TableName
            ) THEN 1 ELSE 0 END
            """,
            connection);
        cmd.Parameters.Add("@TableName", SqlDbType.NVarChar, 128).Value = tableName;
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is int i && i == 1;
    }

    private static string AppendMasterPlanFilters(R02ReportRequest request)
    {
        var sql = "";
        if (request.ProjectIds is { Count: > 0 })
            sql += " AND hr.ProjectID IN (" + string.Join(",", request.ProjectIds) + ")";
        if (request.EmployeeIds is { Count: > 0 })
            sql += " AND hr.EmployeeID IN (" + string.Join(",", request.EmployeeIds) + ")";
        if (request.CustomerIds is { Count: > 0 })
            sql += " AND ct.CompanyID IN (" + string.Join(",", request.CustomerIds) + ")";
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

    private static string AppendReplicaExtendedFilters(R02ReportRequest request)
    {
        // Same project/employee filters; customer via joined MP_Projects when present.
        return AppendReplicaFilters(request);
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
                ? ConvertHoursRaw(reader.IsDBNull(4) ? null : reader.GetValue(4))
                : reader.IsDBNull(4) ? 0m : Math.Round(Convert.ToDecimal(reader.GetValue(4)), 2);

            list.Add(new R02HoursRow(
                HourReportId: reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                ReportDate: reader.GetDateTime(1),
                StartTime: ReadTimeSpan(reader, 2),
                EndTime: ReadTimeSpan(reader, 3),
                Hours: hours,
                Description: reader.IsDBNull(5) ? null : reader.GetString(5),
                EmployeeId: reader.IsDBNull(6) ? null : reader.GetInt32(6),
                EmployeeName: reader.IsDBNull(7) ? null : reader.GetString(7),
                ProjectId: reader.IsDBNull(8) ? null : reader.GetInt32(8),
                ProjectNum: reader.IsDBNull(9) ? null : reader.GetString(9),
                ProjectName: reader.IsDBNull(10) ? null : reader.GetString(10),
                CustomerId: reader.IsDBNull(11) ? null : reader.GetInt32(11),
                CustomerName: reader.IsDBNull(12) ? null : reader.GetString(12),
                SubContractId: reader.IsDBNull(13) ? null : reader.GetInt32(13),
                SubContractNum: reader.IsDBNull(14) ? null : reader.GetString(14),
                SubContractName: reader.IsDBNull(15) ? null : reader.GetString(15),
                SubContractStepId: reader.IsDBNull(16) ? null : reader.GetInt32(16),
                SubContractStepName: reader.IsDBNull(17) ? null : reader.GetString(17),
                Source: source));
        }

        return list;
    }

    private static TimeSpan? ReadTimeSpan(SqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return null;
        var value = reader.GetValue(ordinal);
        return value switch
        {
            TimeSpan ts => ts,
            DateTime dt => dt.TimeOfDay,
            _ => TimeSpan.TryParse(Convert.ToString(value), out var parsed) ? parsed : null,
        };
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
