using System.Globalization;
using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace MasterPlan.SyncEngine;

public sealed class OrphanPurgeRunResult
{
    public int OrphanCount { get; init; }
    public int PurgedCount { get; init; }
    public int DeferredCount { get; init; }
    public string? BlockReason { get; init; }
    public string? ArtifactPath { get; init; }
    public bool DryRun { get; init; }
}

/// <summary>
/// Loads orphan rows, evaluates DEV-019 gates, writes CSV, optionally DELETEs in batches.
/// </summary>
public sealed class OrphanPurgeRunner
{
    private readonly string _replicaConnectionString;
    private readonly OrphanPurgeOptions _options;
    private readonly OrphanSightingsStore _sightings;
    private readonly ILogger _logger;
    private readonly string _artifactDirectory;

    public OrphanPurgeRunner(
        string replicaConnectionString,
        OrphanPurgeOptions options,
        ILogger logger,
        OrphanSightingsStore? sightings = null,
        string? artifactDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replicaConnectionString);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _replicaConnectionString = replicaConnectionString;
        _options = options;
        _logger = logger;
        _sightings = sightings ?? new OrphanSightingsStore();
        _artifactDirectory = artifactDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "SiOffice",
                "MasterPlanSync",
                "orphan-purge");
    }

    public async Task<OrphanPurgeRunResult> RunAsync(
        string entityName,
        string tableName,
        string reportDateColumn,
        bool isFullReconcile,
        DateTime? fromDate,
        int fetchedCount,
        IReadOnlyCollection<int> apiIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportDateColumn);
        ArgumentNullException.ThrowIfNull(apiIds);

        if (!isFullReconcile || fromDate.HasValue)
        {
            return new OrphanPurgeRunResult { BlockReason = "G1_FULL_PULL_ONLY" };
        }

        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var hasLastUpdated = await ColumnExistsAsync(connection, tableName, "LastUpdated").ConfigureAwait(false);
        var hasProjectId = await ColumnExistsAsync(connection, tableName, "ProjectID").ConfigureAwait(false);
        var hasEmployeeId = await ColumnExistsAsync(connection, tableName, "EmployeeID").ConfigureAwait(false);

        var selectSql = BuildSelectSql(tableName, reportDateColumn, hasProjectId, hasEmployeeId, hasLastUpdated);
        var allRows = (await connection.QueryAsync<OrphanReplicaRow>(selectSql).ConfigureAwait(false)).AsList();
        var replicaRowCount = allRows.Count;
        var apiIdSet = apiIds.ToHashSet();
        var orphans = allRows.Where(r => !apiIdSet.Contains(r.Id)).ToList();

        var previous = _sightings.Load(entityName);
        var evaluation = OrphanPurgeGate.Evaluate(
            isFullReconcile: true,
            fromDate: null,
            fetchedCount,
            replicaRowCount,
            orphans,
            previous,
            _options,
            DateTime.UtcNow);

        if (evaluation.PersistSightings)
        {
            _sightings.Save(entityName, evaluation.SightingIdsToPersist);
        }

        var deferredCount = evaluation.DeferredAge.Count + evaluation.DeferredFirstSighting.Count;
        if (evaluation.DeferredAge.Count > 0)
        {
            _logger.LogWarning(
                "[ORPHAN-PURGE] {Entity}: OrphanDeferred age={Count} (ReportDate older than {Months} months). Sample: {Sample}",
                entityName,
                evaluation.DeferredAge.Count,
                _options.AgeWindowMonths,
                string.Join(", ", evaluation.DeferredAge.Take(20).Select(r => r.Id)));
        }

        if (evaluation.DeferredFirstSighting.Count > 0)
        {
            _logger.LogWarning(
                "[ORPHAN-PURGE] {Entity}: OrphanDeferred first-sighting={Count}. Sample: {Sample}",
                entityName,
                evaluation.DeferredFirstSighting.Count,
                string.Join(", ", evaluation.DeferredFirstSighting.Take(20).Select(r => r.Id)));
        }

        if (!evaluation.Allowed)
        {
            _logger.LogWarning(
                "[ORPHAN-PURGE] BLOCKED reason={Reason} entity={Entity} orphans={Orphans} fetched={Fetched} replica={Replica}",
                evaluation.BlockReason,
                entityName,
                evaluation.OrphanCount,
                evaluation.FetchedCount,
                evaluation.ReplicaRowCount);
            return new OrphanPurgeRunResult
            {
                OrphanCount = evaluation.OrphanCount,
                DeferredCount = deferredCount,
                BlockReason = evaluation.BlockReason
            };
        }

        string? artifactPath = null;
        if (evaluation.ToPurge.Count > 0
            && (_options.ShouldWriteDryRunArtifact || _options.ShouldDelete || _options.DryRun))
        {
            artifactPath = WriteCsv(entityName, evaluation.ToPurge);
            _logger.LogWarning(
                "[ORPHAN-PURGE] {Entity}: wrote artifact {Path} rows={Count}",
                entityName,
                artifactPath,
                evaluation.ToPurge.Count);
        }
        else if (evaluation.ToPurge.Count > 0 && !_options.ShouldDelete && !_options.DryRun)
        {
            // Report-only: log eligible IDs but do not delete or require CSV unless dry-run/purge.
            _logger.LogWarning(
                "[ORPHAN-PURGE] {Entity}: {Count} orphan(s) eligible after gates (report-only; use --purge-orphans-dry-run or enable+--purge-orphans). Sample: {Sample}",
                entityName,
                evaluation.ToPurge.Count,
                string.Join(", ", evaluation.ToPurge.Take(20).Select(r => r.Id)));
        }

        if (_options.DryRun)
        {
            if (evaluation.ToPurge.Count > 0 && artifactPath is null)
            {
                artifactPath = WriteCsv(entityName, evaluation.ToPurge);
            }

            _logger.LogWarning(
                "[ORPHAN-PURGE] DRY-RUN entity={Entity} wouldDelete={Count} deferred={Deferred} artifact={Path}",
                entityName,
                evaluation.ToPurge.Count,
                deferredCount,
                artifactPath ?? "(none)");
            return new OrphanPurgeRunResult
            {
                OrphanCount = evaluation.OrphanCount,
                PurgedCount = 0,
                DeferredCount = deferredCount,
                ArtifactPath = artifactPath,
                DryRun = true
            };
        }

        if (!_options.ShouldDelete)
        {
            return new OrphanPurgeRunResult
            {
                OrphanCount = evaluation.OrphanCount,
                PurgedCount = 0,
                DeferredCount = deferredCount,
                ArtifactPath = artifactPath
            };
        }

        if (evaluation.ToPurge.Count == 0)
        {
            _logger.LogInformation("[ORPHAN-PURGE] {Entity}: gates passed; nothing to delete.", entityName);
            return new OrphanPurgeRunResult
            {
                OrphanCount = evaluation.OrphanCount,
                DeferredCount = deferredCount
            };
        }

        if (artifactPath is null)
        {
            artifactPath = WriteCsv(entityName, evaluation.ToPurge);
        }

        var deleted = await DeleteInBatchesAsync(connection, tableName, evaluation.ToPurge, cancellationToken)
            .ConfigureAwait(false);
        _logger.LogWarning(
            "[ORPHAN-PURGE] DELETED entity={Entity} count={Count} artifact={Path}",
            entityName,
            deleted,
            artifactPath);

        return new OrphanPurgeRunResult
        {
            OrphanCount = evaluation.OrphanCount,
            PurgedCount = deleted,
            DeferredCount = deferredCount,
            ArtifactPath = artifactPath
        };
    }

    private async Task<int> DeleteInBatchesAsync(
        SqlConnection connection,
        string tableName,
        IReadOnlyList<OrphanReplicaRow> rows,
        CancellationToken cancellationToken)
    {
        var deleted = 0;
        var batchSize = Math.Max(1, _options.DeleteBatchSize);
        for (var i = 0; i < rows.Count; i += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = rows.Skip(i).Take(batchSize).ToList();
            var ids = batch.Select(r => r.Id).ToArray();
            try
            {
                var affected = await connection.ExecuteAsync(
                    $"DELETE FROM [{tableName}] WHERE ID IN @Ids",
                    new { Ids = ids }).ConfigureAwait(false);
                deleted += affected;
                foreach (var id in ids)
                {
                    _logger.LogWarning("[ORPHAN-PURGE] deleted ID={Id} table={Table}", id, tableName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[ORPHAN-PURGE] DELETE aborted at batch starting index {Index} table={Table}",
                    i,
                    tableName);
                throw;
            }
        }

        return deleted;
    }

    private string WriteCsv(string entityName, IReadOnlyList<OrphanReplicaRow> rows)
    {
        Directory.CreateDirectory(_artifactDirectory);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(_artifactDirectory, $"orphan-purge-{entityName}-{stamp}.csv");
        var sb = new StringBuilder();
        sb.AppendLine("ID,ReportDate,ProjectID,EmployeeID,LastUpdated");
        foreach (var row in rows)
        {
            sb.Append(row.Id).Append(',')
                .Append(FormatDate(row.ReportDate)).Append(',')
                .Append(row.ProjectId?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',')
                .Append(row.EmployeeId?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',')
                .Append(FormatDate(row.LastUpdated))
                .AppendLine();
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        return path;
    }

    private static string FormatDate(DateTime? value)
        => value.HasValue
            ? value.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : "";

    private static string BuildSelectSql(
        string tableName,
        string reportDateColumn,
        bool hasProjectId,
        bool hasEmployeeId,
        bool hasLastUpdated)
    {
        var project = hasProjectId ? "ProjectID AS ProjectId" : "CAST(NULL AS INT) AS ProjectId";
        var employee = hasEmployeeId ? "EmployeeID AS EmployeeId" : "CAST(NULL AS INT) AS EmployeeId";
        var lastUpdated = hasLastUpdated ? "LastUpdated" : "CAST(NULL AS DATETIME2) AS LastUpdated";
        return $@"
            SELECT
                ID AS Id,
                [{reportDateColumn}] AS ReportDate,
                {project},
                {employee},
                {lastUpdated}
            FROM [{tableName}] WITH (NOLOCK)";
    }

    private static async Task<bool> ColumnExistsAsync(SqlConnection connection, string tableName, string columnName)
    {
        var count = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = @TableName AND COLUMN_NAME = @ColumnName",
            new { TableName = tableName, ColumnName = columnName }).ConfigureAwait(false);
        return count > 0;
    }
}
