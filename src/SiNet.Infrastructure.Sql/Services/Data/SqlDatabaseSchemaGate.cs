using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Data;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.Services.Data;

/// <summary>
/// Native schema gate (port of legacy <c>DatabaseSchemaValidator</c>) using
/// <see cref="SiNetSQLDbContext"/> already owned by Infrastructure.Sql.
/// </summary>
public sealed class SqlDatabaseSchemaGate(
    IDbContextFactory<SiNetSQLDbContext> dbFactory,
    IAppLogger? logger = null) : IDatabaseSchemaGate
{
    private static readonly string[] RequiredTables =
    [
        "TaskType",
        "ProjectAssignmentStatus",
        "ProjectAssignmentEvent",
        "UserSetting",
    ];

    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    private readonly IAppLogger? _logger = logger;

    public async Task<DatabaseSchemaGateResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        if (!await CanConnectAsync(context, cancellationToken).ConfigureAwait(false))
        {
            return new DatabaseSchemaGateResult(false, false, RequiredTables);
        }

        var missing = await GetMissingTablesAsync(context, cancellationToken).ConfigureAwait(false);
        var present = missing.Count == 0;
        return new DatabaseSchemaGateResult(true, present, missing);
    }

    private async Task<bool> CanConnectAsync(SiNetSQLDbContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.Warn($"DatabaseSchemaGate: CanConnect failed — {ex.Message}");
            return false;
        }
    }

    private async Task<IReadOnlyList<string>> GetMissingTablesAsync(
        SiNetSQLDbContext context,
        CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        try
        {
            foreach (var table in RequiredTables)
            {
                if (!await TableExistsAsync(context, table, cancellationToken).ConfigureAwait(false))
                {
                    missing.Add(table);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.Warn($"DatabaseSchemaGate: table enumeration failed — {ex.Message}");
            return RequiredTables;
        }

        return missing;
    }

    private static async Task<bool> TableExistsAsync(
        SiNetSQLDbContext context,
        string tableName,
        CancellationToken cancellationToken)
    {
        var sql = "SELECT COUNT(*) AS Value FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = {0}";
        var count = await context.Database
            .SqlQueryRaw<int>(sql, tableName)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return count > 0;
    }
}
