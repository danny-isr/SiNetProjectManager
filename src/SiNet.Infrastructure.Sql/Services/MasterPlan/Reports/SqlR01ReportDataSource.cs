using Microsoft.Data.SqlClient;
using SiNet.Application.Identity;
using SiNet.Application.MasterPlan.Reports;

namespace SiNet.Infrastructure.Sql.Services.MasterPlan.Reports;

/// <summary>R01 portfolio from Replica MP_Projects (KPI columns mostly null — Legacy parity).</summary>
public sealed class SqlR01ReportDataSource(IMasterPlanEmployeeConnectionProvider connectionProvider)
    : IR01ReportDataSource
{
    private readonly IMasterPlanEmployeeConnectionProvider _connectionProvider =
        connectionProvider ?? throw new ArgumentNullException(nameof(connectionProvider));

    public async Task<IReadOnlyList<R01PortfolioRow>> GetPortfolioAsync(
        R01ReportRequest request,
        CancellationToken cancellationToken = default)
    {
        var cs = _connectionProvider.GetConnectionSettings().ReplicaDatabase;
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException("ReplicaDatabase connection string is not configured in the vault.");

        var sql =
            """
            SELECT
              p.ID,
              ISNULL(p.ProjectNum,''),
              ISNULL(p.Name,''),
              ISNULL(p.CustomerName,''),
              ISNULL(p.StatusName,''),
              p.FeeSum,
              CAST(NULL AS decimal(18,2)),
              CASE WHEN ISNULL(p.IsActive,0)=1 THEN 1 ELSE 0 END
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

        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<R01PortfolioRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new R01PortfolioRow(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                reader.GetInt32(7) == 1));
        }

        return list;
    }
}
