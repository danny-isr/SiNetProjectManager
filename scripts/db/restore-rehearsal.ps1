<#
.SYNOPSIS
    Restores a baseline backup into a throwaway database and validates it.

.DESCRIPTION
    The "restore rehearsal" required by docs/DATABASE_RECOVERY_BASELINE.md. Restores a .bak into a
    temporary database (default name: <Database>_Rehearsal), then reports:

      - user table count vs the EF model (85 mapped tables in SiNetSQLDbContextModelSnapshot)
      - __EFMigrationsHistory presence and latest migration id
      - AUTO_CLOSE / READ_COMMITTED_SNAPSHOT / ALLOW_SNAPSHOT_ISOLATION
      - presence of MP_TimeHourReports and MP_ProjectHoursExtended (Replica only)

    DRY RUN BY DEFAULT. Never restores over an existing database: the script aborts if the target
    name already exists unless -DropExistingTarget is given.

.PARAMETER Server
    SQL Server instance to rehearse on. Use an isolated instance, never production.

.PARAMETER BackupFile
    Path to the .bak file, as seen by the SQL Server host.

.PARAMETER SourceDatabase
    Logical database the backup came from (used for the default target name and for the
    Replica-specific checks).

.EXAMPLE
    pwsh scripts/db/restore-rehearsal.ps1 -Server DEV-SQL -BackupFile 'D:\Backups\SiData-baseline.bak' -SourceDatabase SiData

.EXAMPLE
    pwsh scripts/db/restore-rehearsal.ps1 -Server DEV-SQL -BackupFile 'D:\Backups\SiData-baseline.bak' -SourceDatabase SiData -DataDirectory 'D:\SqlData' -Execute
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Server,
    [Parameter(Mandatory = $true)][string]$BackupFile,
    [Parameter(Mandatory = $true)][string]$SourceDatabase,
    [string]$TargetDatabase,
    [string]$DataDirectory,
    [int]$ExpectedMinimumTableCount = 85,
    [string]$UserId,
    [string]$Password,
    [switch]$DropExistingTarget,
    [switch]$Execute
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'SqlHelpers.ps1')

if (-not $TargetDatabase) {
    $TargetDatabase = "${SourceDatabase}_Rehearsal"
}

$master = New-SiNetSqlConnectionString -Server $Server -Database 'master' -UserId $UserId -Password $Password

if (-not $Execute) {
    Write-SqlPlan -Title "Restore rehearsal of $BackupFile into [$TargetDatabase] on $Server" -Statements @(
        "RESTORE FILELISTONLY FROM DISK = N'$BackupFile';",
        "RESTORE DATABASE [$TargetDatabase] FROM DISK = N'$BackupFile' WITH MOVE <logical> TO '<DataDirectory>\...', REPLACE, STATS = 10;",
        "-- validation: user table count, __EFMigrationsHistory, AUTO_CLOSE, RCSI, MP_* tables"
    )
    Write-Host 'Status: Not Run (dry run).' -ForegroundColor Yellow
    exit 0
}

$existing = Invoke-SiNetSqlQuery -ConnectionString $master -Sql "SELECT name FROM sys.databases WHERE name = @name;" -Parameters @{ '@name' = $TargetDatabase }
if ($existing.Rows.Count -gt 0) {
    if (-not $DropExistingTarget) {
        throw "Target database [$TargetDatabase] already exists on $Server. Pass -DropExistingTarget to replace it, or choose another -TargetDatabase."
    }

    Write-Host "[drop] existing [$TargetDatabase]"
    Invoke-SiNetSqlNonQuery -ConnectionString $master -Sql @"
ALTER DATABASE [$TargetDatabase] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
DROP DATABASE [$TargetDatabase];
"@
}

$fileList = Invoke-SiNetSqlQuery -ConnectionString $master -Sql "RESTORE FILELISTONLY FROM DISK = N'$BackupFile';"

if (-not $DataDirectory) {
    $defaultPath = (Invoke-SiNetSqlQuery -ConnectionString $master -Sql "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS NVARCHAR(4000)) AS p;").Rows[0].p
    if ([string]::IsNullOrWhiteSpace($defaultPath)) {
        throw 'Could not determine the instance default data path. Pass -DataDirectory explicitly.'
    }
    $DataDirectory = $defaultPath
}

$moveClauses = foreach ($row in $fileList.Rows) {
    $extension = if ($row.Type -eq 'L') { 'ldf' } else { 'mdf' }
    $target = Join-Path $DataDirectory "$TargetDatabase`_$($row.LogicalName).$extension"
    "MOVE N'$($row.LogicalName)' TO N'$target'"
}

$restoreSql = "RESTORE DATABASE [$TargetDatabase] FROM DISK = N'$BackupFile' WITH " +
    ($moveClauses -join ', ') + ", REPLACE, STATS = 10;"

Write-Host "[restore] $BackupFile -> [$TargetDatabase]"
Invoke-SiNetSqlNonQuery -ConnectionString $master -Sql $restoreSql

$target = New-SiNetSqlConnectionString -Server $Server -Database $TargetDatabase -UserId $UserId -Password $Password

$tableCount = [int](Invoke-SiNetSqlQuery -ConnectionString $target -Sql "SELECT COUNT(*) AS c FROM sys.tables WHERE is_ms_shipped = 0;").Rows[0].c

$migrations = Invoke-SiNetSqlQuery -ConnectionString $target -Sql @"
IF OBJECT_ID(N'__EFMigrationsHistory', N'U') IS NULL
    SELECT CAST(NULL AS NVARCHAR(150)) AS MigrationId WHERE 1 = 0;
ELSE
    SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;
"@

$settings = (Invoke-SiNetSqlQuery -ConnectionString $master -Sql @"
SELECT is_auto_close_on, is_read_committed_snapshot_on, snapshot_isolation_state
FROM sys.databases WHERE name = @name;
"@ -Parameters @{ '@name' = $TargetDatabase }).Rows[0]

$mpTables = Invoke-SiNetSqlQuery -ConnectionString $target -Sql @"
SELECT name FROM sys.tables
WHERE name IN (N'MP_TimeHourReports', N'MP_ProjectHoursExtended');
"@

$failures = @()
if ($tableCount -lt $ExpectedMinimumTableCount) {
    $failures += "user table count $tableCount is below the expected minimum $ExpectedMinimumTableCount"
}
if ($migrations.Rows.Count -eq 0) {
    $failures += '__EFMigrationsHistory is missing or empty'
}
if ($settings.is_auto_close_on) {
    $failures += 'AUTO_CLOSE is ON'
}
if ($SourceDatabase -like 'Replica*' -and $mpTables.Rows.Count -lt 2) {
    $failures += 'Replica is missing MP_TimeHourReports and/or MP_ProjectHoursExtended'
}

Write-Host ''
Write-Host "=== Rehearsal report for [$TargetDatabase] ===" -ForegroundColor Cyan
Write-Host "user tables              : $tableCount (expected >= $ExpectedMinimumTableCount)"
Write-Host "__EFMigrationsHistory    : $($migrations.Rows.Count) row(s)"
if ($migrations.Rows.Count -gt 0) {
    Write-Host "latest migration         : $($migrations.Rows[$migrations.Rows.Count - 1].MigrationId)"
}
Write-Host "AUTO_CLOSE               : $($settings.is_auto_close_on)"
Write-Host "READ_COMMITTED_SNAPSHOT  : $($settings.is_read_committed_snapshot_on)"
Write-Host "snapshot isolation state : $($settings.snapshot_isolation_state)"
Write-Host "MP hours tables present  : $($mpTables.Rows.Count)/2"

if ($failures.Count -gt 0) {
    Write-Host ''
    foreach ($failure in $failures) {
        Write-Host "FAIL: $failure" -ForegroundColor Red
    }
    exit 1
}

Write-Host ''
Write-Host 'Rehearsal PASSED. Record operator, date and commit in docs/manual-tests/DB_RESTORE_REHEARSAL_CHECKLIST.md.' -ForegroundColor Green
