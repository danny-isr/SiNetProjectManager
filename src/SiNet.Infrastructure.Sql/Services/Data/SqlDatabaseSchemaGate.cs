using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Data;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.Services.Data;

/// <summary>
/// Native schema gate (port of legacy <c>DatabaseSchemaValidator</c>) using
/// <see cref="SiNetSQLDbContext"/> already owned by Infrastructure.Sql.
/// Also fails closed when EF reports pending migrations vs the deployed assembly.
/// </summary>
public sealed class SqlDatabaseSchemaGate : IDatabaseSchemaGate
{
    private static readonly string[] RequiredTables =
    [
        "TaskType",
        "ProjectAssignmentStatus",
        "ProjectAssignmentEvent",
        "UserSetting",
    ];

    private static readonly IReadOnlyList<string> NoPending = Array.Empty<string>();

    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;
    private readonly IAppLogger? _logger;
    private readonly Func<SiNetSQLDbContext, CancellationToken, Task<IReadOnlyList<string>>> _pendingMigrationsAsync;

    public SqlDatabaseSchemaGate(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        IAppLogger? logger = null)
        : this(dbFactory, logger, QueryPendingMigrationsAsync)
    {
    }

    /// <summary>Test seam: inject pending-migration reader without a real SQL Server.</summary>
    internal SqlDatabaseSchemaGate(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        IAppLogger? logger,
        Func<SiNetSQLDbContext, CancellationToken, Task<IReadOnlyList<string>>> pendingMigrationsAsync)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _logger = logger;
        _pendingMigrationsAsync = pendingMigrationsAsync
            ?? throw new ArgumentNullException(nameof(pendingMigrationsAsync));
    }

    public async Task<DatabaseSchemaGateResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        if (!await CanConnectAsync(context, cancellationToken).ConfigureAwait(false))
        {
            return Evaluate(canConnect: false, missingTables: RequiredTables, pendingMigrations: NoPending);
        }

        var missing = await GetMissingTablesAsync(context, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<string> pending;
        try
        {
            pending = await _pendingMigrationsAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.Warn($"DatabaseSchemaGate: pending migrations probe failed — {ex.Message}");
            // Fail closed: treat probe failure as "unknown / not ready".
            pending = ["(pending-migrations-probe-failed)"];
        }

        return Evaluate(canConnect: true, missingTables: missing, pendingMigrations: pending);
    }

    /// <summary>Pure combine of connect / tables / pending — unit-tested without SQL.</summary>
    internal static DatabaseSchemaGateResult Evaluate(
        bool canConnect,
        IReadOnlyList<string> missingTables,
        IReadOnlyList<string> pendingMigrations)
    {
        ArgumentNullException.ThrowIfNull(missingTables);
        ArgumentNullException.ThrowIfNull(pendingMigrations);

        if (!canConnect)
        {
            return new DatabaseSchemaGateResult(false, false, missingTables, NoPending);
        }

        return new DatabaseSchemaGateResult(
            true,
            missingTables.Count == 0,
            missingTables,
            pendingMigrations);
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

    private static async Task<IReadOnlyList<string>> QueryPendingMigrationsAsync(
        SiNetSQLDbContext context,
        CancellationToken cancellationToken)
    {
        var pending = await context.Database
            .GetPendingMigrationsAsync(cancellationToken)
            .ConfigureAwait(false);
        return pending as IReadOnlyList<string> ?? pending.ToList();
    }
}
