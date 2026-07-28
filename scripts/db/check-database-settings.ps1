<#
.SYNOPSIS
    Reports AUTO_CLOSE, RCSI and snapshot isolation for the SiNet databases.

.DESCRIPTION
    Read-only. Produces the evidence needed before deciding on the P2 items "AUTO_CLOSE OFF on the
    permanent server" and "blocking measurement before enabling RCSI". It prints the ALTER
    statements that would fix AUTO_CLOSE but never runs them - changing database options on a live
    server is an operator decision.

.EXAMPLE
    pwsh scripts/db/check-database-settings.ps1 -Server SI-WIN-2K19
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Server,
    [string[]]$Databases = @('SiData', 'Replica_DB', 'Db_Mp_SiEng'),
    [string]$UserId,
    [string]$Password
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'SqlHelpers.ps1')

$connectionString = New-SiNetSqlConnectionString -Server $Server -Database 'master' -UserId $UserId -Password $Password

$rows = Invoke-SiNetSqlQuery -ConnectionString $connectionString -Sql @"
SELECT name,
       is_auto_close_on,
       is_read_committed_snapshot_on,
       snapshot_isolation_state_desc,
       recovery_model_desc,
       state_desc
FROM sys.databases
WHERE name IN (SELECT value FROM STRING_SPLIT(@names, ','));
"@ -Parameters @{ '@names' = ($Databases -join ',') }

if ($rows.Rows.Count -eq 0) {
    Write-Host "None of the requested databases exist on $Server." -ForegroundColor Yellow
    exit 0
}

$rows | Format-Table -AutoSize | Out-String | Write-Host

$autoCloseOn = @($rows.Rows | Where-Object { $_.is_auto_close_on })
if ($autoCloseOn.Count -gt 0) {
    Write-Host 'AUTO_CLOSE is ON for:' -ForegroundColor Yellow
    foreach ($row in $autoCloseOn) {
        Write-Host "  ALTER DATABASE [$($row.name)] SET AUTO_CLOSE OFF;"
    }
    Write-Host 'Run these manually during a maintenance window - this script does not change server state.'
}
else {
    Write-Host 'AUTO_CLOSE is OFF everywhere.' -ForegroundColor Green
}

Write-Host ''
Write-Host 'RCSI decision note: do not enable READ_COMMITTED_SNAPSHOT before measuring blocking.' -ForegroundColor Cyan
Write-Host 'Collect a baseline first, e.g. sys.dm_os_wait_stats (LCK_*) and sys.dm_exec_requests blocking_session_id.'
