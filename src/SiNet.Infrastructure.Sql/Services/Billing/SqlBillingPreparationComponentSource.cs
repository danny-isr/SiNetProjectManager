using System.Data;
using Microsoft.Data.SqlClient;
using SiNet.Application.Billing;
using SiNet.Application.Identity;
using SiNet.Infrastructure.Sql.Services.MasterPlan;

namespace SiNet.Infrastructure.Sql.Services.Billing;

/// <summary>
/// Loads stages and hourly scopes from monthly <c>Db_Mp_SiEng</c>. Never reads SiNet PaymentsStep.
/// Observed stage progress is MAX(StepProgress) over bill statuses 2/3/4 — never SUM.
/// </summary>
public sealed class SqlBillingPreparationComponentSource(
    IMasterPlanEmployeeConnectionProvider connectionProvider) : IBillingPreparationComponentSource
{
    private readonly IMasterPlanEmployeeConnectionProvider _connectionProvider =
        connectionProvider ?? throw new ArgumentNullException(nameof(connectionProvider));

    public async Task<BillingPreparationSnapshotLoad> LoadAsync(
        int masterPlanProjectId,
        CancellationToken cancellationToken = default)
    {
        if (masterPlanProjectId <= 0)
            throw new ArgumentOutOfRangeException(nameof(masterPlanProjectId));

        var cs = _connectionProvider.GetConnectionSettings().MasterPlanDatabase;
        if (string.IsNullOrWhiteSpace(cs))
            return Empty(false);

        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        var required = new[] { "Projects", "Contracts", "SubContracts", "SubContractSteps" };
        foreach (var table in required)
        {
            if (!await TableExistsAsync(conn, table, cancellationToken).ConfigureAwait(false))
                return Empty(false);
        }

        var header = await LoadHeaderAsync(conn, masterPlanProjectId, cancellationToken).ConfigureAwait(false);
        var stages = await LoadStagesAsync(conn, masterPlanProjectId, cancellationToken).ConfigureAwait(false);
        var hourly = await LoadHourlySubContractsAsync(conn, masterPlanProjectId, cancellationToken)
            .ConfigureAwait(false);
        var hours = await LoadHourReportsAsync(conn, masterPlanProjectId, cancellationToken).ConfigureAwait(false);
        var backupUtc = await TryLoadBackupStampAsync(cancellationToken).ConfigureAwait(false);

        var snapshotAvailable = stages.Count > 0 || hourly.Count > 0;
        return new BillingPreparationSnapshotLoad(
            snapshotAvailable,
            backupUtc,
            header.CustomerName,
            header.ProjectNumber,
            header.ProjectName,
            stages,
            hourly,
            hours);
    }

    private static BillingPreparationSnapshotLoad Empty(bool available) =>
        new(available, null, null, null, null, [], [], []);

    private static async Task<(string? ProjectNumber, string? ProjectName, string? CustomerName)> LoadHeaderAsync(
        SqlConnection conn,
        int projectId,
        CancellationToken cancellationToken)
    {
        // MasterPlan Contacts has FirstName/LastName, not Name. Company customers use
        // Companies.Name; person customers fall back to CONCAT(FirstName, LastName).
        const string sql =
            """
            SELECT TOP (1)
              p.ProjectNum,
              p.Name,
              COALESCE(
                NULLIF(LTRIM(RTRIM(comp.Name)), ''),
                NULLIF(LTRIM(RTRIM(CONCAT(c.FirstName, ' ', c.LastName))), '')
              ) AS CustomerName
            FROM dbo.Projects p
            LEFT JOIN dbo.Contacts c ON c.ID = p.CustomerID
            LEFT JOIN dbo.Companies comp ON comp.ID = c.CompanyID
            WHERE p.ID = @ProjectId
            """;
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@ProjectId", SqlDbType.Int).Value = projectId;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return (null, null, null);

        var number = reader.IsDBNull(0) ? null : Convert.ToString(reader.GetValue(0));
        var name = reader.IsDBNull(1) ? null : reader.GetString(1);
        var customer = reader.IsDBNull(2) ? null : reader.GetString(2);
        return (number, name, customer);
    }

    private static async Task<IReadOnlyList<BillingPreparationStageDraft>> LoadStagesAsync(
        SqlConnection conn,
        int projectId,
        CancellationToken cancellationToken)
    {
        var hasBills = await TableExistsAsync(conn, "Bills", cancellationToken).ConfigureAwait(false)
                       && await TableExistsAsync(conn, "BillSubContracts", cancellationToken).ConfigureAwait(false)
                       && await TableExistsAsync(conn, "BillLines", cancellationToken).ConfigureAwait(false);

        var progressJoin = hasBills
            ? """
              LEFT JOIN (
                SELECT bl.StepID, bl.StepProgress, b.StatusID
                FROM dbo.BillLines bl
                INNER JOIN dbo.BillSubContracts bsc ON bsc.ID = bl.BillSubContractID
                INNER JOIN dbo.Bills b ON b.ID = bsc.BillID
                WHERE bl.StepID IS NOT NULL AND bl.StepProgress IS NOT NULL
              ) prog ON prog.StepID = st.ID
              """
            : "LEFT JOIN (SELECT CAST(NULL AS int) AS StepID, CAST(NULL AS float) AS StepProgress, CAST(NULL AS int) AS StatusID) prog ON 1 = 0";

        var sql =
            $"""
            SELECT
              st.ID,
              sc.ID,
              st.Name,
              sc.Name,
              st.Percentage,
              prog.StepProgress,
              prog.StatusID
            FROM dbo.SubContractSteps st
            INNER JOIN dbo.SubContracts sc ON sc.ID = st.SubContractID
            INNER JOIN dbo.Contracts c ON c.ID = sc.ContractID
            {progressJoin}
            WHERE c.ProjectID = @ProjectId
            ORDER BY sc.ID, st.OrderNum, st.ID
            """;

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@ProjectId", SqlDbType.Int).Value = projectId;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var buckets = new Dictionary<int, StageBucket>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var stageId = reader.GetInt32(0);
            if (!buckets.TryGetValue(stageId, out var bucket))
            {
                bucket = new StageBucket(
                    stageId,
                    reader.GetInt32(1),
                    reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    ReadDecimal(reader, 4) ?? 0m,
                    []);
                buckets[stageId] = bucket;
            }

            if (!reader.IsDBNull(5) && !reader.IsDBNull(6))
            {
                var statusId = reader.GetInt32(6);
                if (BillingAcceptedBillStatusIds.CountsAsSubmitted(statusId))
                    bucket.Progress.Add(ReadDecimal(reader, 5) ?? 0m);
            }
        }

        return buckets.Values
            .Select(b =>
            {
                var observed = BillingStageProgressCalculator.Observe(b.Progress);
                return new BillingPreparationStageDraft(
                    b.StageId,
                    b.SubContractId,
                    b.StageName,
                    b.SubContractName,
                    b.Weight,
                    observed,
                    observed.Value,
                    Included: false);
            })
            .ToList();
    }

    private static async Task<IReadOnlyList<BillingHourlySubContractDraft>> LoadHourlySubContractsAsync(
        SqlConnection conn,
        int projectId,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT sc.ID, sc.Name, sc.FeeTypeID
            FROM dbo.SubContracts sc
            INNER JOIN dbo.Contracts c ON c.ID = sc.ContractID
            WHERE c.ProjectID = @ProjectId AND sc.FeeTypeID = @FeeType
            ORDER BY sc.ID
            """;
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@ProjectId", SqlDbType.Int).Value = projectId;
        cmd.Parameters.Add("@FeeType", SqlDbType.Int).Value = MasterPlanSnapshotFeeTypeIds.WorkingHours;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<BillingHourlySubContractDraft>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new BillingHourlySubContractDraft(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                reader.GetInt32(2)));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<BillingHourReportFact>> LoadHourReportsAsync(
        SqlConnection conn,
        int projectId,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(conn, "HoursReports", cancellationToken).ConfigureAwait(false))
            return [];

        var hasEmployees = await TableExistsAsync(conn, "Employees", cancellationToken).ConfigureAwait(false);
        // MasterPlan Employees has FirstName/LastName, not Name (SqlMasterPlanEmployeeLookupService).
        var employeeJoin = hasEmployees
            ? "LEFT JOIN dbo.Employees e ON e.ID = hr.EmployeeID"
            : "LEFT JOIN (SELECT CAST(NULL AS int) AS ID, CAST(NULL AS nvarchar(200)) AS FirstName, CAST(NULL AS nvarchar(200)) AS LastName) e ON 1 = 0";

        var sql =
            $"""
            SELECT hr.ID, hr.ProjectID, hr.SubContractID, hr.SubContractStepID, hr.DateTime,
                   hr.EmployeeID,
                   LTRIM(RTRIM(CONCAT(e.FirstName, ' ', e.LastName))),
                   hr.Hours, hr.Description
            FROM dbo.HoursReports hr
            INNER JOIN dbo.SubContracts sc ON sc.ID = hr.SubContractID
            {employeeJoin}
            WHERE hr.ProjectID = @ProjectId AND sc.FeeTypeID = @FeeType
            """;
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@ProjectId", SqlDbType.Int).Value = projectId;
        cmd.Parameters.Add("@FeeType", SqlDbType.Int).Value = MasterPlanSnapshotFeeTypeIds.WorkingHours;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<BillingHourReportFact>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var hoursRaw = reader.IsDBNull(7) ? null : reader.GetValue(7);
            rows.Add(new BillingHourReportFact(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.IsDBNull(4) ? DateTime.MinValue : reader.GetDateTime(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.IsDBNull(6) ? null : Convert.ToString(reader.GetValue(6)),
                MasterPlanHoursNormalizer.ConvertHoursRaw(hoursRaw),
                reader.IsDBNull(8) ? null : Convert.ToString(reader.GetValue(8))));
        }

        return rows;
    }

    private async Task<DateTime?> TryLoadBackupStampAsync(CancellationToken cancellationToken)
    {
        var replica = _connectionProvider.GetConnectionSettings().ReplicaDatabase;
        if (string.IsNullOrWhiteSpace(replica))
            return null;

        try
        {
            await using var replicaConn = new SqlConnection(replica);
            await replicaConn.OpenAsync(cancellationToken).ConfigureAwait(false);
            if (!await TableExistsAsync(replicaConn, "Sync_State", cancellationToken).ConfigureAwait(false))
                return null;

            const string sql =
                """
                SELECT TOP (1) LastSync
                FROM dbo.Sync_State
                WHERE EntityName = N'MonthlyRestore'
                ORDER BY LastSync DESC
                """;
            await using var cmd = new SqlCommand(sql, replicaConn);
            var value = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return value is DateTime dt ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : null;
        }
        catch (SqlException)
        {
            return null;
        }
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

    private static decimal? ReadDecimal(SqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return null;
        return Convert.ToDecimal(reader.GetValue(ordinal));
    }

    private sealed record StageBucket(
        int StageId,
        int SubContractId,
        string StageName,
        string SubContractName,
        decimal Weight,
        List<decimal> Progress);
}
