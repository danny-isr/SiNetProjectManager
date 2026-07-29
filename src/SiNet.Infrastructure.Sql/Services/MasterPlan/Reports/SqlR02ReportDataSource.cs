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
        if (useExtended)
        {
            // HoursRaw: valid Duration (decimal hours 0–24), else TotalHours (TIME/TimeSpan).
            // Never ISNULL(Duration,0) — that hid TotalHours/start-end fallbacks.
            sql =
                """
                SELECT
                  ph.ID,
                  CAST(ph.ReportDate AS date),
                  ph.StartTime,
                  ph.EndTime,
                  CASE
                    WHEN ph.Duration IS NOT NULL AND ph.Duration >= 0 AND ph.Duration <= 24 THEN ph.Duration
                    ELSE NULL
                  END,
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
                  COALESCE(ph.SubContractStepName, ph.StepName),
                  ph.TotalHours
                FROM MP_ProjectHoursExtended ph
                LEFT JOIN MP_Employees e ON ph.EmployeeID = e.ID
                LEFT JOIN MP_Projects p ON ph.ProjectID = p.ID
                WHERE ph.ReportDate >= @Start AND ph.ReportDate < @EndExclusive
                """;
            sql += AppendReplicaExtendedFilters(request);
        }
        else
        {
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
                cancellationToken,
                totalHoursOrdinal: useExtended ? 18 : null)
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
        CancellationToken cancellationToken,
        int? totalHoursOrdinal = null)
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
            var startTime = ReadTimeSpan(reader, 2);
            var endTime = ReadTimeSpan(reader, 3);
            var hoursRaw = reader.IsDBNull(4) ? null : reader.GetValue(4);
            // Extended: when Duration out of range / null, fall back to TotalHours TIME.
            if (hoursRaw is null
                && totalHoursOrdinal is int thOrd
                && thOrd < reader.FieldCount
                && !reader.IsDBNull(thOrd))
            {
                hoursRaw = reader.GetValue(thOrd);
            }

            var hours = ConvertHoursRaw(hoursRaw, startTime, endTime);

            list.Add(new R02HoursRow(
                HourReportId: reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                ReportDate: reader.GetDateTime(1),
                StartTime: startTime,
                EndTime: endTime,
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

    /// <summary>
    /// Converts DB hours payloads to decimal hours (parity with GoogleConnector R02ReportService).
    /// Handles TimeSpan, decimal hours, minutes, milliseconds, and .NET ticks; falls back to start/end.
    /// </summary>
    internal static decimal ConvertHoursRaw(
        object? hoursRaw,
        TimeSpan? startTime = null,
        TimeSpan? endTime = null)
    {
        if (hoursRaw is null or DBNull)
            return CalculateFromStartEnd(startTime, endTime);

        if (hoursRaw is TimeSpan ts)
            return Math.Round((decimal)ts.TotalHours, 2);

        var numericValue = hoursRaw switch
        {
            long l => l,
            decimal d => d,
            double dbl => (decimal)dbl,
            float f => (decimal)f,
            int i => i,
            short s => s,
            byte b => b,
            _ => TryParseToDecimal(hoursRaw),
        };

        return ConvertNumericToHours(numericValue, startTime, endTime);
    }

    /// <summary>Heuristic conversion matching legacy GoogleConnector (plus start/end before ms).</summary>
    private static decimal ConvertNumericToHours(decimal value, TimeSpan? startTime, TimeSpan? endTime)
    {
        const decimal MillisecondsPerHour = 3_600_000m;
        const decimal TicksPerHour = 36_000_000_000m;
        // TIME/ticks sometimes leak as Ticks/1e6 (2h → 72_000).
        const decimal ScaledTicksPerHour = 36_000m;
        const decimal MaxReasonableHours = 24m;
        const decimal MinMilliseconds = 60_000m;
        const decimal MaxMilliseconds = 86_400_000m;
        const decimal MinTicks = 36_000_000_000m;

        var absValue = Math.Abs(value);

        if (absValue <= MaxReasonableHours)
            return Math.Round(value, 2);

        if (absValue >= MinTicks)
            return Math.Round(value / TicksPerHour, 2);

        // Prefer wall-clock duration when the raw number is clearly not decimal-hours.
        var fromRange = CalculateFromStartEnd(startTime, endTime);
        if (fromRange > 0)
            return fromRange;

        // Scaled ticks (Ticks / 1_000_000): 2h → 72_000. Check before ms (72_000 is also in ms range).
        if (absValue >= ScaledTicksPerHour)
        {
            var scaledHours = absValue / ScaledTicksPerHour;
            if (scaledHours <= MaxReasonableHours)
                return Math.Round(value / ScaledTicksPerHour, 2);
        }

        if (absValue >= MinMilliseconds && absValue <= MaxMilliseconds)
            return Math.Round(value / MillisecondsPerHour, 2);

        // MasterPlan HoursReports.Hours is raw minutes.
        if (absValue > MaxReasonableHours && absValue < MinMilliseconds)
            return Math.Round(value / 60m, 2);

        return Math.Round(value, 2);
    }

    private static decimal CalculateFromStartEnd(TimeSpan? startTime, TimeSpan? endTime)
    {
        if (!startTime.HasValue || !endTime.HasValue)
            return 0m;

        var duration = endTime.Value - startTime.Value;
        if (duration < TimeSpan.Zero)
            duration = duration.Add(TimeSpan.FromHours(24));

        if (duration <= TimeSpan.Zero)
            return 0m;

        var hours = (decimal)duration.TotalHours;
        return hours > 24m ? 0m : Math.Round(hours, 2);
    }

    private static decimal TryParseToDecimal(object? value)
    {
        if (value is null)
            return 0m;
        return decimal.TryParse(Convert.ToString(value), out var parsed) ? parsed : 0m;
    }
}
