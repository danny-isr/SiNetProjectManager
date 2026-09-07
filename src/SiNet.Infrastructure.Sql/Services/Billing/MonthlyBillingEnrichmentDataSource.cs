using System.Data;
using Microsoft.Data.SqlClient;
using SiNet.Application.Billing;
using SiNet.Application.Identity;

namespace SiNet.Infrastructure.Sql.Services.Billing;

/// <summary>
/// Loads <c>ProjectsExtraData</c>, customer fallback, and SubContract fee types from
/// vault <c>MasterPlanDatabase</c> (<c>Db_Mp_SiEng</c>). Does not read Replica current facts.
/// </summary>
public sealed class MonthlyBillingEnrichmentDataSource(
    IMasterPlanEmployeeConnectionProvider connectionProvider) : IMonthlyBillingEnrichmentDataSource
{
    private readonly IMasterPlanEmployeeConnectionProvider _connectionProvider =
        connectionProvider ?? throw new ArgumentNullException(nameof(connectionProvider));

    public async Task<MonthlyBillingEnrichmentLoad> LoadAsync(
        IReadOnlyList<int> projectIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectIds);

        var cs = _connectionProvider.GetConnectionSettings().MasterPlanDatabase;
        if (string.IsNullOrWhiteSpace(cs))
            return MonthlyBillingEnrichmentLoad.Unavailable;

        if (projectIds.Count == 0)
            return new MonthlyBillingEnrichmentLoad(true, Array.Empty<string>(), new Dictionary<int, BillingSnapshotProjectEnrichment>());

        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        var missing = new List<string>();
        foreach (var table in new[] { "ProjectsExtraData", "Projects", "Contacts", "Companies", "Contracts", "SubContracts" })
        {
            if (!await TableExistsAsync(conn, table, cancellationToken).ConfigureAwait(false))
                missing.Add(table);
        }

        var extra = missing.Exists(t => t.Equals("ProjectsExtraData", StringComparison.OrdinalIgnoreCase))
            ? new Dictionary<int, ExtraRow>()
            : await LoadExtraAsync(conn, projectIds, cancellationToken).ConfigureAwait(false);

        var customers = missing.Exists(t =>
                t.Equals("Projects", StringComparison.OrdinalIgnoreCase)
                || t.Equals("Contacts", StringComparison.OrdinalIgnoreCase)
                || t.Equals("Companies", StringComparison.OrdinalIgnoreCase))
            ? new Dictionary<int, string?>()
            : await LoadCustomersAsync(conn, projectIds, cancellationToken).ConfigureAwait(false);

        var fees = missing.Exists(t =>
                t.Equals("Contracts", StringComparison.OrdinalIgnoreCase)
                || t.Equals("SubContracts", StringComparison.OrdinalIgnoreCase))
            ? new Dictionary<int, List<int>>()
            : await LoadFeeTypesAsync(conn, projectIds, cancellationToken).ConfigureAwait(false);

        var keys = extra.Keys
            .Concat(customers.Keys)
            .Concat(fees.Keys)
            .Distinct()
            .ToList();

        var projects = new Dictionary<int, BillingSnapshotProjectEnrichment>();
        foreach (var id in keys)
        {
            extra.TryGetValue(id, out var row);
            customers.TryGetValue(id, out var customer);
            fees.TryGetValue(id, out var feeIds);
            projects[id] = new BillingSnapshotProjectEnrichment(
                id,
                row?.Balance,
                row?.OpenBillSum,
                row?.ApprovedBillSum,
                row?.ProgressPercentage,
                customer,
                feeIds ?? (IReadOnlyList<int>)Array.Empty<int>());
        }

        return new MonthlyBillingEnrichmentLoad(true, missing, projects);
    }

    private static async Task<Dictionary<int, ExtraRow>> LoadExtraAsync(
        SqlConnection conn,
        IReadOnlyList<int> projectIds,
        CancellationToken cancellationToken)
    {
        var sql =
            """
            SELECT
              ped.ProjectID,
              ped.Balance,
              ped.OpenBillSum,
              ped.ApprovedBillSum,
              ped.ProgressPercentage
            FROM dbo.ProjectsExtraData ped
            WHERE 1 = 1
            """;
        await using var cmd = new SqlCommand(sql, conn);
        AppendIdFilter(cmd, ref sql, "ped.ProjectID", "@E", projectIds);
        cmd.CommandText = sql;

        var map = new Dictionary<int, ExtraRow>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = reader.GetInt32(0);
            map[id] = new ExtraRow(
                ReadDecimal(reader, 1),
                ReadDecimal(reader, 2),
                ReadDecimal(reader, 3),
                ReadDecimal(reader, 4));
        }

        return map;
    }

    private static async Task<Dictionary<int, string?>> LoadCustomersAsync(
        SqlConnection conn,
        IReadOnlyList<int> projectIds,
        CancellationToken cancellationToken)
    {
        var sql =
            """
            SELECT p.ID, co.Name
            FROM dbo.Projects p
            LEFT JOIN dbo.Contacts ct ON p.CustomerID = ct.ID
            LEFT JOIN dbo.Companies co ON ct.CompanyID = co.ID
            WHERE 1 = 1
            """;
        await using var cmd = new SqlCommand(sql, conn);
        AppendIdFilter(cmd, ref sql, "p.ID", "@C", projectIds);
        cmd.CommandText = sql;

        var map = new Dictionary<int, string?>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = reader.GetInt32(0);
            var name = reader.IsDBNull(1) ? null : reader.GetString(1);
            map[id] = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }

        return map;
    }

    private static async Task<Dictionary<int, List<int>>> LoadFeeTypesAsync(
        SqlConnection conn,
        IReadOnlyList<int> projectIds,
        CancellationToken cancellationToken)
    {
        var sql =
            """
            SELECT c.ProjectID, sc.FeeTypeID
            FROM dbo.SubContracts sc
            INNER JOIN dbo.Contracts c ON c.ID = sc.ContractID
            WHERE c.ProjectID IS NOT NULL
            """;
        await using var cmd = new SqlCommand(sql, conn);
        AppendIdFilter(cmd, ref sql, "c.ProjectID", "@F", projectIds);
        cmd.CommandText = sql;

        var map = new Dictionary<int, List<int>>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.IsDBNull(1))
                continue;
            var projectId = reader.GetInt32(0);
            var feeTypeId = reader.GetInt32(1);
            if (!map.TryGetValue(projectId, out var list))
            {
                list = [];
                map[projectId] = list;
            }

            list.Add(feeTypeId);
        }

        return map;
    }

    private static void AppendIdFilter(
        SqlCommand cmd,
        ref string sql,
        string column,
        string prefix,
        IReadOnlyList<int> ids)
    {
        if (ids.Count == 0)
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

    private static decimal? ReadDecimal(SqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return null;
        return Convert.ToDecimal(reader.GetValue(ordinal));
    }

    private sealed record ExtraRow(
        decimal? Balance,
        decimal? OpenBillSum,
        decimal? ApprovedBillSum,
        decimal? ProgressPercentage);
}
