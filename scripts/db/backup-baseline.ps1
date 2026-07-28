<#
.SYNOPSIS
    Takes a verified baseline backup of SiData and Replica.

.DESCRIPTION
    Produces the artifacts that docs/DATABASE_RECOVERY_BASELINE.md calls "Option A - verified
    backup": a COPY_ONLY, CHECKSUM-verified full backup per database, immediately validated with
    RESTORE VERIFYONLY, plus a metadata file recording the EF migration history and the table count
    at the moment of the backup.

    DRY RUN BY DEFAULT. Nothing runs against the server unless -Execute is passed.

.PARAMETER Server
    SQL Server instance, e.g. SI-WIN-2K19 or .\SQLEXPRESS.

.PARAMETER BackupDirectory
    Directory on the SQL Server host where the .bak files are written. Must be writable by the
    SQL Server service account.

.PARAMETER Databases
    Databases to back up. Defaults to SiData and Replica_DB.

.PARAMETER Execute
    Actually run. Without it the script only prints the statements.

.EXAMPLE
    pwsh scripts/db/backup-baseline.ps1 -Server SI-WIN-2K19 -BackupDirectory 'D:\Backups\baseline'

.EXAMPLE
    pwsh scripts/db/backup-baseline.ps1 -Server SI-WIN-2K19 -BackupDirectory 'D:\Backups\baseline' -Execute
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Server,
    [Parameter(Mandatory = $true)][string]$BackupDirectory,
    [string[]]$Databases = @('SiData', 'Replica_DB'),
    [string]$UserId,
    [string]$Password,
    [switch]$Execute
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'SqlHelpers.ps1')

$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
$connectionString = New-SiNetSqlConnectionString -Server $Server -Database 'master' -UserId $UserId -Password $Password

$plan = @()
$targets = @{}

foreach ($database in $Databases) {
    $file = Join-Path $BackupDirectory "$database-baseline-$stamp.bak"
    $targets[$database] = $file
    $plan += "BACKUP DATABASE [$database] TO DISK = N'$file' WITH COPY_ONLY, INIT, CHECKSUM, STATS = 10;"
    $plan += "RESTORE VERIFYONLY FROM DISK = N'$file' WITH CHECKSUM;"
}

if (-not $Execute) {
    Write-SqlPlan -Title "Baseline backup on $Server" -Statements $plan
    Write-Host 'Status: Not Run (dry run).' -ForegroundColor Yellow
    exit 0
}

foreach ($database in $Databases) {
    $file = $targets[$database]

    Write-Host "[backup] $database -> $file"
    Invoke-SiNetSqlNonQuery -ConnectionString $connectionString -Sql @"
BACKUP DATABASE [$database] TO DISK = N'$file'
WITH COPY_ONLY, INIT, CHECKSUM, STATS = 10;
"@

    Write-Host "[verify] RESTORE VERIFYONLY $file"
    Invoke-SiNetSqlNonQuery -ConnectionString $connectionString -Sql @"
RESTORE VERIFYONLY FROM DISK = N'$file' WITH CHECKSUM;
"@

    $dbConnection = New-SiNetSqlConnectionString -Server $Server -Database $database -UserId $UserId -Password $Password

    $tableCount = (Invoke-SiNetSqlQuery -ConnectionString $dbConnection -Sql @"
SELECT COUNT(*) AS TableCount
FROM sys.tables
WHERE is_ms_shipped = 0;
"@).Rows[0].TableCount

    $migrations = Invoke-SiNetSqlQuery -ConnectionString $dbConnection -Sql @"
IF OBJECT_ID(N'__EFMigrationsHistory', N'U') IS NULL
    SELECT CAST(NULL AS NVARCHAR(150)) AS MigrationId WHERE 1 = 0;
ELSE
    SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;
"@

    $metadata = [ordered]@{
        database          = $database
        server            = $Server
        backupFile        = $file
        takenAtUtc        = (Get-Date).ToUniversalTime().ToString('o')
        userTableCount    = [int]$tableCount
        migrationCount    = $migrations.Rows.Count
        latestMigrationId = if ($migrations.Rows.Count -gt 0) { $migrations.Rows[$migrations.Rows.Count - 1].MigrationId } else { $null }
        migrations        = @($migrations.Rows | ForEach-Object { $_.MigrationId })
    }

    $metadataPath = [IO.Path]::ChangeExtension($file, '.metadata.json')
    $metadata | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $metadataPath -Encoding UTF8
    Write-Host "[meta] $metadataPath (tables=$tableCount, migrations=$($migrations.Rows.Count))"
}

Write-Host 'Baseline backup completed. Record the result in docs/manual-tests/DB_RESTORE_REHEARSAL_CHECKLIST.md.' -ForegroundColor Green
