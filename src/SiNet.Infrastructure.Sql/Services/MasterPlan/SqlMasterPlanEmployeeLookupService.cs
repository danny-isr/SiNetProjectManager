using Microsoft.Data.SqlClient;
using SiNet.Application.Identity;

namespace SiNet.Infrastructure.Sql.Services.MasterPlan;

/// <summary>
/// Native MasterPlan employee lookup for user admin. Queries configured SQL sources via
/// <see cref="IMasterPlanEmployeeConnectionProvider"/> — no legacy MVVM or GoogleConnector references.
/// </summary>
public sealed class SqlMasterPlanEmployeeLookupService : IMasterPlanEmployeeLookupService
{
    private const string ReplicaSourceKey = "Replica";
    private const string MasterPlanSourceKey = "MasterPlan";

    private readonly IMasterPlanEmployeeConnectionProvider _connectionProvider;

    public SqlMasterPlanEmployeeLookupService(IMasterPlanEmployeeConnectionProvider connectionProvider)
    {
        _connectionProvider = connectionProvider ?? throw new ArgumentNullException(nameof(connectionProvider));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MasterPlanEmployeeDto>> GetEmployeesAsync(
        bool includeNoMappingOption = true,
        CancellationToken cancellationToken = default)
    {
        var settings = _connectionProvider.GetConnectionSettings();
        var merged = new Dictionary<int, MasterPlanEmployeeDto>();

        if (!string.IsNullOrWhiteSpace(settings.ReplicaDatabase))
        {
            await MergeEmployeesAsync(
                settings.ReplicaDatabase,
                ReplicaSourceKey,
                ReplicaEmployeesSql,
                preferOnDuplicate: true,
                merged,
                cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(settings.MasterPlanDatabase))
        {
            await MergeEmployeesAsync(
                settings.MasterPlanDatabase,
                MasterPlanSourceKey,
                MasterPlanEmployeesSql,
                preferOnDuplicate: false,
                merged,
                cancellationToken).ConfigureAwait(false);
        }

        var rows = merged.Values
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (!includeNoMappingOption)
        {
            return rows;
        }

        var result = new List<MasterPlanEmployeeDto>(rows.Count + 1)
        {
            new(null, "-- ללא קישור --"),
        };
        result.AddRange(rows);
        return result;
    }

    private static async Task MergeEmployeesAsync(
        string connectionString,
        string sourceKey,
        string sql,
        bool preferOnDuplicate,
        Dictionary<int, MasterPlanEmployeeDto> merged,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = reader.GetInt32(0);
            var name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var dto = new MasterPlanEmployeeDto(id, name, Email: null, SourceDatabase: sourceKey);
            if (preferOnDuplicate || !merged.ContainsKey(id))
            {
                merged[id] = dto;
            }
        }
    }

    // Replica employee union query (legacy R03 report semantics).
    private const string ReplicaEmployeesSql = """
        SELECT DISTINCT EmployeeID AS Id, EmployeeName AS Name
        FROM (
            SELECT EmployeeID, EmployeeName FROM MP_TimeHourReports WHERE EmployeeID IS NOT NULL
            UNION
            SELECT EmployeeID, EmployeeName FROM MP_ProjectHoursExtended WHERE EmployeeID IS NOT NULL
        ) AS combined
        WHERE EmployeeName IS NOT NULL AND EmployeeName <> ''
        """;

    // Native MasterPlan dbo.Employees query (legacy R02 report semantics).
    private const string MasterPlanEmployeesSql = """
        SELECT
            ID AS Id,
            LTRIM(RTRIM(ISNULL(FirstName, '') + ' ' + ISNULL(LastName, ''))) AS Name
        FROM dbo.Employees
        WHERE ID IS NOT NULL
        """;
}
