using System.Data;
using Microsoft.Data.SqlClient;
using SiNet.Application.Billing;
using SiNet.Application.Identity;
using SiNet.Application.MasterPlan.Reports;
using SiNet.Infrastructure.Sql.Services.MasterPlan;

namespace SiNet.Infrastructure.Sql.Services.Billing;

/// <summary>
/// Loads current billing facts from Replica only.
/// Does not read live <c>Db_Mp_SiEng</c> and does not union basic hours when Extended exists.
/// </summary>
public sealed class ReplicaBillingDataSource(IMasterPlanEmployeeConnectionProvider connectionProvider)
    : IReplicaBillingDataSource
{
    private readonly IMasterPlanEmployeeConnectionProvider _connectionProvider =
        connectionProvider ?? throw new ArgumentNullException(nameof(connectionProvider));

    public async Task<ReplicaBillingProbe> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var source = MasterPlanReportSqlSourceResolver.RequireReplica(
            _connectionProvider.GetConnectionSettings());
        var (dataSource, catalog) = ReplicaConnectionStringSanitizer.ReadIdentity(source.ConnectionString);

        await using var conn = new SqlConnection(source.ConnectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        var (sqlServerName, sqlMachineName, sqlInstanceName, databaseName) =
            await LoadServerIdentityAsync(conn, cancellationToken).ConfigureAwait(false);

        var missingTables = new List<string>();
        foreach (var table in BillingReplicaRequirements.RequiredTables)
        {
            if (!await TableExistsAsync(conn, table, cancellationToken).ConfigureAwait(false))
                missingTables.Add(table);
        }

        var syncPresent = !missingTables.Exists(t =>
            t.Equals("Sync_State", StringComparison.OrdinalIgnoreCase));
        var syncTimes = syncPresent
            ? await LoadSyncTimesAsync(conn, cancellationToken).ConfigureAwait(false)
            : new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase);

        DateTime? GetSync(string entity) =>
            syncTimes.TryGetValue(entity, out var value) ? value : null;

        var maxHours = await LoadMaxDateAsync(
            conn, "MP_ProjectHoursExtended", "ReportDate", cancellationToken).ConfigureAwait(false);
        var maxProjects = await LoadMaxDateAsync(
            conn, "MP_Projects", "LastUpdated", cancellationToken).ConfigureAwait(false);
        var maxBills = await LoadMaxDateAsync(
            conn, "MP_Bills", "LastUpdated", cancellationToken).ConfigureAwait(false);
        var maxIntakes = await LoadMaxDateAsync(
            conn, "MP_Intakes", "LastUpdated", cancellationToken).ConfigureAwait(false);

        var diagnostics = new ReplicaConnectionDiagnostics(
            ConfiguredDataSource: dataSource,
            InitialCatalog: catalog,
            SqlServerName: sqlServerName,
            SqlMachineName: sqlMachineName,
            SqlInstanceName: sqlInstanceName,
            DatabaseName: databaseName,
            ProjectsSyncTime: GetSync("Projects"),
            BillsSyncTime: GetSync("Bills"),
            IntakesSyncTime: GetSync("Intakes"),
            ProjectHoursSyncTime: GetSync("ProjectHours"),
            ProjectHoursExtendedSyncTime: GetSync("ProjectHoursExtended"),
            MaxHoursReportDate: maxHours,
            MaxProjectsLastUpdated: maxProjects,
            MaxBillsLastUpdated: maxBills,
            MaxIntakesLastUpdated: maxIntakes);

        return new ReplicaBillingProbe(
            diagnostics,
            missingTables,
            syncPresent,
            syncTimes);
    }

    public async Task<ReplicaBillingSnapshot> LoadFactsAsync(
        BillingDashboardRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var source = MasterPlanReportSqlSourceResolver.RequireReplica(
            _connectionProvider.GetConnectionSettings());
        var asOf = (request.AsOfDate ?? DateTime.Today).Date;

        await using var conn = new SqlConnection(source.ConnectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        if (!await TableExistsAsync(conn, "MP_ProjectHoursExtended", cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "Replica table MP_ProjectHoursExtended is missing. Billing hours are Replica Extended-only; there is no live MasterPlan or MP_ProjectHours fallback.");
        }

        if (!await TableExistsAsync(conn, "MP_Projects", cancellationToken).ConfigureAwait(false)
            || !await TableExistsAsync(conn, "MP_Bills", cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "Replica MP_Projects / MP_Bills are required for the Billing Control Center.");
        }

        var projects = await LoadProjectsAsync(conn, request, cancellationToken).ConfigureAwait(false);
        var bills = await LoadBillsAsync(conn, cancellationToken).ConfigureAwait(false);
        var hours = await LoadHoursAsync(conn, cancellationToken).ConfigureAwait(false);
        var receivedThisMonth = await LoadReceivedThisMonthAsync(conn, asOf, cancellationToken)
            .ConfigureAwait(false);
        var onlyInBasic = await LoadHoursParityCountAsync(conn, cancellationToken).ConfigureAwait(false);

        return new ReplicaBillingSnapshot(
            projects,
            bills,
            hours,
            receivedThisMonth,
            onlyInBasic);
    }

    private static async Task<(string? ServerName, string? MachineName, string? InstanceName, string? DatabaseName)>
        LoadServerIdentityAsync(SqlConnection conn, CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT
              CAST(@@SERVERNAME AS nvarchar(256)),
              CAST(SERVERPROPERTY(N'MachineName') AS nvarchar(256)),
              CAST(SERVERPROPERTY(N'InstanceName') AS nvarchar(256)),
              DB_NAME()
            """;
        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return (null, null, null, null);

        return (
            ReadString(reader, 0),
            ReadString(reader, 1),
            ReadString(reader, 2),
            ReadString(reader, 3));
    }

    private static async Task<Dictionary<string, DateTime?>> LoadSyncTimesAsync(
        SqlConnection conn,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT EntityName, LastSyncTime
            FROM Sync_State
            WHERE EntityName IN (N'Projects', N'Bills', N'Intakes', N'ProjectHoursExtended', N'ProjectHours', N'MonthlyRestore')
            """;
        await using var cmd = new SqlCommand(sql, conn);
        var times = new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var name = reader.GetString(0);
            times[name] = reader.IsDBNull(1) ? null : reader.GetDateTime(1);
        }

        return times;
    }

    private static async Task<DateTime?> LoadMaxDateAsync(
        SqlConnection conn,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(conn, tableName, cancellationToken).ConfigureAwait(false))
            return null;

        var sql = $"SELECT MAX([{columnName}]) FROM [{tableName}]";
        await using var cmd = new SqlCommand(sql, conn);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is null or DBNull)
            return null;
        return Convert.ToDateTime(result);
    }

    private static async Task<IReadOnlyList<BillingProjectFact>> LoadProjectsAsync(
        SqlConnection conn,
        BillingDashboardRequest request,
        CancellationToken cancellationToken)
    {
        var sql =
            """
            SELECT
              p.ID,
              p.ProjectNum,
              p.Name,
              p.CustomerName,
              p.CustomerID,
              p.StatusName,
              CASE WHEN ISNULL(p.IsActive, 0) = 1 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END,
              p.FeeSum
            FROM MP_Projects p
            WHERE 1 = 1
            """;
        if (request.ActiveOnly)
            sql += " AND ISNULL(p.IsActive, 0) = 1";

        await using var cmd = new SqlCommand(sql, conn);
        AppendIdFilter(cmd, ref sql, "p.ID", "@P", request.ProjectIds);
        AppendIdFilter(cmd, ref sql, "p.CustomerID", "@C", request.CustomerIds);
        cmd.CommandText = sql;

        var list = new List<BillingProjectFact>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new BillingProjectFact(
                ProjectId: reader.GetInt32(0),
                ProjectNumber: reader.IsDBNull(1) ? null : reader.GetString(1),
                ProjectName: reader.IsDBNull(2) ? null : reader.GetString(2),
                CustomerName: reader.IsDBNull(3) ? null : NullIfWhiteSpace(reader.GetString(3)),
                CustomerId: reader.IsDBNull(4) ? null : reader.GetInt32(4),
                ProjectStatus: reader.IsDBNull(5) ? null : reader.GetString(5),
                IsActive: ReadBoolish(reader, 6),
                CurrentFeeSum: reader.IsDBNull(7) ? null : Convert.ToDecimal(reader.GetValue(7))));
        }

        return list;
    }

    private static async Task<IReadOnlyList<BillingBillFact>> LoadBillsAsync(
        SqlConnection conn,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT
              b.ID,
              b.ProjectID,
              b.BillNum,
              b.[Sum],
              b.StatusID,
              b.Status,
              b.SubmitDate,
              b.LastUpdated
            FROM MP_Bills b
            WHERE b.ProjectID IS NOT NULL
            """;

        await using var cmd = new SqlCommand(sql, conn);
        var list = new List<BillingBillFact>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new BillingBillFact(
                BillId: reader.GetInt32(0),
                ProjectId: reader.GetInt32(1),
                BillNumber: reader.IsDBNull(2) ? null : reader.GetString(2),
                Sum: reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                StatusId: reader.IsDBNull(4) ? null : reader.GetInt32(4),
                Status: reader.IsDBNull(5) ? null : reader.GetString(5),
                SubmitDate: reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                LastUpdated: reader.IsDBNull(7) ? null : reader.GetDateTime(7)));
        }

        return list;
    }

    private static async Task<IReadOnlyList<BillingHourFact>> LoadHoursAsync(
        SqlConnection conn,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT
              ph.ProjectID,
              CAST(ph.ReportDate AS date),
              ph.StartTime,
              ph.EndTime,
              CASE
                WHEN ph.Duration IS NOT NULL AND ph.Duration >= 0 AND ph.Duration <= 24 THEN ph.Duration
                ELSE NULL
              END,
              ph.TotalHours
            FROM MP_ProjectHoursExtended ph
            WHERE ph.ProjectID IS NOT NULL
              AND ph.ReportDate IS NOT NULL
            """;

        await using var cmd = new SqlCommand(sql, conn);
        var list = new List<BillingHourFact>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var start = reader.IsDBNull(2) ? null : MasterPlanHoursNormalizer.ReadTimeValue(reader.GetValue(2));
            var end = reader.IsDBNull(3) ? null : MasterPlanHoursNormalizer.ReadTimeValue(reader.GetValue(3));
            var duration = reader.IsDBNull(4) ? null : reader.GetValue(4);
            var totalHours = reader.IsDBNull(5) ? null : reader.GetValue(5);
            var hours = MasterPlanHoursNormalizer.ConvertExtendedHours(duration, totalHours, start, end);
            list.Add(new BillingHourFact(
                ProjectId: reader.GetInt32(0),
                ReportDate: reader.GetDateTime(1),
                Hours: hours));
        }

        return list;
    }

    private static async Task<decimal?> LoadReceivedThisMonthAsync(
        SqlConnection conn,
        DateTime asOf,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(conn, "MP_Intakes", cancellationToken).ConfigureAwait(false))
            return null;

        const string sql =
            """
            SELECT SUM([Sum])
            FROM MP_Intakes
            WHERE OpenDate >= @MonthStart AND OpenDate < @MonthEnd
            """;
        await using var cmd = new SqlCommand(sql, conn);
        var monthStart = new DateTime(asOf.Year, asOf.Month, 1);
        cmd.Parameters.Add("@MonthStart", SqlDbType.DateTime2).Value = monthStart;
        cmd.Parameters.Add("@MonthEnd", SqlDbType.DateTime2).Value = monthStart.AddMonths(1);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is null or DBNull)
            return 0m;
        return Convert.ToDecimal(result);
    }

    private static async Task<int> LoadHoursParityCountAsync(
        SqlConnection conn,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(conn, "MP_ProjectHours", cancellationToken).ConfigureAwait(false))
            return 0;

        const string sql =
            """
            SELECT COUNT(1)
            FROM MP_ProjectHours h
            WHERE NOT EXISTS (
              SELECT 1 FROM MP_ProjectHoursExtended x WHERE x.ID = h.ID)
            """;
        await using var cmd = new SqlCommand(sql, conn);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is int i ? i : Convert.ToInt32(result);
    }

    private static void AppendIdFilter(
        SqlCommand cmd,
        ref string sql,
        string column,
        string prefix,
        IReadOnlyList<int>? ids)
    {
        if (ids is not { Count: > 0 })
            return;

        var names = new string[ids.Count];
        for (var i = 0; i < ids.Count; i++)
        {
            names[i] = prefix + i;
            cmd.Parameters.Add(names[i], SqlDbType.Int).Value = ids[i];
        }

        sql += $" AND {column} IN ({string.Join(",", names)})";
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

    private static string? ReadString(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : NullIfWhiteSpace(reader.GetString(ordinal));

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool ReadBoolish(SqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return false;
        var value = reader.GetValue(ordinal);
        return value switch
        {
            bool b => b,
            int i => i != 0,
            byte by => by != 0,
            _ => Convert.ToInt32(value) != 0,
        };
    }
}
