using Microsoft.Data.SqlClient;
using SiNet.Application.Identity;
using SiNet.Application.MasterPlan.Reports;

namespace SiNet.Infrastructure.Sql.Services.MasterPlan.Reports;

/// <summary>
/// R01 portfolio via <see cref="MasterPlanReportSqlSourceResolver"/> (DEV-025 Replica-first).
/// Replica <c>MP_Projects</c> is the product SoT; live MasterPlan KPIs are last-resort only.
/// </summary>
public sealed class SqlR01ReportDataSource(IMasterPlanEmployeeConnectionProvider connectionProvider)
    : IR01ReportDataSource
{
    private readonly IMasterPlanEmployeeConnectionProvider _connectionProvider =
        connectionProvider ?? throw new ArgumentNullException(nameof(connectionProvider));

    public async Task<IReadOnlyList<R01PortfolioRow>> GetPortfolioAsync(
        R01ReportRequest request,
        CancellationToken cancellationToken = default)
    {
        var source = MasterPlanReportSqlSourceResolver.Resolve(_connectionProvider.GetConnectionSettings());
        if (source.Kind == MasterPlanReportSqlSourceKind.Replica)
        {
            return await QueryReplicaAsync(source.ConnectionString, request, cancellationToken)
                .ConfigureAwait(false);
        }

        return await QueryMasterPlanAsync(source.ConnectionString, request, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<R01PortfolioRow>> QueryMasterPlanAsync(
        string cs,
        R01ReportRequest request,
        CancellationToken cancellationToken)
    {
        var sql =
            """
            SELECT
                p.ID,
                ISNULL(p.ProjectNum, ''),
                ISNULL(p.Name, ''),
                ISNULL(p.IsActive, 0),
                p.StartDate,
                p.EndDate,
                p.StatusID,
                ISNULL(ps.Name, ''),
                p.CustomerID,
                ISNULL(co.Name, ''),
                ped.FeeSum,
                ped.OpenBillSum,
                ped.ApprovedBillSum,
                ped.Balance,
                ped.LastBillDate,
                ped.HourReported,
                ped.HourAllotted,
                ped.ProgressPercentage,
                p.LastUpdated
            FROM dbo.Projects p
            LEFT JOIN dbo.ProjectsExtraData ped ON p.ID = ped.ProjectID
            LEFT JOIN dbo.Contacts ct ON p.CustomerID = ct.ID
            LEFT JOIN dbo.Companies co ON ct.CompanyID = co.ID
            LEFT JOIN dbo.ProjectStatuses ps ON p.StatusID = ps.ID
            WHERE 1=1
            """;
        if (request.ActiveOnly)
            sql += " AND p.IsActive = 1";
        if (request.CustomerIds is { Count: > 0 })
            sql += " AND ct.CompanyID IN (" + string.Join(",", request.CustomerIds) + ")";
        if (request.ProjectIds is { Count: > 0 })
            sql += " AND p.ID IN (" + string.Join(",", request.ProjectIds) + ")";
        sql += " ORDER BY p.ProjectNum, p.Name";

        return await ReadAsync(cs, sql, "MasterPlan", cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<R01PortfolioRow>> QueryReplicaAsync(
        string cs,
        R01ReportRequest request,
        CancellationToken cancellationToken)
    {
        var sql =
            """
            SELECT
                p.ID,
                ISNULL(p.ProjectNum, ''),
                ISNULL(p.Name, ''),
                CASE WHEN ISNULL(p.IsActive, 0) = 1 THEN 1 ELSE 0 END,
                CAST(NULL AS datetime),
                CAST(NULL AS datetime),
                CAST(NULL AS int),
                ISNULL(p.StatusName, ''),
                CAST(NULL AS int),
                ISNULL(p.CustomerName, ''),
                p.FeeSum,
                CAST(NULL AS decimal(18,2)),
                CAST(NULL AS decimal(18,2)),
                CAST(NULL AS decimal(18,2)),
                CAST(NULL AS datetime),
                CAST(NULL AS decimal(18,2)),
                CAST(NULL AS decimal(18,2)),
                CAST(NULL AS decimal(18,2)),
                CAST(NULL AS datetime)
            FROM MP_Projects p
            WHERE 1=1
            """;
        if (request.ActiveOnly)
            sql += " AND p.IsActive = 1";
        if (request.CustomerIds is { Count: > 0 })
            sql += " AND p.CustomerID IN (" + string.Join(",", request.CustomerIds) + ")";
        if (request.ProjectIds is { Count: > 0 })
            sql += " AND p.ID IN (" + string.Join(",", request.ProjectIds) + ")";
        sql += " ORDER BY p.ProjectNum, p.Name";

        return await ReadAsync(cs, sql, "Replica", cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<R01PortfolioRow>> ReadAsync(
        string cs,
        string sql,
        string source,
        CancellationToken cancellationToken)
    {
        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<R01PortfolioRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new R01PortfolioRow(
                ProjectId: reader.GetInt32(0),
                ProjectNum: reader.IsDBNull(1) ? null : reader.GetString(1),
                ProjectName: reader.IsDBNull(2) ? null : reader.GetString(2),
                IsActive: ReadBoolish(reader, 3),
                StartDate: reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                EndDate: reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                StatusId: reader.IsDBNull(6) ? null : reader.GetInt32(6),
                StatusName: reader.IsDBNull(7) ? null : reader.GetString(7),
                CustomerId: reader.IsDBNull(8) ? null : reader.GetInt32(8),
                CustomerName: reader.IsDBNull(9) ? null : reader.GetString(9),
                FeeSum: reader.IsDBNull(10) ? null : Convert.ToDecimal(reader.GetValue(10)),
                OpenBillSum: reader.IsDBNull(11) ? null : Convert.ToDecimal(reader.GetValue(11)),
                ApprovedBillSum: reader.IsDBNull(12) ? null : Convert.ToDecimal(reader.GetValue(12)),
                Balance: reader.IsDBNull(13) ? null : Convert.ToDecimal(reader.GetValue(13)),
                LastBillDate: reader.IsDBNull(14) ? null : reader.GetDateTime(14),
                HourReported: reader.IsDBNull(15) ? null : Convert.ToDecimal(reader.GetValue(15)),
                HourAllotted: reader.IsDBNull(16) ? null : Convert.ToDecimal(reader.GetValue(16)),
                ProgressPercentage: reader.IsDBNull(17) ? null : Convert.ToDecimal(reader.GetValue(17)),
                LastUpdated: reader.IsDBNull(18) ? null : reader.GetDateTime(18),
                DataSource: source));
        }

        return list;
    }

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
