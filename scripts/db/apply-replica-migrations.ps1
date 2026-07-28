<#
.SYNOPSIS
    Applies the MasterPlan.SyncEngine Replica migration scripts and records which ones ran.

.DESCRIPTION
    The Replica database has no migration history: MasterPlan.SyncEngine/Migrations/*.sql had to be
    run by hand, so nothing proved which version a given Replica was on. That is why the audit still
    lists MP_TimeHourReports and MP_ProjectHoursExtended as missing.

    This runner adds a SchemaVersions table to Replica and applies pending scripts in file-name
    order, recording script name, SHA-256 and apply time. The scripts themselves stay authoritative
    and are not modified; they are already idempotent (IF NOT EXISTS guards).

    DRY RUN BY DEFAULT - prints what would be applied and exits.

.PARAMETER Server
    SQL Server instance hosting the Replica database.

.PARAMETER Database
    Replica database name. Defaults to Replica_DB.

.EXAMPLE
    pwsh scripts/db/apply-replica-migrations.ps1 -Server SI-WIN-2K19

.EXAMPLE
    pwsh scripts/db/apply-replica-migrations.ps1 -Server SI-WIN-2K19 -Execute
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Server,
    [string]$Database = 'Replica_DB',
    [string]$MigrationsDirectory,
    [string]$UserId,
    [string]$Password,
    [switch]$Execute
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'SqlHelpers.ps1')

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $MigrationsDirectory) {
    $MigrationsDirectory = Join-Path $repoRoot 'MasterPlan.SyncEngine\Migrations'
}

if (-not (Test-Path -LiteralPath $MigrationsDirectory)) {
    throw "Migrations directory not found: $MigrationsDirectory"
}

$scripts = Get-ChildItem -LiteralPath $MigrationsDirectory -Filter '*.sql' | Sort-Object Name
if (-not $scripts) {
    Write-Host 'No migration scripts found - nothing to do.'
    exit 0
}

$versionTableSql = @'
IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SchemaVersions (
        ScriptName   NVARCHAR(260)  NOT NULL PRIMARY KEY,
        Checksum     CHAR(64)       NOT NULL,
        AppliedAtUtc DATETIME2      NOT NULL CONSTRAINT DF_SchemaVersions_AppliedAtUtc DEFAULT SYSUTCDATETIME(),
        AppliedBy    NVARCHAR(200)  NOT NULL
    );
END
'@

function Get-FileChecksum {
    param([Parameter(Mandatory = $true)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

$connectionString = New-SiNetSqlConnectionString -Server $Server -Database $Database -UserId $UserId -Password $Password

if (-not $Execute) {
    $plan = @($versionTableSql)
    foreach ($script in $scripts) {
        $plan += "-- would apply: $($script.Name) (sha256=$(Get-FileChecksum -Path $script.FullName))"
    }

    Write-SqlPlan -Title "Replica migrations on [$Database] @ $Server" -Statements $plan
    Write-Host 'Status: Not Run (dry run).' -ForegroundColor Yellow
    exit 0
}

Write-Host "[schema] ensuring dbo.SchemaVersions in [$Database]"
Invoke-SiNetSqlNonQuery -ConnectionString $connectionString -Sql $versionTableSql

$appliedTable = Invoke-SiNetSqlQuery -ConnectionString $connectionString -Sql 'SELECT ScriptName, Checksum FROM dbo.SchemaVersions;'
$applied = @{}
foreach ($row in $appliedTable.Rows) {
    $applied[$row.ScriptName] = $row.Checksum
}

$operator = try { "$env:USERDOMAIN\$env:USERNAME" } catch { 'unknown' }

foreach ($script in $scripts) {
    $checksum = Get-FileChecksum -Path $script.FullName

    if ($applied.ContainsKey($script.Name)) {
        if ($applied[$script.Name] -ne $checksum) {
            throw "$($script.Name) was already applied with a different checksum. The migration file changed after it ran; resolve manually."
        }

        Write-Host "[skip] $($script.Name) already applied"
        continue
    }

    Write-Host "[apply] $($script.Name)"
    Invoke-SiNetSqlNonQuery -ConnectionString $connectionString -Sql (Get-Content -LiteralPath $script.FullName -Raw)

    Invoke-SiNetSqlNonQuery `
        -ConnectionString $connectionString `
        -Sql 'INSERT INTO dbo.SchemaVersions (ScriptName, Checksum, AppliedBy) VALUES (@name, @checksum, @operator);' `
        -Parameters @{ '@name' = $script.Name; '@checksum' = $checksum; '@operator' = $operator }
}

$missing = Invoke-SiNetSqlQuery -ConnectionString $connectionString -Sql @"
SELECT required.name
FROM (VALUES (N'MP_TimeHourReports'), (N'MP_ProjectHoursExtended')) AS required(name)
WHERE NOT EXISTS (SELECT 1 FROM sys.tables t WHERE t.name = required.name);
"@

if ($missing.Rows.Count -gt 0) {
    foreach ($row in $missing.Rows) {
        Write-Host "FAIL: expected table $($row.name) is still missing." -ForegroundColor Red
    }
    exit 1
}

Write-Host 'Replica migrations applied and verified.' -ForegroundColor Green
