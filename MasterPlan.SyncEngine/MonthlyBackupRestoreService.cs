using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using Dapper;

namespace MasterPlan.SyncEngine;

/// <summary>
/// Result of the monthly backup/restore ETL operation
/// </summary>
public class MonthlyBackupResult
{
    public bool Success { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool Step1Completed { get; set; } // Restore
    public bool Step2Completed { get; set; } // Initialize Replica
    public bool Step3Completed { get; set; } // ETL
    public string? ErrorMessage { get; set; }
    public Dictionary<string, int> EntityRecordCounts { get; set; } = new();
}

/// <summary>
/// Service for monthly backup restore and ETL pipeline using SMO (SQL Server Management Objects).
/// 
/// Phase 1 Flow:
/// 1. [RESTORE] Restore .bak file to Db_Mp_SiEng using SMO
/// 2. [INIT]    Create Replica_DB and sync tables if needed
/// 3. [ETL]     Extract, Transform, Load data from source to replica
/// </summary>
public class MonthlyBackupRestoreService
{
    private readonly string _sourceConnectionString;
    private readonly string _replicaConnectionString;
    private readonly string _masterConnectionString;
    private readonly ILogger<MonthlyBackupRestoreService> _logger;
    private readonly int _hoursLookbackDays;
    private readonly MonthlyHoursCompareRunner _hoursCompare;

    // Available tables in source database (populated during validation)
    // Individual ETL methods can check this to handle missing tables gracefully
    private HashSet<string> _availableSourceTables = new(StringComparer.OrdinalIgnoreCase);

    // Backup finish date extracted from backup header (HEADERONLY, before restore).
    // Used as LastUpdated baseline for ALL ETL rows: "Data valid up to this backup time"
    private DateTime? _backupFinishDate;

    // Source database name (restored from backup)
    private const string SourceDatabaseName = "Db_Mp_SiEng";
    // Replica database name (ETL target)
    private const string ReplicaDatabaseName = "Replica_DB";

    public MonthlyBackupRestoreService(
        string sourceConnectionString,
        string replicaConnectionString,
        string masterConnectionString,
        ILogger<MonthlyBackupRestoreService> logger,
        int hoursLookbackDays = 14)
    {
        _sourceConnectionString = sourceConnectionString;
        _replicaConnectionString = replicaConnectionString;
        _masterConnectionString = masterConnectionString;
        _logger = logger;
        _hoursLookbackDays = hoursLookbackDays > 0 ? hoursLookbackDays : 14;
        _hoursCompare = new MonthlyHoursCompareRunner(logger);
    }

    /// <summary>
    /// Run the complete monthly backup/restore ETL pipeline
    /// 
    /// FULL MODE (Re-enabled):
    /// - Step 1: Restore .bak file to Db_Mp_SiEng using SMO
    /// - Step 2: Create/recreate Replica_DB schema
    /// - Step 3: Full ETL pipeline from Source DB to Replica_DB
    /// </summary>
    public async Task<MonthlyBackupResult> RunMonthlyBackupRestoreAsync(string backupFilePath)
    {
        var result = new MonthlyBackupResult
        {
            StartTime = DateTime.UtcNow
        };

        try
        {
            // ═══════════════════════════════════════════════════════════════════════════════
            // STEP 0: DATE GATE (HEADERONLY — no database writes)
            // ═══════════════════════════════════════════════════════════════════════════════
            Console.WriteLine();
            Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  STEP 0 – BACKUP DATE GATE                                       ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
            Console.WriteLine($"    Backup file: {backupFilePath}");

            _backupFinishDate = await RequireBackupFinishDateAsync(backupFilePath);
            var lastMonthly = await TryGetLastMonthlyRestoreStampAsync();
            Console.WriteLine($"    BackupFinishDate: {_backupFinishDate.Value:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"    Last MonthlyRestore stamp: {(lastMonthly.HasValue ? lastMonthly.Value.ToString("yyyy-MM-dd HH:mm:ss") : "(none — first run)")}");

            if (!MonthlyRestoreGate.IsNewerThanLastRestore(_backupFinishDate.Value, lastMonthly))
            {
                var message =
                    $"הגיבוי אינו חדש יותר מהשחזור החודשי האחרון. BackupFinishDate={_backupFinishDate.Value:yyyy-MM-dd HH:mm:ss}, " +
                    $"שחזור אחרון={lastMonthly:yyyy-MM-dd HH:mm:ss}. לא בוצע שינוי בבסיסי הנתונים.";
                _logger.LogWarning("Monthly restore gate refused bak: {Message}", message);
                throw new InvalidOperationException(message);
            }

            _logger.LogWarning(
                "שחזור חודשי שער תאריך עבר: BackupFinishDate={BackupFinishDate:yyyy-MM-dd HH:mm:ss}, LastMonthlyRestore={LastMonthly}.",
                _backupFinishDate.Value,
                lastMonthly);

            // ═══════════════════════════════════════════════════════════════════════════════
            // STEP 1: RESTORE BACKUP
            // ═══════════════════════════════════════════════════════════════════════════════
            _logger.LogInformation("Step 1: Restoring backup from {BackupPath}", backupFilePath);
            Console.WriteLine();
            Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  STEP 1 – RESTORE                                                ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
            Console.WriteLine($"    Backup file: {backupFilePath}");
            Console.WriteLine($"    Target DB:   {SourceDatabaseName}");

            await RestoreBackupAsync(backupFilePath);
            result.Step1Completed = true;
            Console.WriteLine("    [STEP 1] ✓ Backup restore completed");
            Console.WriteLine($"    Backup finish date: {_backupFinishDate.Value:yyyy-MM-dd HH:mm:ss}");

            // ═══════════════════════════════════════════════════════════════════════════════
            // STEP 1b: COMPARE replica (still old) vs restored HoursReports — fail closed on throw
            // ═══════════════════════════════════════════════════════════════════════════════
            await CompareHoursAsync(MonthlyHoursComparePhase.PreDrop);

            // ═══════════════════════════════════════════════════════════════════════════════
            // STEP 2: INITIALIZE REPLICA
            // ═══════════════════════════════════════════════════════════════════════════════
            _logger.LogInformation("Step 2: Initializing Replica_DB schema");
            Console.WriteLine();
            Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  STEP 2 – INITIALIZE REPLICA                                     ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
            Console.WriteLine($"    Target DB: {ReplicaDatabaseName}");
            Console.WriteLine("    Creating tables and indexes...");

            await InitializeReplicaDatabaseAsync();
            result.Step2Completed = true;
            Console.WriteLine("    [STEP 2] ✓ Replica_DB schema initialized");

            // ═══════════════════════════════════════════════════════════════════════════════
            // STEP 3: FULL ETL LOAD
            // ═══════════════════════════════════════════════════════════════════════════════
            _logger.LogInformation("Step 3: Running full ETL pipeline");
            Console.WriteLine();
            Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  STEP 3 – FULL ETL LOAD                                          ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
            Console.WriteLine($"    Source: {SourceDatabaseName}");
            Console.WriteLine($"    Target: {ReplicaDatabaseName}");
            Console.WriteLine("    Clearing existing data...");
            Console.WriteLine("    Running ETL for all entities...");

            result.EntityRecordCounts = await RunEtlPipelineAsync();
            result.Step3Completed = true;

            var totalRecords = result.EntityRecordCounts.Values.Sum();
            Console.WriteLine($"    [STEP 3] ✓ ETL completed - {totalRecords} total records loaded");

            // ═══════════════════════════════════════════════════════════════════════════════
            // STEP 3b: COMPARE after ETL + stamp MonthlyRestore
            // ═══════════════════════════════════════════════════════════════════════════════
            await CompareHoursAsync(MonthlyHoursComparePhase.PostEtl);
            await StampMonthlyRestoreAsync(_backupFinishDate.Value);

            result.Success = true;
            _logger.LogInformation("Monthly backup/restore completed successfully with {TotalRecords} records", totalRecords);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Monthly backup/restore failed");
            result.ErrorMessage = ex.Message;
            result.Success = false;
        }

        result.EndTime = DateTime.UtcNow;
        return result;
    }

    #region Step 1: Restore Backup using SMO

    private async Task RestoreBackupAsync(string backupFilePath)
    {
        await Task.Run(() =>
        {
            // Extract server name from connection string
            var builder = new SqlConnectionStringBuilder(_masterConnectionString);
            var serverName = builder.DataSource;

            _logger.LogInformation("Connecting to SQL Server: {ServerName}", serverName);

            // Create server connection using Windows Authentication or SQL Auth
            ServerConnection serverConnection;
            if (builder.IntegratedSecurity)
            {
                serverConnection = new ServerConnection(serverName);
            }
            else
            {
                serverConnection = new ServerConnection(serverName, builder.UserID, builder.Password);
            }

            var server = new Server(serverConnection);

            try
            {
                // Kill existing connections and set database to SINGLE_USER mode
                _logger.LogInformation("Setting {Database} to SINGLE_USER mode to ensure exclusive access", SourceDatabaseName);

                if (server.Databases.Contains(SourceDatabaseName))
                {
                    try
                    {
                        // First try to kill all processes
                        server.KillAllProcesses(SourceDatabaseName);

                        // Then set to SINGLE_USER with ROLLBACK IMMEDIATE to force disconnect any remaining connections
                        server.ConnectionContext.ExecuteNonQuery($@"
                            ALTER DATABASE [{SourceDatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                        ");
                        _logger.LogInformation("Database set to SINGLE_USER mode");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not set database to SINGLE_USER mode - it may not exist yet or is already offline");
                    }
                }

                // Configure the restore operation
                var restore = new Restore
                {
                    Database = SourceDatabaseName,
                    Action = RestoreActionType.Database,
                    ReplaceDatabase = true,
                    NoRecovery = false
                };

                // Add the backup device
                restore.Devices.AddDevice(backupFilePath, DeviceType.File);


                // Get the file list from backup to handle file relocation
                System.Data.DataTable fileList;
                try
                {
                    fileList = restore.ReadFileList(server);
                    _logger.LogInformation("Backup contains {FileCount} files", fileList.Rows.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to read file list from backup");
                    throw new InvalidOperationException($"Cannot read backup file list: {ex.Message}", ex);
                }

                // Get default paths using direct SQL query (most reliable method)
                // IMPORTANT: Connect to 'master' database, not the target database, to avoid locking issues
                string defaultDataPath;
                string defaultLogPath;

                var masterDbConnString = new SqlConnectionStringBuilder(_masterConnectionString)
                {
                    InitialCatalog = "master"
                }.ConnectionString;

                using (var pathConn = new SqlConnection(masterDbConnString))
                {
                    pathConn.Open();

                    defaultDataPath = pathConn.ExecuteScalar<string>(
                        "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS NVARCHAR(500))") ?? string.Empty;

                    defaultLogPath = pathConn.ExecuteScalar<string>(
                        "SELECT CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS NVARCHAR(500))") ?? string.Empty;

                    // Fallback: query from master database file location
                    if (string.IsNullOrEmpty(defaultDataPath))
                    {
                        defaultDataPath = pathConn.ExecuteScalar<string>(@"
                            SELECT LEFT(physical_name, LEN(physical_name) - CHARINDEX('\', REVERSE(physical_name)) + 1)
                            FROM sys.master_files 
                            WHERE database_id = 1 AND type = 0") ?? @"C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\DATA\";
                    }

                    if (string.IsNullOrEmpty(defaultLogPath))
                    {
                        defaultLogPath = pathConn.ExecuteScalar<string>(@"
                            SELECT LEFT(physical_name, LEN(physical_name) - CHARINDEX('\', REVERSE(physical_name)) + 1)
                            FROM sys.master_files 
                            WHERE database_id = 1 AND type = 1") ?? defaultDataPath;
                    }
                }

                // Ensure paths end with backslash
                if (!defaultDataPath.EndsWith("\\")) defaultDataPath += "\\";
                if (!defaultLogPath.EndsWith("\\")) defaultLogPath += "\\";

                _logger.LogInformation("Default data path: {DataPath}", defaultDataPath);
                _logger.LogInformation("Default log path: {LogPath}", defaultLogPath);

                // Relocate files to default paths
                foreach (System.Data.DataRow row in fileList.Rows)
                {
                    var logicalName = row["LogicalName"]?.ToString();
                    var type = row["Type"]?.ToString();
                    var originalPhysicalName = row["PhysicalName"]?.ToString();

                    if (string.IsNullOrEmpty(logicalName))
                    {
                        _logger.LogWarning("Skipping file with missing logical name");
                        continue;
                    }

                    // Use original filename or generate one based on logical name
                    string fileName;
                    if (!string.IsNullOrEmpty(originalPhysicalName))
                    {
                        fileName = Path.GetFileName(originalPhysicalName);
                    }
                    else
                    {
                        fileName = type == "L" 
                            ? $"{SourceDatabaseName}_{logicalName}.ldf"
                            : $"{SourceDatabaseName}_{logicalName}.mdf";
                    }

                    string newPath = type == "L" 
                        ? defaultLogPath + fileName 
                        : defaultDataPath + fileName;

                    _logger.LogInformation("Relocating '{LogicalName}' ({Type}) to: {NewPath}", logicalName, type, newPath);
                    restore.RelocateFiles.Add(new RelocateFile(logicalName, newPath));
                }

                // Set up progress reporting
                restore.PercentComplete += (sender, e) =>
                {
                    if (e.Percent % 10 == 0)
                    {
                        Console.WriteLine($"[RESTORE] Progress: {e.Percent}%");
                    }
                };

                restore.Complete += (sender, e) =>
                {
                    _logger.LogInformation("Restore operation completed");
                };

                // Try using Script + Execute instead of SqlRestore (more reliable)
                _logger.LogInformation("Starting restore operation...");
                try
                {
                    // Generate and execute the restore script
                    var script = restore.Script(server);
                    _logger.LogDebug("Generated restore script with {Count} statements", script.Count);

                    foreach (var stmt in script)
                    {
                        _logger.LogDebug("Executing: {Statement}", stmt.Length > 200 ? stmt[..200] + "..." : stmt);
                    }

                    // Execute the script
                    server.ConnectionContext.ExecuteNonQuery(script);
                    _logger.LogInformation("Restore completed successfully via script execution");
                }
                catch (Exception scriptEx)
                {
                    _logger.LogWarning(scriptEx, "Script execution failed, trying direct SqlRestore...");

                    // Fallback to SqlRestore
                    restore.SqlRestore(server);
                    _logger.LogInformation("Restore completed successfully via SqlRestore");
                }

                // Set database back to MULTI_USER mode
                try
                {
                    server.ConnectionContext.ExecuteNonQuery($@"
                        ALTER DATABASE [{SourceDatabaseName}] SET MULTI_USER;
                    ");
                    _logger.LogInformation("Database set back to MULTI_USER mode");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not set database to MULTI_USER mode");
                }
            }
            finally
            {
                serverConnection.Disconnect();
            }
        });
    }

    /// <summary>
    /// Extract BackupFinishDate from the backup file header.
    /// Uses RESTORE HEADERONLY which reads the file header without affecting any database.
    /// Returns the timestamp when the backup completed — used as "data valid up to" marker.
    /// </summary>
    private async Task<DateTime?> TryGetBackupFinishDateAsync(string backupFilePath)
    {
        try
        {
            var masterDbConnString = new SqlConnectionStringBuilder(_masterConnectionString)
            {
                InitialCatalog = "master"
            }.ConnectionString;

            await using var connection = new SqlConnection(masterDbConnString);
            await connection.OpenAsync();

            // RESTORE HEADERONLY doesn't support parameterized paths
            var escapedPath = backupFilePath.Replace("'", "''");
            var sql = $"RESTORE HEADERONLY FROM DISK = N'{escapedPath}'";

            var rows = await connection.QueryAsync(sql);
            var row = rows.FirstOrDefault();

            if (row != null)
            {
                var finishDate = (DateTime)row.BackupFinishDate;
                _logger.LogInformation("Backup header: BackupFinishDate = {BackupFinishDate:O}", finishDate);
                return finishDate;
            }

            _logger.LogWarning("RESTORE HEADERONLY returned no rows");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract BackupFinishDate from backup header");
            return null;
        }
    }

    private async Task<DateTime> RequireBackupFinishDateAsync(string backupFilePath)
    {
        var finishDate = await TryGetBackupFinishDateAsync(backupFilePath);
        if (!finishDate.HasValue)
        {
            throw new InvalidOperationException(
                "לא ניתן לקרוא BackupFinishDate מ־RESTORE HEADERONLY. השחזור נעצר לפני שינוי בבסיסי הנתונים.");
        }

        return finishDate.Value;
    }

    private async Task<DateTime?> TryGetLastMonthlyRestoreStampAsync()
    {
        try
        {
            await using var connection = new SqlConnection(_replicaConnectionString);
            await connection.OpenAsync();

            var tableId = await connection.ExecuteScalarAsync<int?>(
                "SELECT OBJECT_ID(N'dbo.Sync_State', N'U')");
            if (tableId is null)
            {
                return null;
            }

            return await connection.ExecuteScalarAsync<DateTime?>(
                "SELECT LastWatermark FROM Sync_State WHERE EntityName = @EntityName",
                new { EntityName = MonthlyRestoreGate.SyncStateEntityName });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read Sync_State.MonthlyRestore — treating as first run");
            return null;
        }
    }

    private async Task CompareHoursAsync(MonthlyHoursComparePhase phase)
    {
        Console.WriteLine();
        Console.WriteLine(phase == MonthlyHoursComparePhase.PreDrop
            ? "╔══════════════════════════════════════════════════════════════════╗\n║  STEP 1b – COMPARE (pre-DROP)                                    ║\n╚══════════════════════════════════════════════════════════════════╝"
            : "╔══════════════════════════════════════════════════════════════════╗\n║  STEP 3b – COMPARE (post-ETL)                                    ║\n╚══════════════════════════════════════════════════════════════════╝");

        await using var source = new SqlConnection(_sourceConnectionString);
        await using var replica = new SqlConnection(_replicaConnectionString);
        await source.OpenAsync();
        await replica.OpenAsync();

        var dailyFromDate = await ResolveDailyFromDateAsync(replica, phase);

        // Fail closed before DROP: let exceptions bubble to RunMonthlyBackupRestoreAsync catch.
        await _hoursCompare.RunAsync(
            source,
            replica,
            phase,
            dailyFromDate,
            _backupFinishDate);
    }

    private async Task<DateTime> ResolveDailyFromDateAsync(SqlConnection replica, MonthlyHoursComparePhase phase)
    {
        // Prefer the live ProjectHoursExtended watermark (what daily sync actually used) before DROP.
        if (phase == MonthlyHoursComparePhase.PreDrop)
        {
            try
            {
                var tableId = await replica.ExecuteScalarAsync<int?>(
                    "SELECT OBJECT_ID(N'dbo.Sync_State', N'U')");
                if (tableId is not null)
                {
                    var watermark = await replica.ExecuteScalarAsync<DateTime?>(
                        "SELECT LastWatermark FROM Sync_State WHERE EntityName = @EntityName",
                        new { EntityName = "ProjectHoursExtended" });
                    if (watermark.HasValue)
                    {
                        return watermark.Value.Date.AddDays(-_hoursLookbackDays);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read ProjectHoursExtended watermark for FromDate — falling back to BackupFinishDate");
            }
        }

        return _backupFinishDate!.Value.Date.AddDays(-_hoursLookbackDays);
    }

    private async Task StampMonthlyRestoreAsync(DateTime backupFinishDate)
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.OpenAsync();

        // Sync_State may have been recreated empty during schema init / ClearReplicaData.
        await connection.ExecuteAsync(@"
            IF OBJECT_ID(N'dbo.Sync_State', N'U') IS NULL
            BEGIN
                CREATE TABLE Sync_State (
                    EntityName NVARCHAR(100) PRIMARY KEY,
                    LastWatermark DATETIME2,
                    LastSyncTime DATETIME2,
                    UpdatedAt DATETIME2 DEFAULT GETUTCDATE()
                )
            END

            MERGE Sync_State AS target
            USING (SELECT @EntityName AS EntityName) AS source
            ON target.EntityName = source.EntityName
            WHEN MATCHED THEN
                UPDATE SET LastWatermark = @Watermark, LastSyncTime = GETUTCDATE(), UpdatedAt = GETUTCDATE()
            WHEN NOT MATCHED THEN
                INSERT (EntityName, LastWatermark, LastSyncTime, UpdatedAt)
                VALUES (@EntityName, @Watermark, GETUTCDATE(), GETUTCDATE());",
            new
            {
                EntityName = MonthlyRestoreGate.SyncStateEntityName,
                Watermark = backupFinishDate
            });

        _logger.LogWarning(
            "שחזור חודשי: נשמרה חותמת Sync_State.MonthlyRestore={BackupFinishDate:yyyy-MM-dd HH:mm:ss}.",
            backupFinishDate);
        Console.WriteLine($"    [STEP 3b] MonthlyRestore stamp = {backupFinishDate:yyyy-MM-dd HH:mm:ss}");
    }

    #endregion

    #region Step 2: Initialize Replica Database


    private async Task InitializeReplicaDatabaseAsync()
    {
        // First ensure the database exists using master connection
        await EnsureReplicaDatabaseExistsAsync();

        // Then create the schema
        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.OpenAsync();

        // Create all entity tables with proper schema
        await CreateReplicaSchemaAsync(connection);

        // Create sync state and run history tables
        await CreateSyncTablesAsync(connection);

        _logger.LogInformation("Replica database schema created successfully");
    }

    private async Task EnsureReplicaDatabaseExistsAsync()
    {
        // IMPORTANT: CREATE DATABASE must be executed while connected to 'master' database
        var masterDbConnString = new SqlConnectionStringBuilder(_masterConnectionString)
        {
            InitialCatalog = "master"
        }.ConnectionString;

        await using var connection = new SqlConnection(masterDbConnString);
        await connection.OpenAsync();

        var dbExists = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys.databases WHERE name = @DbName",
            new { DbName = ReplicaDatabaseName });

        if (dbExists == 0)
        {
            _logger.LogInformation("Creating database {Database}", ReplicaDatabaseName);
            await connection.ExecuteAsync($"CREATE DATABASE [{ReplicaDatabaseName}]");
        }
    }

    private async Task CreateReplicaSchemaAsync(SqlConnection connection)
    {
        // ═══════════════════════════════════════════════════════════════════════════════
        // IMPORTANT: Schema matches actual API responses from 20260213_010939/*.ndjson
        // These tables must be compatible with both:
        //   1. ETL from source database (this service)
        //   2. API sync (ApiDailySyncService)
        // NOTE: This schema is EMBEDDED in code, not loaded from CreateReplicaTables.sql
        // ═══════════════════════════════════════════════════════════════════════════════

        _logger.LogInformation("Creating replica schema from EMBEDDED definitions (not from .sql file)");
        Console.WriteLine("        [INFO] Schema source: MonthlyBackupRestoreService.CreateReplicaSchemaAsync()");

        // Drop existing tables for full rebuild (ensures schema is current)
        await connection.ExecuteAsync(@"
            IF OBJECT_ID('MP_ProjectHoursExtended', 'U') IS NOT NULL DROP TABLE MP_ProjectHoursExtended;
            IF OBJECT_ID('MP_TimeHourReports', 'U') IS NOT NULL DROP TABLE MP_TimeHourReports;
            IF OBJECT_ID('MP_ProjectHours', 'U') IS NOT NULL DROP TABLE MP_ProjectHours;
            IF OBJECT_ID('MP_Conversations', 'U') IS NOT NULL DROP TABLE MP_Conversations;
            IF OBJECT_ID('MP_Tasks', 'U') IS NOT NULL DROP TABLE MP_Tasks;
            IF OBJECT_ID('MP_Intakes', 'U') IS NOT NULL DROP TABLE MP_Intakes;
            IF OBJECT_ID('MP_Bills', 'U') IS NOT NULL DROP TABLE MP_Bills;
            IF OBJECT_ID('MP_Bids', 'U') IS NOT NULL DROP TABLE MP_Bids;
            IF OBJECT_ID('MP_Contacts', 'U') IS NOT NULL DROP TABLE MP_Contacts;
            IF OBJECT_ID('MP_Employees', 'U') IS NOT NULL DROP TABLE MP_Employees;
            IF OBJECT_ID('MP_Companies', 'U') IS NOT NULL DROP TABLE MP_Companies;
            IF OBJECT_ID('MP_Projects', 'U') IS NOT NULL DROP TABLE MP_Projects;
        ");
        _logger.LogInformation("Dropped existing MP_* tables for full rebuild");

        // Projects table - matches API schema
        await connection.ExecuteAsync(@"
            CREATE TABLE MP_Projects (
                ID INT PRIMARY KEY,
                Name NVARCHAR(500),
                ProjectNum NVARCHAR(100),
                StartDate DATETIME2,
                EndDate DATETIME2,
                Description NVARCHAR(MAX),
                CustomerName NVARCHAR(500),
                CustomerID INT,
                EmployeeID INT,
                EmployeeName NVARCHAR(500),
                StatusID INT,
                StatusName NVARCHAR(200),
                ProjectTypeID INT,
                ProjectType NVARCHAR(200),
                StudioDepartmentTypeID INT,
                StudioDepartmentType NVARCHAR(200),
                IsActive BIT,
                FeeSum DECIMAL(18,2),
                LastUpdated DATETIME2,
                INDEX IX_MP_Projects_LastUpdated (LastUpdated),
                INDEX IX_MP_Projects_CustomerID (CustomerID),
                INDEX IX_MP_Projects_EmployeeID (EmployeeID)
            )");

        // Companies table - API schema: city (lowercase), RegistrationNumber, PhoneNum
        await connection.ExecuteAsync(@"
            CREATE TABLE MP_Companies (
                ID INT PRIMARY KEY,
                Name NVARCHAR(500),
                Address NVARCHAR(500),
                City NVARCHAR(200),
                Email NVARCHAR(500),
                RegistrationNumber NVARCHAR(100),
                PhoneNum NVARCHAR(100),
                LastUpdated DATETIME2,
                INDEX IX_MP_Companies_LastUpdated (LastUpdated)
            )");

        // Contacts table - API schema includes Address
        await connection.ExecuteAsync(@"
            CREATE TABLE MP_Contacts (
                ID INT PRIMARY KEY,
                FirstName NVARCHAR(200),
                LastName NVARCHAR(200),
                CompanyName NVARCHAR(500),
                CompanyID INT,
                Address NVARCHAR(500),
                Email NVARCHAR(500),
                Phone NVARCHAR(100),
                Mobile NVARCHAR(100),
                LastUpdated DATETIME2,
                INDEX IX_MP_Contacts_LastUpdated (LastUpdated),
                INDEX IX_MP_Contacts_CompanyID (CompanyID)
            )");

        // Employees table - API returns minimal fields
        await connection.ExecuteAsync(@"
            CREATE TABLE MP_Employees (
                ID INT PRIMARY KEY,
                FirstName NVARCHAR(200),
                LastName NVARCHAR(200),
                LastUpdated DATETIME2,
                INDEX IX_MP_Employees_LastUpdated (LastUpdated)
            )");

        // Bids table - API schema: ProposalNum, DateTime, EstimatedSum, ProposalStatus
        await connection.ExecuteAsync(@"
            CREATE TABLE MP_Bids (
                ID INT PRIMARY KEY,
                ProposalNum NVARCHAR(100),
                Name NVARCHAR(500),
                ActiveProposal BIT,
                [DateTime] DATETIME2,
                EstimatedSum DECIMAL(18,2),
                ProbabilityID INT,
                ProbabilityName NVARCHAR(200),
                StatusID INT,
                ProposalStatus NVARCHAR(200),
                LastUpdated DATETIME2,
                INDEX IX_MP_Bids_LastUpdated (LastUpdated)
            )");

        // Bills table - API schema: Sum, Status, BillInternalNum, ResponsibleEmployee
        await connection.ExecuteAsync(@"
            CREATE TABLE MP_Bills (
                ID INT PRIMARY KEY,
                BillNum NVARCHAR(100),
                ProjectName NVARCHAR(500),
                ProjectID INT,
                BillInternalNum NVARCHAR(100),
                [Sum] DECIMAL(18,2),
                SubmitDate DATETIME2,
                CollectionDate DATETIME2,
                Status NVARCHAR(200),
                StatusID INT,
                ResponsibleEmployee NVARCHAR(500),
                ResponsibleEmployeeID INT,
                StudioDepartment NVARCHAR(200),
                StudioDepartmentTypeID INT,
                LastUpdated DATETIME2,
                INDEX IX_MP_Bills_LastUpdated (LastUpdated),
                INDEX IX_MP_Bills_ProjectID (ProjectID)
            )");

        // Intakes table - API schema: Sum, PaymentType, Description
        await connection.ExecuteAsync(@"
            CREATE TABLE MP_Intakes (
                ID INT PRIMARY KEY,
                OpenDate DATETIME2,
                [Sum] DECIMAL(18,2),
                CustomerID INT,
                CustomerName NVARCHAR(500),
                PaymentType NVARCHAR(200),
                PayTypeID INT,
                Description NVARCHAR(MAX),
                LastUpdated DATETIME2,
                INDEX IX_MP_Intakes_LastUpdated (LastUpdated)
            )");

        // Tasks table - API schema: TaskDescription, IsHandled, IsClosed, Sender/Receiver
        await connection.ExecuteAsync(@"
            CREATE TABLE MP_Tasks (
                ID INT PRIMARY KEY,
                TaskDescription NVARCHAR(MAX),
                IsHandled BIT,
                IsClosed BIT,
                StartDate DATETIME2,
                DueDate DATETIME2,
                SenderName NVARCHAR(500),
                SenderID INT,
                ReceiverName NVARCHAR(500),
                ReceiverID INT,
                CompletionDate DATETIME2,
                Priority NVARCHAR(100),
                PriorityID INT,
                LastUpdated DATETIME2,
                INDEX IX_MP_Tasks_LastUpdated (LastUpdated),
                INDEX IX_MP_Tasks_DueDate (DueDate)
            )");

        // Conversations table
        await connection.ExecuteAsync(@"
            CREATE TABLE MP_Conversations (
                ID INT PRIMARY KEY,
                ProjectID INT,
                ProjectName NVARCHAR(500),
                ContactID INT,
                ContactName NVARCHAR(500),
                EmployeeID INT,
                EmployeeName NVARCHAR(500),
                CreatedDate DATETIME2,
                DueDate DATETIME2,
                Subject NVARCHAR(500),
                Notes NVARCHAR(MAX),
                INDEX IX_MP_Conversations_CreatedDate (CreatedDate),
                INDEX IX_MP_Conversations_ProjectID (ProjectID)
            )");

        // ProjectHours table - TIME(0) for StartTime/EndTime/TotalHours, serialize as "HH:mm"
        await connection.ExecuteAsync(@"
            CREATE TABLE MP_ProjectHours (
                ID INT PRIMARY KEY,
                ProjectID INT,
                ProjectName NVARCHAR(500),
                ProjectNumber NVARCHAR(100),
                EmployeeID INT,
                EmployeeName NVARCHAR(500),
                ReportDate DATE,
                StepName NVARCHAR(200),
                Description NVARCHAR(MAX),
                StartTime TIME(0),
                EndTime TIME(0),
                TotalHours TIME(0),
                INDEX IX_MP_ProjectHours_ReportDate (ReportDate),
                INDEX IX_MP_ProjectHours_ProjectID (ProjectID),
                INDEX IX_MP_ProjectHours_EmployeeID (EmployeeID)
            )");

        // TimeHourReports table - GET /api/projecthours/GetTimeHourReports
        // NOTE: API field "DateTime" mapped to "ReportDateTime" to avoid reserved word conflict
        await connection.ExecuteAsync(@"
            CREATE TABLE MP_TimeHourReports (
                ID INT PRIMARY KEY,
                EmployeeID INT,
                EmployeeName NVARCHAR(200),
                ReportDateTime DATETIME2,
                StartTime TIME(0),
                EndTime TIME(0),
                Duration DECIMAL(10,4),
                SyncedAt DATETIME2 DEFAULT GETUTCDATE(),
                INDEX IX_MP_TimeHourReports_ReportDateTime (ReportDateTime),
                INDEX IX_MP_TimeHourReports_EmployeeID (EmployeeID)
            )");

        // ProjectHoursExtended table - GET /api/projecthours/GetProjectHoursExtended
        // Extended hours with SubContract details, dual duration formats, LastUpdated for watermark
        await connection.ExecuteAsync(@"
            CREATE TABLE MP_ProjectHoursExtended (
                ID INT PRIMARY KEY,
                EmployeeID INT,
                EmployeeName NVARCHAR(200),
                ProjectID INT,
                ProjectName NVARCHAR(500),
                ProjectNumber NVARCHAR(50),
                SubContractID INT,
                SubContractName NVARCHAR(500),
                SubContractStepID INT,
                SubContractStepName NVARCHAR(200),
                ReportDate DATETIME2,
                StepName NVARCHAR(200),
                HoursReportsStepID INT,
                Description NVARCHAR(MAX),
                StartTime TIME(0),
                EndTime TIME(0),
                TotalHours TIME(0),
                Duration DECIMAL(10,4),
                LastUpdated DATETIME2,
                SyncedAt DATETIME2 DEFAULT GETUTCDATE(),
                INDEX IX_MP_ProjectHoursExtended_ReportDate (ReportDate),
                INDEX IX_MP_ProjectHoursExtended_LastUpdated (LastUpdated),
                INDEX IX_MP_ProjectHoursExtended_ProjectID (ProjectID),
                INDEX IX_MP_ProjectHoursExtended_EmployeeID (EmployeeID)
            )");

        _logger.LogInformation("Created all MP_* tables with API-compatible schema");
    }

    private async Task CreateSyncTablesAsync(SqlConnection connection)
    {
        await connection.ExecuteAsync(@"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Sync_State')
            CREATE TABLE Sync_State (
                EntityName NVARCHAR(100) PRIMARY KEY,
                LastWatermark DATETIME2,
                LastSyncTime DATETIME2,
                UpdatedAt DATETIME2 DEFAULT GETUTCDATE()
            )

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Sync_RunHistory')
            CREATE TABLE Sync_RunHistory (
                ID INT IDENTITY(1,1) PRIMARY KEY,
                StartTime DATETIME2 NOT NULL,
                EndTime DATETIME2 NOT NULL,
                Success BIT NOT NULL,
                ErrorMessage NVARCHAR(MAX),
                RecordsSynced INT,
                Details NVARCHAR(MAX)
            )

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Sync_Lock')
            BEGIN
                CREATE TABLE Sync_Lock (
                    LockName NVARCHAR(100) PRIMARY KEY,
                    AcquiredAt DATETIME2,
                    AcquiredBy NVARCHAR(200)
                )
                INSERT INTO Sync_Lock (LockName) VALUES ('DailySync')
            END
        ");
    }

    #endregion

    #region Step 3: ETL Pipeline

    private async Task<Dictionary<string, int>> RunEtlPipelineAsync()
    {
        var counts = new Dictionary<string, int>();

        await using var sourceConn = new SqlConnection(_sourceConnectionString);
        await using var replicaConn = new SqlConnection(_replicaConnectionString);
        await sourceConn.OpenAsync();
        await replicaConn.OpenAsync();

        // GUARD: Validate all required source tables exist before running ETL
        Console.WriteLine("    → Validating source database schema...");
        await ValidateSourceTablesAsync(sourceConn);

        // Clear existing data in replica (full rebuild)
        _logger.LogInformation("Clearing existing data in replica...");
        await ClearReplicaDataAsync(replicaConn);

        // ETL for each entity
        // Note: The actual ETL queries depend on the source database schema from script.sql
        // These are placeholder implementations - adjust based on actual source schema

        counts["Projects"] = await EtlProjectsAsync(sourceConn, replicaConn);
        counts["Companies"] = await EtlCompaniesAsync(sourceConn, replicaConn);
        counts["Contacts"] = await EtlContactsAsync(sourceConn, replicaConn);
        counts["Employees"] = await EtlEmployeesAsync(sourceConn, replicaConn);
        counts["Bids"] = await EtlBidsAsync(sourceConn, replicaConn);
        counts["Bills"] = await EtlBillsAsync(sourceConn, replicaConn);
        counts["Intakes"] = await EtlIntakesAsync(sourceConn, replicaConn);
        counts["Tasks"] = await EtlTasksAsync(sourceConn, replicaConn);
        counts["Conversations"] = await EtlConversationsAsync(sourceConn, replicaConn);
        counts["ProjectHours"] = await EtlProjectHoursAsync(sourceConn, replicaConn);
        counts["TimeHourReports"] = await EtlTimeHourReportsAsync(sourceConn, replicaConn);
        counts["ProjectHoursExtended"] = await EtlProjectHoursExtendedAsync(sourceConn, replicaConn);

        // Set LastUpdated = BackupFinishDate for dimension entities that have LastUpdated.
        // DEV-021: do NOT stamp MP_ProjectHoursExtended — HoursReports has no business LastUpdated;
        // bak finish lives in Sync_State watermarks / MonthlyRestore. PHE.LastUpdated stays NULL
        // until the first successful API upsert (MERGE may also repair null Duration/TotalHours).
        if (_backupFinishDate.HasValue)
        {
            Console.WriteLine($"    → Setting LastUpdated = {_backupFinishDate.Value:yyyy-MM-dd HH:mm:ss} for dimension entities...");
            var tablesWithLastUpdated = new[]
            {
                "MP_Projects", "MP_Companies", "MP_Contacts", "MP_Employees",
                "MP_Bids", "MP_Bills", "MP_Intakes", "MP_Tasks"
            };
            foreach (var table in tablesWithLastUpdated)
            {
                var affected = await replicaConn.ExecuteAsync(
                    $"UPDATE [{table}] SET LastUpdated = @BackupFinishDate",
                    new { BackupFinishDate = _backupFinishDate.Value });
                _logger.LogDebug("Set LastUpdated for {Table}: {Count} rows", table, affected);
            }
            Console.WriteLine($"        LastUpdated baseline set for {tablesWithLastUpdated.Length} entity tables (PHE excluded)");
        }

        // Initialize watermarks based on loaded data
        await InitializeWatermarksAsync(replicaConn);

        return counts;
    }

    /// <summary>
    /// Validates source tables and populates _availableSourceTables.
    /// Logs warnings for missing tables but does NOT abort.
    /// Individual ETL methods handle missing dependencies gracefully.
    /// </summary>
    private async Task ValidateSourceTablesAsync(SqlConnection sourceConn)
    {
        // Core tables used by ETL queries (informational only - variations handled by ETL methods)
        // Note: Link/lookup table names may vary - ETL methods handle this dynamically
        //       e.g., TasksProjects vs ProjectsTasks, Priorities vs TaskPriorities, Steps vs HoursReportsSteps
        var expectedTables = new[]
        {
            // Projects ETL
            "Projects", "Companies", "Employees", "ProjectStatuses", "ProjectTopicsTypes", "StudioDepartmentTypes",
            // Companies ETL
            "CompanyAddresses", "Cities", "CompanyPhones",
            // Contacts ETL
            "Contacts", "ContactPhones",
            // Bids ETL - uses Proposals (lookup tables may vary: ProposalStatus, BidStatuses, etc.)
            "Proposals",
            // Bills ETL - lookup tables handled dynamically
            "Bills", "Contracts",
            // Intakes ETL
            "Intakes", "PayTypes",
            // Tasks ETL - priority table may be TaskPriorities or Priorities (handled dynamically)
            "Tasks",
            // Conversations ETL - link tables may be TasksProjects/ProjectsTasks, TasksContacts/ContactsTasks (handled dynamically)
            // ProjectHours ETL - step tables may be HoursReportsSteps, ProjectSteps, etc. (handled dynamically)
            "HoursReports",
            // TimeHourReports ETL — attendance/time-clock table (separate from HoursReports)
            "TimeHourReports"
        };

        // Query existing tables and store for ETL methods to check
        _availableSourceTables = (await sourceConn.QueryAsync<string>(@"
            SELECT TABLE_NAME 
            FROM INFORMATION_SCHEMA.TABLES 
            WHERE TABLE_TYPE = 'BASE TABLE'")).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingTables = expectedTables.Where(t => !_availableSourceTables.Contains(t)).ToList();

        if (missingTables.Any())
        {
            // WARNING only - do not abort
            var warnMsg = $"Some expected tables not found in source database: {string.Join(", ", missingTables)}";
            _logger.LogWarning(warnMsg);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"        [WARNING] {warnMsg}");
            Console.WriteLine($"        ETL will continue - individual entities will handle missing dependencies.");
            Console.ResetColor();
        }
        else
        {
            Console.WriteLine($"        ✓ All {expectedTables.Length} expected tables found");
        }

        // Log available table count
        _logger.LogInformation("Source database contains {Count} tables", _availableSourceTables.Count);
    }

    /// <summary>
    /// Helper method for ETL methods to check if a table exists in source DB.
    /// </summary>
    private bool SourceTableExists(string tableName) => _availableSourceTables.Contains(tableName);

    /// <summary>
    /// Gets the columns of a table from the source database.
    /// Returns empty set if table doesn't exist.
    /// </summary>
    private async Task<HashSet<string>> GetTableColumnsAsync(SqlConnection sourceConn, string tableName)
    {
        var columns = await sourceConn.QueryAsync<string>(@"
            SELECT COLUMN_NAME 
            FROM INFORMATION_SCHEMA.COLUMNS 
            WHERE TABLE_NAME = @TableName",
            new { TableName = tableName });
        return columns.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Finds the first matching column from a list of candidates.
    /// Returns null if none found.
    /// </summary>
    private string? FindColumn(HashSet<string> availableColumns, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (availableColumns.Contains(candidate))
                return candidate;
        }
        return null;
    }

    private async Task ClearReplicaDataAsync(SqlConnection replicaConn)
    {
        _logger.LogInformation("Clearing existing data in replica tables...");

        // Tables are already dropped/recreated in CreateReplicaSchemaAsync
        // But clear sync state to ensure fresh watermarks
        await replicaConn.ExecuteAsync("IF OBJECT_ID('Sync_State', 'U') IS NOT NULL DELETE FROM Sync_State");

        _logger.LogInformation("Replica data cleared");
    }

    // ETL implementations - map source DB schema to API-compatible schema
    // Source DB tables have different column names than API responses
    // We transform during ETL to ensure Replica_DB works with both ETL and API sync

    private async Task<int> EtlProjectsAsync(SqlConnection source, SqlConnection replica)
    {
        _logger.LogInformation("ETL: Projects");
        Console.WriteLine("    → Loading Projects...");

        // ETL query: Source DB → API-compatible schema
        var data = await source.QueryAsync<dynamic>(@"
            SELECT 
                p.ID,
                p.Name,
                p.ProjectNum,
                p.StartDate,
                p.EndDate,
                p.Description,
                c.Name AS CustomerName,
                p.CustomerID,
                p.ProjectManagerID AS EmployeeID,
                CONCAT(e.FirstName, ' ', e.LastName) AS EmployeeName,
                p.StatusID,
                ps.Name AS StatusName,
                p.ProjectTopicsTypeID AS ProjectTypeID,
                ptt.Name AS ProjectType,
                p.StudioDepartmentTypeID,
                sdt.Name AS StudioDepartmentType,
                p.IsActive,
                CAST(ped.FeeSum AS DECIMAL(18,2)) AS FeeSum,
                p.LastUpdated
            FROM Projects p WITH (NOLOCK)
            LEFT JOIN Companies c WITH (NOLOCK) ON c.ID = p.CustomerID
            LEFT JOIN Employees e WITH (NOLOCK) ON e.ID = p.ProjectManagerID
            LEFT JOIN ProjectStatuses ps WITH (NOLOCK) ON ps.ID = p.StatusID
            LEFT JOIN ProjectTopicsTypes ptt WITH (NOLOCK) ON ptt.ID = p.ProjectTopicsTypeID
            LEFT JOIN StudioDepartmentTypes sdt WITH (NOLOCK) ON sdt.ID = p.StudioDepartmentTypeID
            OUTER APPLY (
                SELECT TOP 1 ped2.FeeSum
                FROM ProjectsExtraData ped2 WITH (NOLOCK)
                WHERE ped2.ProjectID = p.ID
            ) ped
            WHERE p.ID IS NOT NULL");

        var list = data.ToList();
        if (list.Count == 0) return 0;

        // SAFETY CHECK: Detect duplicate Project IDs before inserting
        var duplicates = list
            .GroupBy(x => (int)x.ID)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Any())
        {
            var errorMsg = $"ETL ABORTED: Duplicate Project IDs detected: {string.Join(", ", duplicates.Take(10))}" +
                          (duplicates.Count > 10 ? $" (and {duplicates.Count - 10} more)" : "");
            _logger.LogError(errorMsg);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"        [ERROR] {errorMsg}");
            Console.ResetColor();
            throw new InvalidOperationException(errorMsg);
        }

        foreach (var item in list)
        {
            await replica.ExecuteAsync(@"
                INSERT INTO MP_Projects (ID, Name, ProjectNum, StartDate, EndDate, Description,
                    CustomerName, CustomerID, EmployeeID, EmployeeName, StatusID, StatusName,
                    ProjectTypeID, ProjectType, StudioDepartmentTypeID, StudioDepartmentType,
                    IsActive, FeeSum, LastUpdated)
                VALUES (@ID, @Name, @ProjectNum, @StartDate, @EndDate, @Description,
                    @CustomerName, @CustomerID, @EmployeeID, @EmployeeName, @StatusID, @StatusName,
                    @ProjectTypeID, @ProjectType, @StudioDepartmentTypeID, @StudioDepartmentType,
                    @IsActive, @FeeSum, @LastUpdated)",
                (object)item);
        }

        Console.WriteLine($"        Projects: {list.Count} records");
        return list.Count;
    }

    private async Task<int> EtlCompaniesAsync(SqlConnection source, SqlConnection replica)
    {
        _logger.LogInformation("ETL: Companies");
        Console.WriteLine("    → Loading Companies...");

        // ETL query: Source DB → API-compatible schema (City, RegistrationNumber, PhoneNum)
        // IMPORTANT: Use OUTER APPLY (TOP 1) to guarantee exactly ONE row per company
        // This prevents PK violations from duplicate addresses/phones
        var data = await source.QueryAsync<dynamic>(@"
            SELECT 
                c.ID,
                c.Name,
                addr.Address,
                addr.City,
                c.Email,
                c.RegistrationNumber,
                phone.PhoneNum,
                c.LastUpdated
            FROM Companies c WITH (NOLOCK)
            -- Get exactly ONE address per company (prefer Primary, then most recent)
            OUTER APPLY (
                SELECT TOP 1 
                    ca.Address,
                    ct.Name AS City
                FROM CompanyAddresses ca WITH (NOLOCK)
                LEFT JOIN Cities ct WITH (NOLOCK) ON ct.ID = ca.CityID
                WHERE ca.CompanyID = c.ID
                ORDER BY ca.IsPrimary DESC, ca.ID DESC
            ) addr
            -- Get exactly ONE phone per company (prefer Primary, then most recent)
            OUTER APPLY (
                SELECT TOP 1 
                    cp.PhoneNum
                FROM CompanyPhones cp WITH (NOLOCK)
                WHERE cp.CompanyID = c.ID
                ORDER BY cp.IsPrimary DESC, cp.ID DESC
            ) phone
            WHERE c.ID IS NOT NULL");

        var list = data.ToList();
        if (list.Count == 0) return 0;

        // SAFETY CHECK: Detect duplicate Company IDs before inserting
        var duplicates = list
            .GroupBy(x => (int)x.ID)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Any())
        {
            var errorMsg = $"ETL ABORTED: Duplicate Company IDs detected: {string.Join(", ", duplicates.Take(10))}" +
                          (duplicates.Count > 10 ? $" (and {duplicates.Count - 10} more)" : "");
            _logger.LogError(errorMsg);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"        [ERROR] {errorMsg}");
            Console.ResetColor();
            throw new InvalidOperationException(errorMsg);
        }

        foreach (var item in list)
        {
            await replica.ExecuteAsync(@"
                INSERT INTO MP_Companies (ID, Name, Address, City, Email, RegistrationNumber, PhoneNum, LastUpdated)
                VALUES (@ID, @Name, @Address, @City, @Email, @RegistrationNumber, @PhoneNum, @LastUpdated)",
                (object)item);
        }

        Console.WriteLine($"        Companies: {list.Count} records");
        return list.Count;
    }

    private async Task<int> EtlContactsAsync(SqlConnection source, SqlConnection replica)
    {
        _logger.LogInformation("ETL: Contacts");
        Console.WriteLine("    → Loading Contacts...");

        // ETL query: Source DB → API-compatible schema (Address, no FullName/Title/IsActive)
        // IMPORTANT: Use OUTER APPLY (TOP 1) to guarantee exactly ONE row per contact
        var data = await source.QueryAsync<dynamic>(@"
            SELECT 
                c.ID,
                c.FirstName,
                c.LastName,
                co.Name AS CompanyName,
                c.CompanyID,
                addr.Address,
                COALESCE(c.OfficeEmail, c.PrivateEmail) AS Email,
                phone.PhoneNum AS Phone,
                mobile.PhoneNum AS Mobile,
                c.LastUpdated
            FROM Contacts c WITH (NOLOCK)
            LEFT JOIN Companies co WITH (NOLOCK) ON co.ID = c.CompanyID
            -- Get exactly ONE address from company (prefer Primary, then most recent)
            OUTER APPLY (
                SELECT TOP 1 ca.Address
                FROM CompanyAddresses ca WITH (NOLOCK)
                WHERE ca.CompanyID = c.CompanyID
                ORDER BY ca.IsPrimary DESC, ca.ID DESC
            ) addr
            -- Get exactly ONE phone per contact (type 1 = phone)
            OUTER APPLY (
                SELECT TOP 1 cp.PhoneNum
                FROM ContactPhones cp WITH (NOLOCK)
                WHERE cp.ContactID = c.ID AND cp.PhoneTypeID = 1
                ORDER BY cp.IsPrimary DESC, cp.ID DESC
            ) phone
            -- Get exactly ONE mobile per contact (type 3 = mobile)
            OUTER APPLY (
                SELECT TOP 1 cp.PhoneNum
                FROM ContactPhones cp WITH (NOLOCK)
                WHERE cp.ContactID = c.ID AND cp.PhoneTypeID = 3
                ORDER BY cp.IsPrimary DESC, cp.ID DESC
            ) mobile
            WHERE c.ID IS NOT NULL");

        var list = data.ToList();
        if (list.Count == 0) return 0;

        // SAFETY CHECK: Detect duplicate Contact IDs before inserting
        var duplicates = list
            .GroupBy(x => (int)x.ID)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Any())
        {
            var errorMsg = $"ETL ABORTED: Duplicate Contact IDs detected: {string.Join(", ", duplicates.Take(10))}" +
                          (duplicates.Count > 10 ? $" (and {duplicates.Count - 10} more)" : "");
            _logger.LogError(errorMsg);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"        [ERROR] {errorMsg}");
            Console.ResetColor();
            throw new InvalidOperationException(errorMsg);
        }

        foreach (var item in list)
        {
            await replica.ExecuteAsync(@"
                INSERT INTO MP_Contacts (ID, FirstName, LastName, CompanyName, CompanyID, Address, Email, Phone, Mobile, LastUpdated)
                VALUES (@ID, @FirstName, @LastName, @CompanyName, @CompanyID, @Address, @Email, @Phone, @Mobile, @LastUpdated)",
                (object)item);
        }

        Console.WriteLine($"        Contacts: {list.Count} records");
        return list.Count;
    }

    private async Task<int> EtlEmployeesAsync(SqlConnection source, SqlConnection replica)
    {
        _logger.LogInformation("ETL: Employees");
        Console.WriteLine("    → Loading Employees...");

        // ETL query: Source DB → API-compatible schema (minimal: ID, FirstName, LastName, LastUpdated)
        var data = await source.QueryAsync<dynamic>(@"
            SELECT 
                e.ID,
                e.FirstName,
                e.LastName,
                e.LastUpdated
            FROM Employees e WITH (NOLOCK)
            WHERE e.ID IS NOT NULL");

        var list = data.ToList();
        if (list.Count == 0) return 0;

        // SAFETY CHECK: Detect duplicate Employee IDs before inserting
        var duplicates = list
            .GroupBy(x => (int)x.ID)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Any())
        {
            var errorMsg = $"ETL ABORTED: Duplicate Employee IDs detected: {string.Join(", ", duplicates.Take(10))}" +
                          (duplicates.Count > 10 ? $" (and {duplicates.Count - 10} more)" : "");
            _logger.LogError(errorMsg);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"        [ERROR] {errorMsg}");
            Console.ResetColor();
            throw new InvalidOperationException(errorMsg);
        }

        foreach (var item in list)
        {
            await replica.ExecuteAsync(@"
                INSERT INTO MP_Employees (ID, FirstName, LastName, LastUpdated)
                VALUES (@ID, @FirstName, @LastName, @LastUpdated)",
                (object)item);
        }

        Console.WriteLine($"        Employees: {list.Count} records");
        return list.Count;
    }

    private async Task<int> EtlBidsAsync(SqlConnection source, SqlConnection replica)
    {
        _logger.LogInformation("ETL: Bids");
        Console.WriteLine("    → Loading Bids...");

        // Check if required base table exists
        if (!SourceTableExists("Proposals"))
        {
            _logger.LogWarning("Skipping Bids ETL - Proposals table not found in source database");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("        [SKIP] Proposals table not found - skipping Bids ETL");
            Console.ResetColor();
            return 0;
        }

        // Get actual columns from Proposals table
        var proposalsColumns = await GetTableColumnsAsync(source, "Proposals");
        _logger.LogDebug("Proposals table columns: {Columns}", string.Join(", ", proposalsColumns));

        // Validate required columns exist
        var requiredColumns = new[] { "ID", "LastUpdated" };
        var missingRequired = requiredColumns.Where(c => !proposalsColumns.Contains(c)).ToList();
        if (missingRequired.Any())
        {
            var errorMsg = $"Skipping Bids ETL - Required columns missing in Proposals table: {string.Join(", ", missingRequired)}";
            _logger.LogWarning(errorMsg);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"        [SKIP] {errorMsg}");
            Console.ResetColor();
            return 0;
        }

        // Find the amount column (try multiple naming conventions)
        var amountColumn = FindColumn(proposalsColumns, "Sum", "Amount", "EstimatedSum", "TotalSum", "TotalAmount", "ProposalSum", "Value");
        if (amountColumn == null)
        {
            _logger.LogWarning("No amount column found in Proposals (tried: Sum, Amount, EstimatedSum, TotalSum, TotalAmount, ProposalSum, Value) - EstimatedSum will be NULL");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("        [WARNING] No amount column found - EstimatedSum will be NULL");
            Console.ResetColor();
        }
        else
        {
            _logger.LogInformation("Using '{Column}' as amount column for Bids ETL", amountColumn);
        }

        // Find optional columns (try multiple naming conventions)
        var proposalNumColumn = FindColumn(proposalsColumns, "ProposalNum", "BidNum", "Number", "Num", "ProposalNumber");
        var nameColumn = FindColumn(proposalsColumns, "Name", "Title", "Description", "ProposalName");
        var dateTimeColumn = FindColumn(proposalsColumns, "DateTime", "Date", "CreatedDate", "ProposalDate", "BidDate");
        var statusIdColumn = FindColumn(proposalsColumns, "StatusID", "ProposalStatusID", "BidStatusID", "Status");
        var probabilityIdColumn = FindColumn(proposalsColumns, "ProbabilityID", "WinProbabilityID", "ProbabilityId");

        // Check for optional lookup tables (try multiple naming conventions)
        var hasProposalStatus = SourceTableExists("ProposalStatus") || SourceTableExists("ProposalStatuses") || SourceTableExists("BidStatuses");
        var hasProposalProbabilities = SourceTableExists("ProposalProbabilities") || SourceTableExists("Proposalpriorities");

        // Determine actual table names
        var statusTable = SourceTableExists("ProposalStatus") ? "ProposalStatus" :
                         SourceTableExists("ProposalStatuses") ? "ProposalStatuses" :
                         SourceTableExists("BidStatuses") ? "BidStatuses" : null;
        var probabilityTable = SourceTableExists("ProposalProbabilities") ? "ProposalProbabilities" :
                              SourceTableExists("Proposalpriorities") ? "Proposalpriorities" : null;

        // Build dynamic SELECT clause based on available columns
        var selectClauses = new List<string>
        {
            "pr.ID",
            proposalNumColumn != null ? $"pr.{proposalNumColumn} AS ProposalNum" : "CAST(NULL AS NVARCHAR(100)) AS ProposalNum",
            nameColumn != null ? $"pr.{nameColumn} AS Name" : "CAST(NULL AS NVARCHAR(500)) AS Name",
            "CAST(1 AS BIT) AS ActiveProposal",
            dateTimeColumn != null ? $"pr.{dateTimeColumn} AS [DateTime]" : "CAST(NULL AS DATETIME2) AS [DateTime]",
            amountColumn != null ? $"pr.{amountColumn} AS EstimatedSum" : "CAST(NULL AS DECIMAL(18,2)) AS EstimatedSum",
            probabilityIdColumn != null ? $"pr.{probabilityIdColumn} AS ProbabilityID" : "CAST(NULL AS INT) AS ProbabilityID"
        };

        // Add probability name (from lookup or NULL)
        if (hasProposalProbabilities && probabilityIdColumn != null)
            selectClauses.Add("pp.Name AS ProbabilityName");
        else
            selectClauses.Add("CAST(NULL AS NVARCHAR(200)) AS ProbabilityName");

        // Add status ID
        selectClauses.Add(statusIdColumn != null ? $"pr.{statusIdColumn} AS StatusID" : "CAST(NULL AS INT) AS StatusID");

        // Add status name (from lookup or NULL)
        if (hasProposalStatus && statusIdColumn != null)
            selectClauses.Add("ps.Name AS ProposalStatus");
        else
            selectClauses.Add("CAST(NULL AS NVARCHAR(200)) AS ProposalStatus");

        selectClauses.Add("pr.LastUpdated");

        // Build JOINs
        var joins = new List<string>();
        if (hasProposalStatus && statusIdColumn != null)
            joins.Add($"LEFT JOIN {statusTable} ps WITH (NOLOCK) ON ps.ID = pr.{statusIdColumn}");
        if (hasProposalProbabilities && probabilityIdColumn != null)
            joins.Add($"LEFT JOIN {probabilityTable} pp WITH (NOLOCK) ON pp.ID = pr.{probabilityIdColumn}");

        var sql = $@"
            SELECT 
                {string.Join(",\n                ", selectClauses)}
            FROM Proposals pr WITH (NOLOCK)
            {string.Join("\n            ", joins)}
            WHERE pr.ID IS NOT NULL";

        _logger.LogDebug("Bids ETL SQL: {Sql}", sql);

        IEnumerable<dynamic> data;
        try
        {
            data = await source.QueryAsync<dynamic>(sql);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bids ETL query failed - skipping entity");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"        [ERROR] Query failed: {ex.Message}");
            Console.ResetColor();
            return 0;
        }

        var list = data.ToList();
        if (list.Count == 0) return 0;

        // SAFETY CHECK: Detect duplicate Bid IDs before inserting
        var duplicates = list
            .GroupBy(x => (int)x.ID)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Any())
        {
            var errorMsg = $"ETL ABORTED: Duplicate Bid IDs detected: {string.Join(", ", duplicates.Take(10))}" +
                          (duplicates.Count > 10 ? $" (and {duplicates.Count - 10} more)" : "");
            _logger.LogError(errorMsg);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"        [ERROR] {errorMsg}");
            Console.ResetColor();
            throw new InvalidOperationException(errorMsg);
        }

        foreach (var item in list)
        {
            await replica.ExecuteAsync(@"
                INSERT INTO MP_Bids (ID, ProposalNum, Name, ActiveProposal, [DateTime], EstimatedSum,
                    ProbabilityID, ProbabilityName, StatusID, ProposalStatus, LastUpdated)
                VALUES (@ID, @ProposalNum, @Name, @ActiveProposal, @DateTime, @EstimatedSum,
                    @ProbabilityID, @ProbabilityName, @StatusID, @ProposalStatus, @LastUpdated)",
                (object)item);
        }

        Console.WriteLine($"        Bids: {list.Count} records");
        return list.Count;
    }

    private async Task<int> EtlBillsAsync(SqlConnection source, SqlConnection replica)
    {
        _logger.LogInformation("ETL: Bills");
        Console.WriteLine("    → Loading Bills...");

        // Check if required base table exists
        if (!SourceTableExists("Bills"))
        {
            _logger.LogWarning("Skipping Bills ETL - Bills table not found in source database");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("        [SKIP] Bills table not found - skipping Bills ETL");
            Console.ResetColor();
            return 0;
        }

        // Get actual columns from relevant tables
        var billsColumns = await GetTableColumnsAsync(source, "Bills");
        var contractsColumns = SourceTableExists("Contracts") ? await GetTableColumnsAsync(source, "Contracts") : new HashSet<string>();
        var projectsColumns = SourceTableExists("Projects") ? await GetTableColumnsAsync(source, "Projects") : new HashSet<string>();

        _logger.LogDebug("Bills table columns: {Columns}", string.Join(", ", billsColumns));
        _logger.LogDebug("Contracts table columns: {Columns}", string.Join(", ", contractsColumns));

        // Validate required columns exist
        if (!billsColumns.Contains("ID"))
        {
            _logger.LogWarning("Skipping Bills ETL - Required column 'ID' missing in Bills table");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("        [SKIP] Required column 'ID' missing - skipping Bills ETL");
            Console.ResetColor();
            return 0;
        }

        // Check if we can join to Contracts (required for Project linkage)
        var hasContracts = SourceTableExists("Contracts");
        var contractIdColumn = FindColumn(billsColumns, "ContractID", "ContractId", "Contract_ID");
        if (!hasContracts || contractIdColumn == null)
        {
            _logger.LogWarning("Bills ETL: Contracts table or ContractID column not found - Project/Responsible fields will be NULL");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("        [WARNING] Contracts linkage unavailable - some fields will be NULL");
            Console.ResetColor();
        }

        // Find column names dynamically
        var billNumColumn = FindColumn(billsColumns, "BillNum", "BillNumber", "Number", "Num", "InvoiceNum");
        var sumColumn = FindColumn(billsColumns, "Sum", "Amount", "Total", "TotalSum", "BillSum", "Value");
        var submitDateColumn = FindColumn(billsColumns, "SubmitDate", "Date", "BillDate", "CreatedDate", "InvoiceDate");
        var collectionDateColumn = FindColumn(billsColumns, "CollectionDate", "PaidDate", "PaymentDate", "DueDate");
        var statusIdColumn = FindColumn(billsColumns, "StatusID", "BillStatusID", "Status");

        // Find responsible column: prioritize ConfirmatorID as per mapping rule
        // Source.ConfirmatorID -> Replica.ResponsibleEmployeeID
        var responsibleColumn = FindColumn(contractsColumns, "ConfirmatorID", "Confirmator_ID", "ConfirmatorId",
            "ResponsibleID", "ResponsibleEmployeeID", "EmployeeID", "OwnerID", "UserID", "ManagerID", "AssignedToID");
        var projectIdColumn = FindColumn(contractsColumns, "ProjectID", "ProjectId", "Project_ID");

        if (responsibleColumn != null && hasContracts)
        {
            _logger.LogInformation("Using '{Column}' as responsible employee column for Bills ETL", responsibleColumn);
        }
        else if (hasContracts)
        {
            _logger.LogWarning("No responsible column found in Contracts (tried: ConfirmatorID, ResponsibleID, EmployeeID, etc.) - ResponsibleEmployee will be NULL");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("        [WARNING] No responsible column found - ResponsibleEmployee will be NULL");
            Console.ResetColor();
        }

        if (sumColumn == null)
        {
            _logger.LogWarning("No amount column found in Bills (tried: Sum, Amount, Total, TotalSum, BillSum, Value) - Sum will be NULL");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("        [WARNING] No amount column found - Sum will be NULL");
            Console.ResetColor();
        }

        // Check for lookup tables
        var hasBillStatuses = SourceTableExists("BillStatuses") || SourceTableExists("BillStatus");
        var statusTable = SourceTableExists("BillStatuses") ? "BillStatuses" : 
                         SourceTableExists("BillStatus") ? "BillStatus" : null;
        var hasStudioDeptTypes = SourceTableExists("StudioDepartmentTypes");
        var studioDeptIdColumn = FindColumn(projectsColumns, "StudioDepartmentTypeID", "StudioDepartmentID", "DepartmentTypeID");

        // Build dynamic SELECT clause
        var selectClauses = new List<string> { "b.ID" };

        // BillNum
        selectClauses.Add(billNumColumn != null ? $"b.{billNumColumn} AS BillNum" : "CAST(NULL AS NVARCHAR(100)) AS BillNum");

        // Project fields (from Contracts → Projects)
        if (hasContracts && projectIdColumn != null && SourceTableExists("Projects"))
        {
            selectClauses.Add("p.Name AS ProjectName");
            selectClauses.Add($"ct.{projectIdColumn} AS ProjectID");
        }
        else
        {
            selectClauses.Add("CAST(NULL AS NVARCHAR(500)) AS ProjectName");
            selectClauses.Add("CAST(NULL AS INT) AS ProjectID");
        }

        // BillInternalNum (same as BillNum typically)
        selectClauses.Add(billNumColumn != null ? $"b.{billNumColumn} AS BillInternalNum" : "CAST(NULL AS NVARCHAR(100)) AS BillInternalNum");

        // Sum
        selectClauses.Add(sumColumn != null ? $"b.{sumColumn} AS [Sum]" : "CAST(NULL AS DECIMAL(18,2)) AS [Sum]");

        // Dates
        selectClauses.Add(submitDateColumn != null ? $"b.{submitDateColumn} AS SubmitDate" : "CAST(NULL AS DATETIME2) AS SubmitDate");
        selectClauses.Add(collectionDateColumn != null ? $"b.{collectionDateColumn} AS CollectionDate" : "CAST(NULL AS DATETIME2) AS CollectionDate");

        // Status
        if (hasBillStatuses && statusIdColumn != null)
            selectClauses.Add("bs.Name AS Status");
        else
            selectClauses.Add("CAST(NULL AS NVARCHAR(200)) AS Status");
        selectClauses.Add(statusIdColumn != null ? $"b.{statusIdColumn} AS StatusID" : "CAST(NULL AS INT) AS StatusID");

        // Responsible Employee
        if (hasContracts && responsibleColumn != null && SourceTableExists("Employees"))
        {
            selectClauses.Add("CONCAT(e.FirstName, ' ', e.LastName) AS ResponsibleEmployee");
            selectClauses.Add($"ct.{responsibleColumn} AS ResponsibleEmployeeID");
        }
        else
        {
            selectClauses.Add("CAST(NULL AS NVARCHAR(500)) AS ResponsibleEmployee");
            selectClauses.Add("CAST(NULL AS INT) AS ResponsibleEmployeeID");
        }

        // Studio Department
        if (hasContracts && projectIdColumn != null && hasStudioDeptTypes && studioDeptIdColumn != null)
        {
            selectClauses.Add("sdt.Name AS StudioDepartment");
            selectClauses.Add($"p.{studioDeptIdColumn} AS StudioDepartmentTypeID");
        }
        else
        {
            selectClauses.Add("CAST(NULL AS NVARCHAR(200)) AS StudioDepartment");
            selectClauses.Add("CAST(NULL AS INT) AS StudioDepartmentTypeID");
        }

        // Build JOINs dynamically
        var joins = new List<string>();
        if (hasContracts && contractIdColumn != null)
        {
            joins.Add($"LEFT JOIN Contracts ct WITH (NOLOCK) ON ct.ID = b.{contractIdColumn}");
            if (projectIdColumn != null && SourceTableExists("Projects"))
                joins.Add($"LEFT JOIN Projects p WITH (NOLOCK) ON p.ID = ct.{projectIdColumn}");
            if (responsibleColumn != null && SourceTableExists("Employees"))
                joins.Add($"LEFT JOIN Employees e WITH (NOLOCK) ON e.ID = ct.{responsibleColumn}");
            if (projectIdColumn != null && hasStudioDeptTypes && studioDeptIdColumn != null)
                joins.Add($"LEFT JOIN StudioDepartmentTypes sdt WITH (NOLOCK) ON sdt.ID = p.{studioDeptIdColumn}");
        }
        if (hasBillStatuses && statusIdColumn != null)
            joins.Add($"LEFT JOIN {statusTable} bs WITH (NOLOCK) ON bs.ID = b.{statusIdColumn}");

        var sql = $@"
            SELECT 
                {string.Join(",\n                ", selectClauses)}
            FROM Bills b WITH (NOLOCK)
            {string.Join("\n            ", joins)}
            WHERE b.ID IS NOT NULL";

        _logger.LogDebug("Bills ETL SQL: {Sql}", sql);

        IEnumerable<dynamic> data;
        try
        {
            data = await source.QueryAsync<dynamic>(sql);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bills ETL query failed - skipping entity");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"        [ERROR] Query failed: {ex.Message}");
            Console.ResetColor();
            return 0;
        }

        var list = data.ToList();
        if (list.Count == 0) return 0;

        // SAFETY CHECK: Detect duplicate Bill IDs before inserting
        var duplicates = list
            .GroupBy(x => (int)x.ID)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Any())
        {
            var errorMsg = $"ETL ABORTED: Duplicate Bill IDs detected: {string.Join(", ", duplicates.Take(10))}" +
                          (duplicates.Count > 10 ? $" (and {duplicates.Count - 10} more)" : "");
            _logger.LogError(errorMsg);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"        [ERROR] {errorMsg}");
            Console.ResetColor();
            throw new InvalidOperationException(errorMsg);
        }

        foreach (var item in list)
        {
            await replica.ExecuteAsync(@"
                INSERT INTO MP_Bills (ID, BillNum, ProjectName, ProjectID, BillInternalNum, [Sum],
                    SubmitDate, CollectionDate, Status, StatusID, ResponsibleEmployee, ResponsibleEmployeeID,
                    StudioDepartment, StudioDepartmentTypeID, LastUpdated)
                VALUES (@ID, @BillNum, @ProjectName, @ProjectID, @BillInternalNum, @Sum,
                    @SubmitDate, @CollectionDate, @Status, @StatusID, @ResponsibleEmployee, @ResponsibleEmployeeID,
                    @StudioDepartment, @StudioDepartmentTypeID, GETUTCDATE())",
                (object)item);
        }

        Console.WriteLine($"        Bills: {list.Count} records");
        return list.Count;
    }

    private async Task<int> EtlIntakesAsync(SqlConnection source, SqlConnection replica)
    {
        _logger.LogInformation("ETL: Intakes");
        Console.WriteLine("    → Loading Intakes...");

        // Check if required base table exists
        if (!SourceTableExists("Intakes"))
        {
            _logger.LogWarning("Skipping Intakes ETL - Intakes table not found in source database");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("        [SKIP] Intakes table not found - skipping Intakes ETL");
            Console.ResetColor();
            return 0;
        }

        // Get actual columns from Intakes table
        var intakesColumns = await GetTableColumnsAsync(source, "Intakes");
        _logger.LogDebug("Intakes table columns: {Columns}", string.Join(", ", intakesColumns));

        // Validate required columns exist
        if (!intakesColumns.Contains("ID"))
        {
            _logger.LogWarning("Skipping Intakes ETL - Required column 'ID' missing in Intakes table");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("        [SKIP] Required column 'ID' missing - skipping Intakes ETL");
            Console.ResetColor();
            return 0;
        }

        // Find column names dynamically
        var dateTimeColumn = FindColumn(intakesColumns, "DateTime", "Date", "OpenDate", "CreatedDate", "IntakeDate");
        var sumColumn = FindColumn(intakesColumns, "Sum", "Amount", "Total", "Value");
        var customerIdColumn = FindColumn(intakesColumns, "CustomerID", "CustomerId", "CompanyID", "ClientID");
        var payTypeIdColumn = FindColumn(intakesColumns, "PayTypeID", "PaymentTypeID", "PayTypeId");
        var descriptionColumn = FindColumn(intakesColumns, "Description", "Notes", "Comment", "Remarks");
        var lastUpdatedColumn = FindColumn(intakesColumns, "LastUpdated", "ModifiedDate", "UpdatedDate");

        // Check for customer table (Companies is the source of truth for CustomerName)
        // CustomerName must ALWAYS be derived from CustomerID, not from any existing field
        var hasCustomers = SourceTableExists("Companies") || SourceTableExists("Customers");
        var customerTable = SourceTableExists("Companies") ? "Companies" : 
                           SourceTableExists("Customers") ? "Customers" : null;

        if (!hasCustomers)
        {
            _logger.LogWarning("Customer table not found (tried: Companies, Customers) - CustomerName will be NULL");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("        [WARNING] Customer table not found - CustomerName will be NULL");
            Console.ResetColor();
        }
        else
        {
            _logger.LogInformation("Using '{Table}' as customer table for Intakes ETL (CustomerName derived from CustomerID)", customerTable);
        }

        // Check for PayTypes table
        var hasPayTypes = SourceTableExists("PayTypes") || SourceTableExists("PaymentTypes");
        var payTypesTable = SourceTableExists("PayTypes") ? "PayTypes" : 
                           SourceTableExists("PaymentTypes") ? "PaymentTypes" : null;

        // Build dynamic SELECT clause
        var selectClauses = new List<string> { "i.ID" };

        // OpenDate
        selectClauses.Add(dateTimeColumn != null ? $"i.{dateTimeColumn} AS OpenDate" : "CAST(NULL AS DATETIME2) AS OpenDate");

        // Sum
        selectClauses.Add(sumColumn != null ? $"i.{sumColumn} AS [Sum]" : "CAST(NULL AS DECIMAL(18,2)) AS [Sum]");

        // CustomerID
        selectClauses.Add(customerIdColumn != null ? $"i.{customerIdColumn} AS CustomerID" : "CAST(NULL AS INT) AS CustomerID");

        // CustomerName - ALWAYS derived from CustomerID via JOIN, never from source field
        // If CustomerID is NULL or customer record missing, CustomerName will be NULL
        if (hasCustomers && customerIdColumn != null)
            selectClauses.Add("cust.Name AS CustomerName");
        else
            selectClauses.Add("CAST(NULL AS NVARCHAR(500)) AS CustomerName");

        // PaymentType
        if (hasPayTypes && payTypeIdColumn != null)
            selectClauses.Add("pt.Name AS PaymentType");
        else
            selectClauses.Add("CAST(NULL AS NVARCHAR(200)) AS PaymentType");

        // PayTypeID
        selectClauses.Add(payTypeIdColumn != null ? $"i.{payTypeIdColumn} AS PayTypeID" : "CAST(NULL AS INT) AS PayTypeID");

        // Description
        selectClauses.Add(descriptionColumn != null ? $"i.{descriptionColumn} AS Description" : "CAST(NULL AS NVARCHAR(MAX)) AS Description");

        // LastUpdated
        selectClauses.Add(lastUpdatedColumn != null ? $"i.{lastUpdatedColumn} AS LastUpdated" : "GETUTCDATE() AS LastUpdated");

        // Build JOINs - CustomerName is strictly from customer table based on CustomerID
        var joins = new List<string>();
        if (hasCustomers && customerIdColumn != null)
            joins.Add($"LEFT JOIN {customerTable} cust WITH (NOLOCK) ON cust.ID = i.{customerIdColumn}");
        if (hasPayTypes && payTypeIdColumn != null)
            joins.Add($"LEFT JOIN {payTypesTable} pt WITH (NOLOCK) ON pt.ID = i.{payTypeIdColumn}");

        var sql = $@"
            SELECT 
                {string.Join(",\n                ", selectClauses)}
            FROM Intakes i WITH (NOLOCK)
            {string.Join("\n            ", joins)}
            WHERE i.ID IS NOT NULL";

        _logger.LogDebug("Intakes ETL SQL: {Sql}", sql);

        IEnumerable<dynamic> data;
        try
        {
            data = await source.QueryAsync<dynamic>(sql);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Intakes ETL query failed - skipping entity");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"        [ERROR] Query failed: {ex.Message}");
            Console.ResetColor();
            return 0;
        }

        var list = data.ToList();
        if (list.Count == 0) return 0;

        // Log warning for intakes with CustomerID but no matching customer record
        if (hasCustomers && customerIdColumn != null)
        {
            var orphanedCustomers = list
                .Where(x => x.CustomerID != null && x.CustomerName == null)
                .Select(x => (int)x.CustomerID)
                .Distinct()
                .ToList();
            if (orphanedCustomers.Any())
            {
                _logger.LogWarning("Intakes reference {Count} CustomerIDs with no matching customer record: {Ids}",
                    orphanedCustomers.Count,
                    string.Join(", ", orphanedCustomers.Take(10)) + (orphanedCustomers.Count > 10 ? "..." : ""));
            }
        }

        // SAFETY CHECK: Detect duplicate Intake IDs before inserting
        var duplicates = list
            .GroupBy(x => (int)x.ID)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Any())
        {
            var errorMsg = $"ETL ABORTED: Duplicate Intake IDs detected: {string.Join(", ", duplicates.Take(10))}" +
                          (duplicates.Count > 10 ? $" (and {duplicates.Count - 10} more)" : "");
            _logger.LogError(errorMsg);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"        [ERROR] {errorMsg}");
            Console.ResetColor();
            throw new InvalidOperationException(errorMsg);
        }

        foreach (var item in list)
        {
            await replica.ExecuteAsync(@"
                INSERT INTO MP_Intakes (ID, OpenDate, [Sum], CustomerID, CustomerName, PaymentType, PayTypeID, Description, LastUpdated)
                VALUES (@ID, @OpenDate, @Sum, @CustomerID, @CustomerName, @PaymentType, @PayTypeID, @Description, @LastUpdated)",
                (object)item);
        }

        Console.WriteLine($"        Intakes: {list.Count} records");
        return list.Count;
    }

    private async Task<int> EtlTasksAsync(SqlConnection source, SqlConnection replica)
    {
        _logger.LogInformation("ETL: Tasks");
        Console.WriteLine("    → Loading Tasks...");

        // Check if required base table exists
        if (!SourceTableExists("Tasks"))
        {
            _logger.LogWarning("Skipping Tasks ETL - Tasks table not found in source database");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("        [SKIP] Tasks table not found - skipping Tasks ETL");
            Console.ResetColor();
            return 0;
        }

        // Get actual columns from Tasks table
        var tasksColumns = await GetTableColumnsAsync(source, "Tasks");
        _logger.LogDebug("Tasks table columns: {Columns}", string.Join(", ", tasksColumns));

        // Validate required columns exist
        if (!tasksColumns.Contains("ID"))
        {
            _logger.LogWarning("Skipping Tasks ETL - Required column 'ID' missing in Tasks table");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("        [SKIP] Required column 'ID' missing - skipping Tasks ETL");
            Console.ResetColor();
            return 0;
        }

        // Find column names dynamically
        var subjectColumn = FindColumn(tasksColumns, "Subject", "Title", "Name", "Description", "TaskName");
        var isCompletedColumn = FindColumn(tasksColumns, "IsCompleted", "Completed", "IsDone", "Done", "IsFinished");
        var createdDateColumn = FindColumn(tasksColumns, "CreatedDate", "CreateDate", "DateCreated", "StartDate");
        var dueDateColumn = FindColumn(tasksColumns, "DueDate", "Deadline", "EndDate", "TargetDate");
        var organizerIdColumn = FindColumn(tasksColumns, "OrganizerID", "CreatorID", "SenderID", "OwnerID", "AssignedByID");
        var recipientIdColumn = FindColumn(tasksColumns, "RecipientID", "AssigneeID", "ReceiverID", "AssignedToID");
        var completionDateColumn = FindColumn(tasksColumns, "CompletionDate", "CompletedDate", "FinishedDate");
        var priorityIdColumn = FindColumn(tasksColumns, "PriorityID", "Priority", "TaskPriorityID");
        var lastUpdatedColumn = FindColumn(tasksColumns, "LastUpdated", "ModifiedDate", "UpdatedDate", "DateModified");
        var isEventColumn = FindColumn(tasksColumns, "IsEvent", "Event");
        var isConversationLogColumn = FindColumn(tasksColumns, "IsConversationLog", "ConversationLog", "IsConversation");

        // Check for optional priority lookup table (try multiple naming conventions)
        var hasPriorities = SourceTableExists("TaskPriorities") || SourceTableExists("Priorities") || SourceTableExists("TaskPriority");
        var priorityTable = SourceTableExists("TaskPriorities") ? "TaskPriorities" :
                           SourceTableExists("Priorities") ? "Priorities" :
                           SourceTableExists("TaskPriority") ? "TaskPriority" : null;

        if (!hasPriorities)
        {
            _logger.LogWarning("Priority lookup table not found (tried: TaskPriorities, Priorities, TaskPriority) - Priority will be NULL");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("        [WARNING] Priority lookup table not found - Priority will be NULL");
            Console.ResetColor();
        }
        else
        {
            _logger.LogInformation("Using '{Table}' as priority lookup table for Tasks ETL", priorityTable);
        }

        // Check for Employees table
        var hasEmployees = SourceTableExists("Employees");

        // Build dynamic SELECT clause
        var selectClauses = new List<string> { "t.ID" };

        // TaskDescription (Subject)
        selectClauses.Add(subjectColumn != null ? $"t.{subjectColumn} AS TaskDescription" : "CAST(NULL AS NVARCHAR(MAX)) AS TaskDescription");

        // IsHandled / IsClosed (from IsCompleted)
        if (isCompletedColumn != null)
        {
            selectClauses.Add($"CAST(CASE WHEN t.{isCompletedColumn} = 1 THEN 1 ELSE 0 END AS BIT) AS IsHandled");
            selectClauses.Add($"CAST(CASE WHEN t.{isCompletedColumn} = 1 THEN 1 ELSE 0 END AS BIT) AS IsClosed");
        }
        else
        {
            selectClauses.Add("CAST(0 AS BIT) AS IsHandled");
            selectClauses.Add("CAST(0 AS BIT) AS IsClosed");
        }

        // StartDate
        selectClauses.Add(createdDateColumn != null ? $"t.{createdDateColumn} AS StartDate" : "CAST(NULL AS DATETIME2) AS StartDate");

        // DueDate
        selectClauses.Add(dueDateColumn != null ? $"t.{dueDateColumn} AS DueDate" : "CAST(NULL AS DATETIME2) AS DueDate");

        // SenderName / SenderID
        if (hasEmployees && organizerIdColumn != null)
        {
            selectClauses.Add("CONCAT(e_sender.FirstName, ' ', e_sender.LastName) AS SenderName");
            selectClauses.Add($"t.{organizerIdColumn} AS SenderID");
        }
        else
        {
            selectClauses.Add("CAST(NULL AS NVARCHAR(500)) AS SenderName");
            selectClauses.Add(organizerIdColumn != null ? $"t.{organizerIdColumn} AS SenderID" : "CAST(NULL AS INT) AS SenderID");
        }

        // ReceiverName / ReceiverID
        if (hasEmployees && recipientIdColumn != null)
        {
            selectClauses.Add("CONCAT(e_receiver.FirstName, ' ', e_receiver.LastName) AS ReceiverName");
            selectClauses.Add($"t.{recipientIdColumn} AS ReceiverID");
        }
        else
        {
            selectClauses.Add("CAST(NULL AS NVARCHAR(500)) AS ReceiverName");
            selectClauses.Add(recipientIdColumn != null ? $"t.{recipientIdColumn} AS ReceiverID" : "CAST(NULL AS INT) AS ReceiverID");
        }

        // CompletionDate
        selectClauses.Add(completionDateColumn != null ? $"t.{completionDateColumn} AS CompletionDate" : "CAST(NULL AS DATETIME2) AS CompletionDate");

        // Priority / PriorityID
        if (hasPriorities && priorityIdColumn != null)
            selectClauses.Add("pr.Name AS Priority");
        else
            selectClauses.Add("CAST(NULL AS NVARCHAR(100)) AS Priority");
        selectClauses.Add(priorityIdColumn != null ? $"t.{priorityIdColumn} AS PriorityID" : "CAST(NULL AS INT) AS PriorityID");

        // LastUpdated
        selectClauses.Add(lastUpdatedColumn != null ? $"t.{lastUpdatedColumn} AS LastUpdated" : "GETUTCDATE() AS LastUpdated");

        // Build JOINs dynamically
        var joins = new List<string>();
        if (hasEmployees && organizerIdColumn != null)
            joins.Add($"LEFT JOIN Employees e_sender WITH (NOLOCK) ON e_sender.ID = t.{organizerIdColumn}");
        if (hasEmployees && recipientIdColumn != null)
            joins.Add($"LEFT JOIN Employees e_receiver WITH (NOLOCK) ON e_receiver.ID = t.{recipientIdColumn}");
        if (hasPriorities && priorityIdColumn != null)
            joins.Add($"LEFT JOIN {priorityTable} pr WITH (NOLOCK) ON pr.ID = t.{priorityIdColumn}");

        // Build WHERE clause (exclude events and conversation logs if those columns exist)
        var whereConditions = new List<string> { "t.ID IS NOT NULL" };
        if (isEventColumn != null)
            whereConditions.Add($"t.{isEventColumn} = 0");
        if (isConversationLogColumn != null)
            whereConditions.Add($"t.{isConversationLogColumn} = 0");

        var sql = $@"
            SELECT 
                {string.Join(",\n                ", selectClauses)}
            FROM Tasks t WITH (NOLOCK)
            {string.Join("\n            ", joins)}
            WHERE {string.Join(" AND ", whereConditions)}";

        _logger.LogDebug("Tasks ETL SQL: {Sql}", sql);

        IEnumerable<dynamic> data;
        try
        {
            data = await source.QueryAsync<dynamic>(sql);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tasks ETL query failed - skipping entity");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"        [ERROR] Query failed: {ex.Message}");
            Console.ResetColor();
            return 0;
        }

        var list = data.ToList();
        if (list.Count == 0) return 0;

        // SAFETY CHECK: Detect duplicate Task IDs before inserting
        var duplicates = list
            .GroupBy(x => (int)x.ID)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Any())
        {
            var errorMsg = $"ETL ABORTED: Duplicate Task IDs detected: {string.Join(", ", duplicates.Take(10))}" +
                          (duplicates.Count > 10 ? $" (and {duplicates.Count - 10} more)" : "");
            _logger.LogError(errorMsg);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"        [ERROR] {errorMsg}");
            Console.ResetColor();
            throw new InvalidOperationException(errorMsg);
        }

        foreach (var item in list)
        {
            await replica.ExecuteAsync(@"
                INSERT INTO MP_Tasks (ID, TaskDescription, IsHandled, IsClosed, StartDate, DueDate,
                    SenderName, SenderID, ReceiverName, ReceiverID, CompletionDate, Priority, PriorityID, LastUpdated)
                VALUES (@ID, @TaskDescription, @IsHandled, @IsClosed, @StartDate, @DueDate,
                    @SenderName, @SenderID, @ReceiverName, @ReceiverID, @CompletionDate, @Priority, @PriorityID, @LastUpdated)",
                (object)item);
        }

        Console.WriteLine($"        Tasks: {list.Count} records");
        return list.Count;
    }

    private async Task<int> EtlConversationsAsync(SqlConnection source, SqlConnection replica)
    {
        _logger.LogInformation("ETL: Conversations");
        Console.WriteLine("    → Loading Conversations...");

        // Conversations in source DB are Tasks WHERE IsConversationLog=1
        // Check if required base table exists
        if (!SourceTableExists("Tasks"))
        {
            _logger.LogWarning("Skipping Conversations ETL - Tasks table not found in source database");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("        [SKIP] Tasks table not found - skipping Conversations ETL");
            Console.ResetColor();
            return 0;
        }

        // Get actual columns from Tasks table
        var tasksColumns = await GetTableColumnsAsync(source, "Tasks");
        _logger.LogDebug("Tasks table columns for Conversations: {Columns}", string.Join(", ", tasksColumns));

        // Validate required columns exist
        if (!tasksColumns.Contains("ID"))
        {
            _logger.LogWarning("Skipping Conversations ETL - Required column 'ID' missing in Tasks table");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("        [SKIP] Required column 'ID' missing - skipping Conversations ETL");
            Console.ResetColor();
            return 0;
        }

        // Find column names dynamically
        var organizerIdColumn = FindColumn(tasksColumns, "OrganizerID", "CreatorID", "EmployeeID", "OwnerID");
        var createdDateColumn = FindColumn(tasksColumns, "CreatedDate", "CreateDate", "DateCreated");
        var dueDateColumn = FindColumn(tasksColumns, "DueDate", "Deadline", "EndDate");
        var subjectColumn = FindColumn(tasksColumns, "Subject", "Title", "Name");
        var notesColumn = FindColumn(tasksColumns, "CompletionDescription", "Notes", "Description", "Body");
        var isConversationLogColumn = FindColumn(tasksColumns, "IsConversationLog", "ConversationLog", "IsConversation");

        // Check for optional link tables (try multiple naming conventions)
        // Project link: ProjectsTasks / TasksProjects / TaskProjects
        var hasProjectLink = SourceTableExists("ProjectsTasks") || SourceTableExists("TasksProjects") || SourceTableExists("TaskProjects");
        var projectLinkTable = SourceTableExists("ProjectsTasks") ? "ProjectsTasks" :
                              SourceTableExists("TasksProjects") ? "TasksProjects" :
                              SourceTableExists("TaskProjects") ? "TaskProjects" : null;

        // Contact link: TasksContacts / ContactsTasks / TaskContacts
        var hasContactLink = SourceTableExists("TasksContacts") || SourceTableExists("ContactsTasks") || SourceTableExists("TaskContacts");
        var contactLinkTable = SourceTableExists("TasksContacts") ? "TasksContacts" :
                              SourceTableExists("ContactsTasks") ? "ContactsTasks" :
                              SourceTableExists("TaskContacts") ? "TaskContacts" : null;

        if (!hasProjectLink)
        {
            _logger.LogWarning("Project link table not found (tried: ProjectsTasks, TasksProjects, TaskProjects) - ProjectID/ProjectName will be NULL");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("        [WARNING] Project link table not found - ProjectID will be NULL");
            Console.ResetColor();
        }
        else
        {
            _logger.LogInformation("Using '{Table}' as project link table for Conversations ETL", projectLinkTable);
        }

        if (!hasContactLink)
        {
            _logger.LogWarning("Contact link table not found (tried: TasksContacts, ContactsTasks, TaskContacts) - ContactID/ContactName will be NULL");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("        [WARNING] Contact link table not found - ContactID will be NULL");
            Console.ResetColor();
        }

        // Detect column names in link tables
        string? projectLinkTaskIdCol = null, projectLinkProjectIdCol = null;
        string? contactLinkTaskIdCol = null, contactLinkContactIdCol = null;

        if (hasProjectLink)
        {
            var linkCols = await GetTableColumnsAsync(source, projectLinkTable!);
            projectLinkTaskIdCol = FindColumn(linkCols, "TaskID", "TaskId", "Task_ID");
            projectLinkProjectIdCol = FindColumn(linkCols, "ProjectID", "ProjectId", "Project_ID");
            if (projectLinkTaskIdCol == null || projectLinkProjectIdCol == null)
            {
                _logger.LogWarning("Project link table '{Table}' missing expected columns - disabling project link", projectLinkTable);
                hasProjectLink = false;
            }
        }

        if (hasContactLink)
        {
            var linkCols = await GetTableColumnsAsync(source, contactLinkTable!);
            contactLinkTaskIdCol = FindColumn(linkCols, "TaskID", "TaskId", "Task_ID");
            contactLinkContactIdCol = FindColumn(linkCols, "ContactID", "ContactId", "Contact_ID");
            if (contactLinkTaskIdCol == null || contactLinkContactIdCol == null)
            {
                _logger.LogWarning("Contact link table '{Table}' missing expected columns - disabling contact link", contactLinkTable);
                hasContactLink = false;
            }
        }

        var hasEmployees = SourceTableExists("Employees");
        var hasProjects = SourceTableExists("Projects");
        var hasContacts = SourceTableExists("Contacts");

        // Build dynamic SELECT clause
        var selectClauses = new List<string> { "t.ID" };

        // ProjectID / ProjectName (via OUTER APPLY for flat projection)
        selectClauses.Add("proj.ProjectID");
        selectClauses.Add("proj.ProjectName");

        // ContactID / ContactName (via OUTER APPLY for flat projection)
        selectClauses.Add("cont.ContactID");
        selectClauses.Add("cont.ContactName");

        // EmployeeID / EmployeeName
        if (hasEmployees && organizerIdColumn != null)
        {
            selectClauses.Add($"t.{organizerIdColumn} AS EmployeeID");
            selectClauses.Add("CONCAT(e.FirstName, ' ', e.LastName) AS EmployeeName");
        }
        else
        {
            selectClauses.Add(organizerIdColumn != null ? $"t.{organizerIdColumn} AS EmployeeID" : "CAST(NULL AS INT) AS EmployeeID");
            selectClauses.Add("CAST(NULL AS NVARCHAR(500)) AS EmployeeName");
        }

        // Dates
        selectClauses.Add(createdDateColumn != null ? $"t.{createdDateColumn} AS CreatedDate" : "CAST(NULL AS DATETIME2) AS CreatedDate");
        selectClauses.Add(dueDateColumn != null ? $"t.{dueDateColumn} AS DueDate" : "CAST(NULL AS DATETIME2) AS DueDate");

        // Subject / Notes
        selectClauses.Add(subjectColumn != null ? $"t.{subjectColumn} AS Subject" : "CAST(NULL AS NVARCHAR(500)) AS Subject");
        selectClauses.Add(notesColumn != null ? $"t.{notesColumn} AS Notes" : "CAST(NULL AS NVARCHAR(MAX)) AS Notes");

        // Build OUTER APPLY for project (one row per conversation)
        string projectApply;
        if (hasProjectLink && hasProjects)
        {
            projectApply = $@"
            OUTER APPLY (
                SELECT TOP 1 
                    tp.{projectLinkProjectIdCol} AS ProjectID,
                    p.Name AS ProjectName
                FROM {projectLinkTable} tp WITH (NOLOCK)
                LEFT JOIN Projects p WITH (NOLOCK) ON p.ID = tp.{projectLinkProjectIdCol}
                WHERE tp.{projectLinkTaskIdCol} = t.ID
                ORDER BY tp.{projectLinkProjectIdCol}
            ) proj";
        }
        else
        {
            // No project link - return NULLs
            projectApply = @"
            OUTER APPLY (
                SELECT 
                    CAST(NULL AS INT) AS ProjectID,
                    CAST(NULL AS NVARCHAR(500)) AS ProjectName
            ) proj";
        }

        // Build OUTER APPLY for contact (one row per conversation)
        string contactApply;
        if (hasContactLink && hasContacts)
        {
            contactApply = $@"
            OUTER APPLY (
                SELECT TOP 1 
                    tc.{contactLinkContactIdCol} AS ContactID,
                    CONCAT(c.FirstName, ' ', c.LastName) AS ContactName
                FROM {contactLinkTable} tc WITH (NOLOCK)
                LEFT JOIN Contacts c WITH (NOLOCK) ON c.ID = tc.{contactLinkContactIdCol}
                WHERE tc.{contactLinkTaskIdCol} = t.ID
                ORDER BY tc.{contactLinkContactIdCol}
            ) cont";
        }
        else
        {
            // No contact link - return NULLs
            contactApply = @"
            OUTER APPLY (
                SELECT 
                    CAST(NULL AS INT) AS ContactID,
                    CAST(NULL AS NVARCHAR(500)) AS ContactName
            ) cont";
        }

        // Build Employee JOIN
        var employeeJoin = hasEmployees && organizerIdColumn != null
            ? $"LEFT JOIN Employees e WITH (NOLOCK) ON e.ID = t.{organizerIdColumn}"
            : "";

        // Build WHERE clause
        var whereClause = isConversationLogColumn != null
            ? $"t.ID IS NOT NULL AND t.{isConversationLogColumn} = 1"
            : "t.ID IS NOT NULL"; // If no IsConversationLog column, include all (may need adjustment)

        var sql = $@"
            SELECT 
                {string.Join(",\n                ", selectClauses)}
            FROM Tasks t WITH (NOLOCK)
            {projectApply}
            {contactApply}
            {employeeJoin}
            WHERE {whereClause}";

        _logger.LogDebug("Conversations ETL SQL: {Sql}", sql);

        IEnumerable<dynamic> data;
        try
        {
            data = await source.QueryAsync<dynamic>(sql);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Conversations ETL query failed - skipping entity");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"        [ERROR] Query failed: {ex.Message}");
            Console.ResetColor();
            return 0;
        }

        var list = data.ToList();
        if (list.Count == 0) return 0;

        // SAFETY CHECK: Detect duplicate Conversation IDs before inserting
        var duplicates = list
            .GroupBy(x => (int)x.ID)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Any())
        {
            var errorMsg = $"ETL ABORTED: Duplicate Conversation IDs detected: {string.Join(", ", duplicates.Take(10))}" +
                          (duplicates.Count > 10 ? $" (and {duplicates.Count - 10} more)" : "");
            _logger.LogError(errorMsg);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"        [ERROR] {errorMsg}");
            Console.ResetColor();
            throw new InvalidOperationException(errorMsg);
        }

        foreach (var item in list)
        {
            await replica.ExecuteAsync(@"
                INSERT INTO MP_Conversations (ID, ProjectID, ProjectName, ContactID, ContactName, EmployeeID, EmployeeName, CreatedDate, DueDate, Subject, Notes)
                VALUES (@ID, @ProjectID, @ProjectName, @ContactID, @ContactName, @EmployeeID, @EmployeeName, @CreatedDate, @DueDate, @Subject, @Notes)",
                (object)item);
        }

        Console.WriteLine($"        Conversations: {list.Count} records");
        return list.Count;
    }

    private async Task<int> EtlProjectHoursAsync(SqlConnection source, SqlConnection replica)
    {
        _logger.LogInformation("ETL: ProjectHours");
        Console.WriteLine("    → Loading ProjectHours...");

        // Check if required base table exists
        if (!SourceTableExists("HoursReports"))
        {
            _logger.LogWarning("Skipping ProjectHours ETL - HoursReports table not found in source database");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("        [SKIP] HoursReports table not found - skipping ProjectHours ETL");
            Console.ResetColor();
            return 0;
        }

        // Get actual columns from HoursReports table
        var hoursColumns = await GetTableColumnsAsync(source, "HoursReports");
        _logger.LogDebug("HoursReports table columns: {Columns}", string.Join(", ", hoursColumns));

        // Validate required columns exist
        if (!hoursColumns.Contains("ID"))
        {
            _logger.LogWarning("Skipping ProjectHours ETL - Required column 'ID' missing in HoursReports table");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("        [SKIP] Required column 'ID' missing - skipping ProjectHours ETL");
            Console.ResetColor();
            return 0;
        }

        // Find column names dynamically
        var projectIdColumn = FindColumn(hoursColumns, "ProjectID", "ProjectId", "Project_ID");
        var employeeIdColumn = FindColumn(hoursColumns, "EmployeeID", "EmployeeId", "Employee_ID");
        var dateTimeColumn = FindColumn(hoursColumns, "DateTime", "Date", "ReportDate", "WorkDate");
        var descriptionColumn = FindColumn(hoursColumns, "Description", "Notes", "Comment");
        var hoursColumn = FindColumn(hoursColumns, "Hours", "TotalHours", "WorkHours", "Duration");
        var stepIdColumn = FindColumn(hoursColumns, "StepID", "StepId", "Step_ID", "ProjectStepID");

        // Check for SubContractSteps table (primary source for StepName via SubContractStepID)
        var subContractStepIdColumn = FindColumn(hoursColumns, "SubContractStepID", "SubContractStepId", "SubContract_StepID");
        var hasSubContractSteps = SourceTableExists("SubContractSteps");

        string? subContractStepNameColumn = null;
        if (hasSubContractSteps)
        {
            var subContractStepsColumns = await GetTableColumnsAsync(source, "SubContractSteps");
            subContractStepNameColumn = FindColumn(subContractStepsColumns, "Name", "StepName", "Title");

            if (subContractStepNameColumn == null)
            {
                _logger.LogWarning("SubContractSteps table missing Name column - StepName will be NULL");
                hasSubContractSteps = false;
            }
            else
            {
                _logger.LogInformation("Using SubContractSteps.{Column} for StepName lookup via SubContractStepID", subContractStepNameColumn);
            }
        }

        if (!hasSubContractSteps || subContractStepIdColumn == null)
        {
            _logger.LogWarning("SubContractSteps lookup not available (table exists: {TableExists}, column exists: {ColumnExists}) - StepName will be NULL",
                hasSubContractSteps, subContractStepIdColumn != null);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("        [WARNING] SubContractSteps lookup not available - StepName will be NULL");
            Console.ResetColor();
        }

        // Check for StartTime/EndTime columns
        var startTimeColumn = FindColumn(hoursColumns, "StartTime", "Start_Time", "TimeStart");
        var endTimeColumn = FindColumn(hoursColumns, "EndTime", "End_Time", "TimeEnd");

        var hasProjects = SourceTableExists("Projects");
        var hasEmployees = SourceTableExists("Employees");

        // Get Projects columns to find ProjectNum
        string? projectNumColumn = null;
        if (hasProjects)
        {
            var projectsColumns = await GetTableColumnsAsync(source, "Projects");
            projectNumColumn = FindColumn(projectsColumns, "ProjectNum", "ProjectNumber", "Number", "Code");
        }

        // Build dynamic SELECT clause
        var selectClauses = new List<string> { "hr.ID" };

        // ProjectID
        selectClauses.Add(projectIdColumn != null ? $"hr.{projectIdColumn} AS ProjectID" : "CAST(NULL AS INT) AS ProjectID");

        // ProjectName / ProjectNumber
        if (hasProjects && projectIdColumn != null)
        {
            selectClauses.Add("p.Name AS ProjectName");
            selectClauses.Add(projectNumColumn != null ? $"p.{projectNumColumn} AS ProjectNumber" : "CAST(NULL AS NVARCHAR(100)) AS ProjectNumber");
        }
        else
        {
            selectClauses.Add("CAST(NULL AS NVARCHAR(500)) AS ProjectName");
            selectClauses.Add("CAST(NULL AS NVARCHAR(100)) AS ProjectNumber");
        }

        // EmployeeID / EmployeeName
        selectClauses.Add(employeeIdColumn != null ? $"hr.{employeeIdColumn} AS EmployeeID" : "CAST(NULL AS INT) AS EmployeeID");
        if (hasEmployees && employeeIdColumn != null)
            selectClauses.Add("CONCAT(e.FirstName, ' ', e.LastName) AS EmployeeName");
        else
            selectClauses.Add("CAST(NULL AS NVARCHAR(500)) AS EmployeeName");

        // ReportDate (store as DATE only)
        selectClauses.Add(dateTimeColumn != null ? $"CAST(hr.{dateTimeColumn} AS DATE) AS ReportDate" : "CAST(NULL AS DATE) AS ReportDate");

        // StepName (via SubContractSteps lookup using SubContractStepID)
        selectClauses.Add("step.StepName");

        // Description
        selectClauses.Add(descriptionColumn != null ? $"hr.{descriptionColumn} AS Description" : "CAST(NULL AS NVARCHAR(MAX)) AS Description");

        // StartTime / EndTime (cast to TIME(0))
        if (startTimeColumn != null)
            selectClauses.Add($"CAST(hr.{startTimeColumn} AS TIME(0)) AS StartTime");
        else
            selectClauses.Add("CAST(NULL AS TIME(0)) AS StartTime");

        if (endTimeColumn != null)
            selectClauses.Add($"CAST(hr.{endTimeColumn} AS TIME(0)) AS EndTime");
        else
            selectClauses.Add("CAST(NULL AS TIME(0)) AS EndTime");

        // TotalHours: HoursReports.Hours is always milliseconds → minutes = Hours/60000.
        // Cap at 23:59 (TIME(0) cannot store 24:00). Same unit as MillisecondsToDecimalHours.
        if (hoursColumn != null)
        {
            selectClauses.Add($@"CASE
                WHEN hr.{hoursColumn} IS NULL THEN NULL
                WHEN hr.{hoursColumn} < 0 THEN NULL
                WHEN hr.{hoursColumn} / 60000.0 >= 1440 THEN NULL
                ELSE TIMEFROMPARTS(
                    CAST(ROUND(hr.{hoursColumn} / 60000.0, 0) AS INT) / 60,
                    CAST(ROUND(hr.{hoursColumn} / 60000.0, 0) AS INT) % 60,
                    0, 0, 0)
            END AS TotalHours");
        }
        else
            selectClauses.Add("CAST(NULL AS TIME(0)) AS TotalHours");

        // Build OUTER APPLY for step (SubContractSteps via SubContractStepID)
        string stepApply;
        if (hasSubContractSteps && subContractStepIdColumn != null && subContractStepNameColumn != null)
        {
            stepApply = $@"
            OUTER APPLY (
                SELECT TOP 1 
                    scs.{subContractStepNameColumn} AS StepName
                FROM SubContractSteps scs WITH (NOLOCK)
                WHERE scs.ID = hr.{subContractStepIdColumn}
            ) step";
        }
        else
        {
            // No SubContractSteps lookup available - return NULL
            stepApply = @"
            OUTER APPLY (
                SELECT 
                    CAST(NULL AS NVARCHAR(200)) AS StepName
            ) step";
        }

        // Build JOINs
        var joins = new List<string>();
        if (hasProjects && projectIdColumn != null)
            joins.Add($"LEFT JOIN Projects p WITH (NOLOCK) ON p.ID = hr.{projectIdColumn}");
        if (hasEmployees && employeeIdColumn != null)
            joins.Add($"LEFT JOIN Employees e WITH (NOLOCK) ON e.ID = hr.{employeeIdColumn}");

        var sql = $@"
            SELECT 
                {string.Join(",\n                ", selectClauses)}
            FROM HoursReports hr WITH (NOLOCK)
            {stepApply}
            {string.Join("\n            ", joins)}
            WHERE hr.ID IS NOT NULL";

        _logger.LogDebug("ProjectHours ETL SQL: {Sql}", sql);

        IEnumerable<dynamic> data;
        try
        {
            data = await source.QueryAsync<dynamic>(sql);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProjectHours ETL query failed - skipping entity");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"        [ERROR] Query failed: {ex.Message}");
            Console.ResetColor();
            return 0;
        }

        var list = data.ToList();
        if (list.Count == 0) return 0;

        // SAFETY CHECK: Detect duplicate ProjectHours IDs before inserting
        var duplicates = list
            .GroupBy(x => (int)x.ID)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Any())
        {
            var errorMsg = $"ETL ABORTED: Duplicate ProjectHours IDs detected: {string.Join(", ", duplicates.Take(10))}" +
                          (duplicates.Count > 10 ? $" (and {duplicates.Count - 10} more)" : "");
            _logger.LogError(errorMsg);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"        [ERROR] {errorMsg}");
            Console.ResetColor();
            throw new InvalidOperationException(errorMsg);
        }

        foreach (var item in list)
        {
            await replica.ExecuteAsync(@"
                INSERT INTO MP_ProjectHours (ID, ProjectID, ProjectName, ProjectNumber, EmployeeID, EmployeeName,
                    ReportDate, StepName, Description, StartTime, EndTime, TotalHours)
                VALUES (@ID, @ProjectID, @ProjectName, @ProjectNumber, @EmployeeID, @EmployeeName,
                    @ReportDate, @StepName, @Description, @StartTime, @EndTime, @TotalHours)",
                (object)item);
        }

        Console.WriteLine($"        ProjectHours: {list.Count} records");
        return list.Count;
    }

    /// <summary>
    /// ETL: TimeHourReports → MP_TimeHourReports
    /// 
    /// Explicit column mapping from source schema (Db_Mp_SiEng, script.sql):
    ///   Source: dbo.TimeHourReports (line 5417), dbo.Employees (line 2177)
    ///   ─────────────────────────────────────────────────────────────────
    ///   thr.ID                              → ID (PK, INT)
    ///   thr.EmployeeID                      → EmployeeID (INT, NOT NULL in source)
    ///   CONCAT(e.FirstName,' ',e.LastName)   → EmployeeName (NVARCHAR(200))
    ///   thr.[DateTime] (datetime NOT NULL)   → ReportDateTime (DATETIME2)
    ///   CAST(thr.StartTime AS TIME(0))       → StartTime (TIME(0), source is datetime NULL)
    ///   CAST(thr.EndTime AS TIME(0))         → EndTime (TIME(0), source is datetime NULL)
    ///   thr.SumHours (bigint NOT NULL)       → Duration (DECIMAL(10,4), decimal hours)
    ///     SumHours stores .NET TimeSpan ticks (bigint, 10M ticks/sec)
    ///     Conversion: SumHours / 36000000000.0 = decimal hours
    ///     VERIFY: SELECT TOP 5 ID, SumHours, SumHours/36000000000.0 FROM TimeHourReports
    /// 
    /// NOTE: dbo.TimeHourReports is the attendance/time-clock table.
    ///       dbo.HoursReports is the project-hours table (feeds MP_ProjectHoursExtended).
    /// </summary>
    private async Task<int> EtlTimeHourReportsAsync(SqlConnection source, SqlConnection replica)
    {
        _logger.LogInformation("ETL: TimeHourReports (source: dbo.TimeHourReports)");
        Console.WriteLine("    → Loading TimeHourReports...");

        // Explicit SQL — source table: dbo.TimeHourReports (script.sql line 5417)
        // SumHours is bigint NOT NULL DEFAULT 0 — stores .NET TimeSpan ticks (10M ticks/sec)
        // Conversion: SumHours / 36,000,000,000.0 = decimal hours
        // StartTime / EndTime are datetime (time-of-day stored in datetime column)
        // IMPORTANT: This is NOT dbo.HoursReports — that table feeds MP_ProjectHoursExtended
        // RawSumHours included for diagnostics (many rows have SumHours=0 despite valid StartTime/EndTime)
        const string sql = @"
            SELECT 
                thr.ID,
                thr.EmployeeID,
                CONCAT(e.FirstName, ' ', e.LastName) AS EmployeeName,
                thr.[DateTime] AS ReportDateTime,
                CAST(thr.StartTime AS TIME(0)) AS StartTime,
                CAST(thr.EndTime AS TIME(0)) AS EndTime,
                thr.SumHours AS RawSumHours,
                CAST(thr.SumHours / 36000000000.0 AS DECIMAL(10,4)) AS Duration
            FROM TimeHourReports thr WITH (NOLOCK)
            LEFT JOIN Employees e WITH (NOLOCK) ON e.ID = thr.EmployeeID
            WHERE thr.ID IS NOT NULL";

        _logger.LogDebug("TimeHourReports ETL SQL: {Sql}", sql);

        IEnumerable<dynamic> data;
        try
        {
            data = await source.QueryAsync<dynamic>(sql);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "TimeHourReports ETL failed — source schema mismatch? " +
                "Expected: dbo.TimeHourReports(ID, EmployeeID, [DateTime], StartTime, EndTime, SumHours) " +
                $"+ dbo.Employees(ID, FirstName, LastName). Error: {ex.Message}", ex);
        }

        var list = data.ToList();
        if (list.Count == 0) return 0;

        // SAFETY CHECK: Detect duplicate IDs before inserting
        var duplicates = list
            .GroupBy(x => (int)x.ID)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Any())
        {
            var errorMsg = $"ETL ABORTED: Duplicate TimeHourReports IDs detected: {string.Join(", ", duplicates.Take(10))}" +
                          (duplicates.Count > 10 ? $" (and {duplicates.Count - 10} more)" : "");
            _logger.LogError(errorMsg);
            throw new InvalidOperationException(errorMsg);
        }

        var loggedSamples = 0;
        var derivedFromTimeRange = 0;
        var zeroSumHours = 0;

        foreach (var item in list)
        {
            // Duration was computed in SQL from SumHours / 36000000000.0
            // MUST use explicit type — item properties are dynamic (DapperRow)
            decimal? duration = item.Duration is DBNull ? null : (decimal?)item.Duration;

            // Many rows have SumHours=0 (DEFAULT) despite valid StartTime/EndTime
            // Fallback: derive from time range
            if (!duration.HasValue || duration.Value == 0m)
            {
                if (duration.HasValue && duration.Value == 0m) zeroSumHours++;

                TimeSpan? startTs = item.StartTime is DBNull ? null : (TimeSpan?)item.StartTime;
                TimeSpan? endTs = item.EndTime is DBNull ? null : (TimeSpan?)item.EndTime;
                var derived = HoursNormalization.DeriveDecimalHoursFromTimeRange(startTs, endTs);
                if (derived.HasValue)
                {
                    duration = derived;
                    derivedFromTimeRange++;
                }
            }

            // Diagnostic: log first 3 records
            if (loggedSamples < 3)
            {
                var diagId = (int)item.ID;
                string diagRawSum = item.RawSumHours is DBNull ? "(DBNull)" : Convert.ToString(item.RawSumHours) ?? "(null)";
                string diagStart = item.StartTime is DBNull ? "(DBNull)" : Convert.ToString(item.StartTime) ?? "(null)";
                string diagEnd = item.EndTime is DBNull ? "(DBNull)" : Convert.ToString(item.EndTime) ?? "(null)";

                _logger.LogInformation(
                    "[DIAG ETL THR RAW] ID={ID} RawSumHours={RawSumHours} StartTime={StartTime} EndTime={EndTime}",
                    diagId, diagRawSum, diagStart, diagEnd);
                _logger.LogInformation(
                    "[DIAG ETL THR INSERT] ID={ID} Duration={Duration}",
                    diagId, (object?)duration ?? "(null)");
                loggedSamples++;
            }

            var p = new DynamicParameters((object)item);
            p.Add("Duration", duration);

            await replica.ExecuteAsync(@"
                INSERT INTO MP_TimeHourReports (ID, EmployeeID, EmployeeName, ReportDateTime, StartTime, EndTime, Duration)
                VALUES (@ID, @EmployeeID, @EmployeeName, @ReportDateTime, @StartTime, @EndTime, @Duration)", p);
        }

        if (derivedFromTimeRange > 0)
            _logger.LogInformation("ETL TimeHourReports: derived Duration from StartTime/EndTime for {Count} records (SumHours was 0)", derivedFromTimeRange);
        if (zeroSumHours > 0)
            _logger.LogWarning("ETL TimeHourReports: {Count} records had SumHours=0 in source", zeroSumHours);
        Console.WriteLine($"        TimeHourReports: {list.Count} records (derived={derivedFromTimeRange}, zeroSumHours={zeroSumHours})");
        return list.Count;
    }

    /// <summary>
    /// ETL: dbo.HoursReports → MP_ProjectHoursExtended
    /// 
    /// Explicit column mapping from source schema (Db_Mp_SiEng, script.sql):
    ///   Source: dbo.HoursReports (line 2564) | Employees (2177) | Projects (4272)
    ///          | SubContracts (4996) | SubContractSteps (5091) | HoursReportsSteps (2651)
    /// NOTE: dbo.HoursReports is the project-hours table (has ProjectID, SubContractID).
    ///       dbo.TimeHourReports is the attendance table (feeds MP_TimeHourReports instead).
    ///   ─────────────────────────────────────────────────────────────────────────────────
    ///   hr.ID                              → ID (PK, INT)
    ///   hr.EmployeeID                      → EmployeeID (INT)
    ///   CONCAT(e.FirstName,' ',e.LastName)  → EmployeeName (NVARCHAR(200))
    ///   hr.ProjectID                        → ProjectID (INT)
    ///   p.Name                              → ProjectName (NVARCHAR(500))
    ///   p.ProjectNum                        → ProjectNumber (NVARCHAR(50))
    ///   hr.SubContractID                    → SubContractID (INT)
    ///   sc.Name                             → SubContractName (NVARCHAR(500))
    ///   hr.SubContractStepID                → SubContractStepID (INT)
    ///   scs.Name                            → SubContractStepName (NVARCHAR(200))
    ///   hr.[DateTime]                       → ReportDate (DATETIME2)
    ///   hrs.[Description]                   → StepName (NVARCHAR(200), from HoursReportsSteps via HoursReportsStepID)
    ///   hr.HoursReportsStepID               → HoursReportsStepID (INT)
    ///   hr.[Description]                    → Description (NVARCHAR(MAX))
    ///   CAST(hr.StartTime AS TIME(0))       → StartTime (TIME(0), source is datetime)
    ///   CAST(hr.EndTime AS TIME(0))         → EndTime (TIME(0), source is datetime)
    ///   hr.Hours (float, raw MILLISECONDS)  → RawMilliseconds (fetched raw, normalized in C#)
    ///     → Duration via HoursNormalization.MillisecondsToDecimalHours (DECIMAL(10,4), decimal hours)
    ///     → TotalHours via HoursNormalization.DecimalHoursToTimeSpan (TIME(0), derived from Duration)
    ///   LastUpdated                         → NULL after ETL (DEV-021; source has no business LastUpdated)
    ///   NOTE: Hours (float) contains out-of-range values — normalization returns NULL for those
    /// </summary>
    private async Task<int> EtlProjectHoursExtendedAsync(SqlConnection source, SqlConnection replica)
    {
        _logger.LogInformation("ETL: ProjectHoursExtended");
        Console.WriteLine("    → Loading ProjectHoursExtended...");

        // Explicit SQL — all column names verified against source schema (script.sql, Db_Mp_SiEng)
        // JOINs: Employees, Projects, SubContracts, SubContractSteps, HoursReportsSteps
        // Hours (float) stores raw MILLISECONDS — normalization to decimal hours is done in C# via HoursNormalization
        // TotalHours and Duration are NOT computed in SQL — derived in C# from the same function (single source of truth)
        // LastUpdated left NULL (DEV-021) — bak finish is Sync_State only
        // StepName comes from HoursReportsSteps.Description (via HoursReportsStepID), NOT SubContractSteps
        const string sql = @"
            SELECT 
                hr.ID,
                hr.EmployeeID,
                CONCAT(e.FirstName, ' ', e.LastName) AS EmployeeName,
                hr.ProjectID,
                p.Name AS ProjectName,
                p.ProjectNum AS ProjectNumber,
                hr.SubContractID,
                sc.Name AS SubContractName,
                hr.SubContractStepID,
                scs.Name AS SubContractStepName,
                hr.[DateTime] AS ReportDate,
                hrs.[Description] AS StepName,
                hr.HoursReportsStepID,
                hr.[Description] AS [Description],
                CAST(hr.StartTime AS TIME(0)) AS StartTime,
                CAST(hr.EndTime AS TIME(0)) AS EndTime,
                hr.Hours AS RawMilliseconds
            FROM HoursReports hr WITH (NOLOCK)
            LEFT JOIN Employees e WITH (NOLOCK) ON e.ID = hr.EmployeeID
            LEFT JOIN Projects p WITH (NOLOCK) ON p.ID = hr.ProjectID
            LEFT JOIN SubContracts sc WITH (NOLOCK) ON sc.ID = hr.SubContractID
            LEFT JOIN SubContractSteps scs WITH (NOLOCK) ON scs.ID = hr.SubContractStepID
            LEFT JOIN HoursReportsSteps hrs WITH (NOLOCK) ON hrs.ID = hr.HoursReportsStepID
            WHERE hr.ID IS NOT NULL";

        _logger.LogDebug("ProjectHoursExtended ETL SQL: {Sql}", sql);

        IEnumerable<dynamic> data;
        try
        {
            data = await source.QueryAsync<dynamic>(sql);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "ProjectHoursExtended ETL failed — source schema mismatch? " +
                "Expected: HoursReports(ID, EmployeeID, ProjectID, SubContractID, SubContractStepID, " +
                "HoursReportsStepID, [DateTime], StartTime, EndTime, Hours, [Description]) " +
                "+ Employees(ID, FirstName, LastName) + Projects(ID, Name, ProjectNum) " +
                "+ SubContracts(ID, Name) + SubContractSteps(ID, Name) " +
                $"+ HoursReportsSteps(ID, [Description]). Error: {ex.Message}", ex);
        }

        var list = data.ToList();
        if (list.Count == 0) return 0;

        // SAFETY CHECK: Detect duplicate IDs before inserting
        var duplicates = list
            .GroupBy(x => (int)x.ID)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Any())
        {
            var errorMsg = $"ETL ABORTED: Duplicate ProjectHoursExtended IDs detected: {string.Join(", ", duplicates.Take(10))}" +
                          (duplicates.Count > 10 ? $" (and {duplicates.Count - 10} more)" : "");
            _logger.LogError(errorMsg);
            throw new InvalidOperationException(errorMsg);
        }

        var loggedSamples = 0;
        var derivedFromTimeRange = 0;
        var nullDurations = 0;

        foreach (var item in list)
        {
            // Normalize using shared C# function (same logic as API daily sync)
            // MUST use explicit type — item.RawMilliseconds is dynamic, so 'var' would make duration dynamic too,
            // causing RuntimeBinderException on .HasValue when result is null
            decimal? duration = HoursNormalization.MillisecondsToDecimalHours(item.RawMilliseconds);

            // FALLBACK: derive Duration from StartTime/EndTime when hr.Hours is NULL / invalid
            if (!duration.HasValue)
            {
                TimeSpan? startTs = item.StartTime is DBNull ? null : (TimeSpan?)item.StartTime;
                TimeSpan? endTs = item.EndTime is DBNull ? null : (TimeSpan?)item.EndTime;
                var derived = HoursNormalization.DeriveDecimalHoursFromTimeRange(startTs, endTs);
                if (derived.HasValue)
                {
                    duration = derived;
                    derivedFromTimeRange++;
                }
            }

            if (!duration.HasValue) nullDurations++;

            TimeSpan? totalHours = HoursNormalization.DecimalHoursToTimeSpan(duration);

            // ── Diagnostic: log first 3 ETL records to verify normalization ──
            if (loggedSamples < 3)
            {
                var diagId = (int)item.ID;
                string diagRawMs = item.RawMilliseconds is DBNull ? "(DBNull)" : Convert.ToString(item.RawMilliseconds) ?? "(null)";
                string diagStep = item.StepName is DBNull ? "(DBNull)" : (string?)item.StepName ?? "(null)";
                string diagStepId = item.HoursReportsStepID is DBNull ? "(DBNull)" : Convert.ToString(item.HoursReportsStepID) ?? "(null)";
                string diagStart = item.StartTime is DBNull ? "(DBNull)" : Convert.ToString(item.StartTime) ?? "(null)";
                string diagEnd = item.EndTime is DBNull ? "(DBNull)" : Convert.ToString(item.EndTime) ?? "(null)";

                _logger.LogInformation(
                    "[DIAG ETL RAW] ID={ID} RawMilliseconds={RawMilliseconds} StepName={StepName} " +
                    "HoursReportsStepID={StepID} StartTime={StartTime} EndTime={EndTime}",
                    diagId, diagRawMs, diagStep, diagStepId, diagStart, diagEnd);
                _logger.LogInformation(
                    "[DIAG ETL INSERT] ID={ID} Duration={Duration} TotalHours={TotalHours} " +
                    "StepName={StepName} HoursReportsStepID={StepID} LastUpdated=(null)",
                    diagId, (object?)duration ?? "(null)", (object?)totalHours ?? "(null)",
                    diagStep, diagStepId);
                loggedSamples++;
            }

            var p = new DynamicParameters((object)item);
            p.Add("Duration", duration);
            p.Add("TotalHours", totalHours);
            p.Add("LastUpdated", (DateTime?)null);

            await replica.ExecuteAsync(@"
                INSERT INTO MP_ProjectHoursExtended (ID, EmployeeID, EmployeeName, ProjectID, ProjectName,
                    ProjectNumber, SubContractID, SubContractName, SubContractStepID, SubContractStepName,
                    ReportDate, StepName, HoursReportsStepID, Description, StartTime, EndTime,
                    TotalHours, Duration, LastUpdated)
                VALUES (@ID, @EmployeeID, @EmployeeName, @ProjectID, @ProjectName,
                    @ProjectNumber, @SubContractID, @SubContractName, @SubContractStepID, @SubContractStepName,
                    @ReportDate, @StepName, @HoursReportsStepID, @Description, @StartTime, @EndTime,
                    @TotalHours, @Duration, @LastUpdated)", p);
        }

        if (derivedFromTimeRange > 0)
            _logger.LogInformation("ETL ProjectHoursExtended: derived Duration from StartTime/EndTime for {Count} records", derivedFromTimeRange);
        if (nullDurations > 0)
            _logger.LogWarning("ETL ProjectHoursExtended: {Count} records have NULL Duration (no Hours, no StartTime/EndTime)", nullDurations);
        Console.WriteLine($"        ProjectHoursExtended: {list.Count} records (derived={derivedFromTimeRange}, nullDuration={nullDurations})");
        return list.Count;
    }

    private async Task InitializeWatermarksAsync(SqlConnection replica)
    {
        _logger.LogInformation("Initializing watermarks...");
        Console.WriteLine("    → Initializing sync watermarks...");

        // Set watermarks based on MAX values from each table
        // Uses API-compatible column names
        var watermarkQueries = new Dictionary<string, string>
        {
            ["Projects"] = "SELECT MAX(LastUpdated) FROM MP_Projects",
            ["Companies"] = "SELECT MAX(LastUpdated) FROM MP_Companies",
            ["Contacts"] = "SELECT MAX(LastUpdated) FROM MP_Contacts",
            ["Employees"] = "SELECT MAX(LastUpdated) FROM MP_Employees",
            ["Bids"] = "SELECT MAX(LastUpdated) FROM MP_Bids",
            ["Bills"] = "SELECT MAX(LastUpdated) FROM MP_Bills",
            ["Intakes"] = "SELECT MAX(LastUpdated) FROM MP_Intakes",
            ["Tasks"] = "SELECT MAX(LastUpdated) FROM MP_Tasks",
            ["Conversations"] = "SELECT MAX(CreatedDate) FROM MP_Conversations",
            ["ProjectHours"] = "SELECT MAX(ReportDate) FROM MP_ProjectHours",
            ["TimeHourReports"] = "SELECT MAX(ReportDateTime) FROM MP_TimeHourReports",
            ["ProjectHoursExtended"] = "SELECT MAX(ReportDate) FROM MP_ProjectHoursExtended"
        };

        foreach (var (entity, query) in watermarkQueries)
        {
            // Use BackupFinishDate as watermark baseline when available (represents "data valid up to")
            // Falls back to MAX column value if BackupFinishDate was not extracted
            var watermark = _backupFinishDate ?? await replica.ExecuteScalarAsync<DateTime?>(query);
            if (watermark.HasValue)
            {
                await replica.ExecuteAsync(@"
                    INSERT INTO Sync_State (EntityName, LastWatermark, LastSyncTime, UpdatedAt)
                    VALUES (@EntityName, @Watermark, GETUTCDATE(), GETUTCDATE())",
                    new { EntityName = entity, Watermark = watermark.Value });
            }
        }

        _logger.LogInformation("Watermarks initialized");
        Console.WriteLine("        Watermarks initialized for daily sync");
    }

    #endregion
}

