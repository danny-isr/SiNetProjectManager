using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Dapper;

namespace MasterPlan.SyncEngine;

/// <summary>
/// Legacy database sync manager for direct database-to-database synchronization.
/// This is kept for backward compatibility but the recommended approach is to use
/// the API-based sync (ApiDailySyncService) for daily incremental updates.
/// </summary>
public class DatabaseSyncManager
{
    private readonly string _sourceConnectionString;
    private readonly string _replicaConnectionString;
    private readonly string _masterConnectionString;
    private readonly ILogger<DatabaseSyncManager> _logger;

    public DatabaseSyncManager(
        string sourceConnectionString,
        string replicaConnectionString,
        string masterConnectionString,
        ILogger<DatabaseSyncManager> logger)
    {
        _sourceConnectionString = sourceConnectionString;
        _replicaConnectionString = replicaConnectionString;
        _masterConnectionString = masterConnectionString;
        _logger = logger;
    }

    /// <summary>
    /// Run daily sync directly from source database (legacy mode).
    /// This performs incremental sync based on LastUpdated timestamps.
    /// </summary>
    public async Task RunDailySyncAsync()
    {
        _logger.LogInformation("Starting legacy database-to-database sync");

        try
        {
            await using var sourceConn = new SqlConnection(_sourceConnectionString);
            await using var replicaConn = new SqlConnection(_replicaConnectionString);
            await sourceConn.OpenAsync();
            await replicaConn.OpenAsync();

            // Get watermarks and sync each entity
            await SyncEntityAsync<ProjectSyncRecord>(sourceConn, replicaConn, "Projects", "MP_Projects");
            await SyncEntityAsync<CompanySyncRecord>(sourceConn, replicaConn, "Companies", "MP_Companies");
            await SyncEntityAsync<ContactSyncRecord>(sourceConn, replicaConn, "Contacts", "MP_Contacts");
            await SyncEntityAsync<EmployeeSyncRecord>(sourceConn, replicaConn, "Employees", "MP_Employees");

            _logger.LogInformation("Legacy database sync completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Legacy database sync failed");
            throw;
        }
    }

    private async Task SyncEntityAsync<T>(
        SqlConnection source,
        SqlConnection replica,
        string entityName,
        string targetTable) where T : class
    {
        _logger.LogInformation("Syncing {Entity}...", entityName);

        // Get the watermark
        var watermark = await replica.ExecuteScalarAsync<DateTime?>(
            "SELECT LastWatermark FROM Sync_State WHERE EntityName = @Entity",
            new { Entity = entityName });

        // Query source for changes
        var sourceQuery = BuildSourceQuery(entityName, watermark);
        var records = (await source.QueryAsync<T>(sourceQuery)).ToList();

        _logger.LogInformation("{Entity}: Found {Count} records to sync", entityName, records.Count);

        if (records.Count == 0) return;

        // Upsert records
        foreach (var record in records)
        {
            await UpsertRecordAsync(replica, targetTable, record);
        }

        // Update watermark
        var newWatermark = GetMaxLastUpdated(records);
        if (newWatermark.HasValue)
        {
            await UpdateWatermarkAsync(replica, entityName, newWatermark.Value);
        }
    }

    private string BuildSourceQuery(string entityName, DateTime? watermark)
    {
        var baseQuery = entityName switch
        {
            "Projects" => "SELECT ProjectID as ID, ProjectName as Name, LastUpdated FROM Projects WITH (NOLOCK)",
            "Companies" => "SELECT CompanyID as ID, CompanyName as Name, LastUpdated FROM Companies WITH (NOLOCK)",
            "Contacts" => "SELECT ContactID as ID, FirstName, LastName, LastUpdated FROM Contacts WITH (NOLOCK)",
            "Employees" => "SELECT EmployeeID as ID, FirstName, LastName, LastUpdated FROM Employees WITH (NOLOCK)",
            _ => throw new ArgumentException($"Unknown entity: {entityName}")
        };

        if (watermark.HasValue)
        {
            baseQuery += $" WHERE LastUpdated >= '{watermark.Value:yyyy-MM-dd HH:mm:ss}'";
        }

        return baseQuery;
    }

    private async Task UpsertRecordAsync<T>(SqlConnection replica, string tableName, T record)
    {
        // Generic upsert logic - simplified for demo
        var id = GetRecordId(record);
        var exists = await replica.ExecuteScalarAsync<int>(
            $"SELECT COUNT(1) FROM {tableName} WHERE ID = @Id",
            new { Id = id });

        // This is a simplified implementation - full implementation would use dynamic column mapping
        _logger.LogDebug("Upserting record ID {Id} into {Table}", id, tableName);
    }

    private int GetRecordId<T>(T record)
    {
        var prop = typeof(T).GetProperty("ID") ?? typeof(T).GetProperty("Id");
        return (int)(prop?.GetValue(record) ?? 0);
    }

    private DateTime? GetMaxLastUpdated<T>(IEnumerable<T> records)
    {
        var prop = typeof(T).GetProperty("LastUpdated");
        if (prop == null) return null;

        return records
            .Select(r => prop.GetValue(r) as DateTime?)
            .Where(d => d.HasValue)
            .Max();
    }

    private async Task UpdateWatermarkAsync(SqlConnection replica, string entityName, DateTime watermark)
    {
        await replica.ExecuteAsync(@"
            MERGE Sync_State AS target
            USING (SELECT @EntityName AS EntityName) AS source
            ON target.EntityName = source.EntityName
            WHEN MATCHED THEN
                UPDATE SET LastWatermark = @Watermark, LastSyncTime = GETUTCDATE(), UpdatedAt = GETUTCDATE()
            WHEN NOT MATCHED THEN
                INSERT (EntityName, LastWatermark, LastSyncTime, UpdatedAt)
                VALUES (@EntityName, @Watermark, GETUTCDATE(), GETUTCDATE());",
            new { EntityName = entityName, Watermark = watermark });
    }

    // Internal record types for database sync
    private class ProjectSyncRecord
    {
        public int ID { get; set; }
        public string? Name { get; set; }
        public DateTime? LastUpdated { get; set; }
    }

    private class CompanySyncRecord
    {
        public int ID { get; set; }
        public string? Name { get; set; }
        public DateTime? LastUpdated { get; set; }
    }

    private class ContactSyncRecord
    {
        public int ID { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public DateTime? LastUpdated { get; set; }
    }

    private class EmployeeSyncRecord
    {
        public int ID { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public DateTime? LastUpdated { get; set; }
    }
}
