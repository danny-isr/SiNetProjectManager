<#
.SYNOPSIS
    Runs the L4W automated P0 Pilot write smoke (docs/TEST_STRATEGY.md §4W).

.DESCRIPTION
    Thin launcher. The authoritative safety gate lives in code (PilotSmokeEnvironment), not here —
    this script only surfaces the resolved target so a mistake is visible before anything is written,
    and then invokes the right test filter.

    DEV machine only. It creates projects, workflow instances, tasks, Gmail labels and ACC folders.

.PARAMETER Probe
    Read-only. Prints the resolved targets (server, database, operator, mailbox, ACC inbox project)
    and runs the probe test, which writes nothing. Always run this first.

.PARAMETER Configuration
    Build configuration. Defaults to Debug to match the local agent gate.

.EXAMPLE
    $env:SINET_LIVE_SMOKE = "1"
    $env:SINET_PILOT_SMOKE = "1"
    $env:SINET_PILOT_SMOKE_SQL = "Server=.\SQLEXPRESS;Database=SiNetDev;Trusted_Connection=True;TrustServerCertificate=True"
    $env:SINET_PILOT_SMOKE_DB_CONFIRM = "SiNetDev"
    $env:SINET_PILOT_SMOKE_USER_ID = "1"
    pwsh .\build\run-p0-pilot-smoke.ps1 -Probe
#>
[CmdletBinding()]
param(
    [switch] $Probe,
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repoRoot 'src\SiNet.App.Wpf.Tests\SiNet.App.Wpf.Tests.csproj'

function Get-Flag([string] $name) {
    $value = [Environment]::GetEnvironmentVariable($name)
    return ($value -eq '1' -or $value -eq 'true')
}

function Get-Value([string] $name) {
    $value = [Environment]::GetEnvironmentVariable($name)
    if ([string]::IsNullOrWhiteSpace($value)) { return $null }
    return $value.Trim()
}

# The connection string is a secret and is never printed. Only the parsed server/database are shown.
$sqlRaw = Get-Value 'SINET_PILOT_SMOKE_SQL'
$server = $null
$database = $null
if ($sqlRaw) {
    try {
        $builder = New-Object Microsoft.Data.SqlClient.SqlConnectionStringBuilder $sqlRaw
        $server = $builder.DataSource
        $database = $builder.InitialCatalog
    }
    catch {
        # Fall back to a text scan so the summary still shows something useful; the test performs
        # the real validation.
        if ($sqlRaw -match '(?i)(?:Initial\s*Catalog|Database)\s*=\s*([^;]+)') { $database = $Matches[1].Trim() }
        if ($sqlRaw -match '(?i)(?:Data\s*Source|Server)\s*=\s*([^;]+)') { $server = $Matches[1].Trim() }
    }
}

$confirm = Get-Value 'SINET_PILOT_SMOKE_DB_CONFIRM'

Write-Host ''
Write-Host 'P0 Pilot smoke — resolved target' -ForegroundColor Cyan
Write-Host '--------------------------------'
Write-Host ("  Machine                : {0}" -f $env:COMPUTERNAME)
Write-Host ("  Configuration          : {0}" -f $Configuration)
if (-not $server) { $server = '<not set>' }
if (-not $database) { $database = '<not set>' }
if (-not $confirm) { $confirm = '<not set>' }
$operatorUserId = Get-Value 'SINET_PILOT_SMOKE_USER_ID'
if (-not $operatorUserId) { $operatorUserId = '<not set>' }

Write-Host ("  SQL server             : {0}" -f $server)
Write-Host ("  SQL database           : {0}" -f $database)
Write-Host ("  Database confirmation  : {0}" -f $confirm)
Write-Host ("  Operator SIUser.Id     : {0}" -f $operatorUserId)
Write-Host ("  Live tier              : {0}" -f (Get-Flag 'SINET_LIVE_SMOKE'))
Write-Host ("  Write tier             : {0}" -f (Get-Flag 'SINET_PILOT_SMOKE'))
Write-Host ("  Gmail layer            : {0}" -f (Get-Flag 'SINET_PILOT_SMOKE_GMAIL'))
$gmailSubject = Get-Value 'SINET_PILOT_SMOKE_GMAIL_SUBJECT'
if (-not $gmailSubject) { $gmailSubject = '<not set>' }
$gmailAccount = Get-Value 'SINET_PILOT_SMOKE_GMAIL_ACCOUNT'
if (-not $gmailAccount) { $gmailAccount = '<not set>' }
$accInbox = Get-Value 'SINET_PILOT_SMOKE_ACC_INBOX_PROJECT'
if (-not $accInbox) { $accInbox = '<not set>' }
$accPlace = Get-Value 'SINET_PILOT_SMOKE_ACC_PLACE'
if (-not $accPlace) { $accPlace = '<not set>' }

Write-Host ("  Gmail subject token    : {0}" -f $gmailSubject)
Write-Host ("  Gmail expected mailbox : {0}" -f $gmailAccount)
Write-Host ("  ACC layer              : {0}" -f (Get-Flag 'SINET_PILOT_SMOKE_ACC'))
Write-Host ("  ACC inbox project       : {0}" -f $accInbox)
Write-Host ("  ACC place              : {0}" -f $accPlace)
Write-Host ''

if ($database -and $confirm -and ($database -ne $confirm)) {
    Write-Host 'The database confirmation does not match the connection string. The test will skip.' -ForegroundColor Yellow
    Write-Host ''
}

if (-not $Probe) {
    Write-Host 'This run WRITES to the target database, the Gmail mailbox and ACC.' -ForegroundColor Yellow
    Write-Host 'Run with -Probe first if you have not already confirmed the target above.' -ForegroundColor Yellow
    Write-Host ''
}

$filter = if ($Probe) { 'Category=PilotSmokeProbe' } else { 'Category=PilotSmoke' }

Write-Host ("Running: dotnet test --filter {0}" -f $filter) -ForegroundColor Cyan
Write-Host ''

& dotnet test $testProject --configuration $Configuration --filter $filter
$testExit = $LASTEXITCODE

$evidenceDir = Get-Value 'SINET_PILOT_SMOKE_EVIDENCE_DIR'
if (-not $evidenceDir) {
    $evidenceDir = Join-Path $env:LOCALAPPDATA 'SiNet\pilot-smoke'
}

if (Test-Path $evidenceDir) {
    $latest = Get-ChildItem -Path $evidenceDir -Filter 'p0-pilot-smoke-*.md' |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($latest) {
        Write-Host ''
        Write-Host ("Evidence: {0}" -f $latest.FullName) -ForegroundColor Green
    }
}

exit $testExit
