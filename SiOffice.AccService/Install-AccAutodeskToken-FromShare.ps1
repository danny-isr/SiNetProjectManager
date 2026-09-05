# =============================================================================
#  Install-AccAutodeskToken-FromShare.ps1
#  SERVER (elevated): install drop\refresh_token.json into the Windows service
#  account's dedicated AccService store, then restart SiOfficeAccService.
#  Destination: ...\SiNet\Autodesk\AccService\refresh_token.json
#  Double-click: Install-AccAutodeskToken-FromShare.cmd
#  IMPORTANT: ASCII only (Windows PowerShell 5.1).
# =============================================================================

param(
    [string]$DropDir = "",
    [string]$ServiceUser = "",
    [string]$ServiceName = "SiOfficeAccService",
    [string]$ExpectedAdminEmail = "siad@si-eng.co.il",
    [switch]$Force,
    [switch]$KeepDropFile
)

$ErrorActionPreference = "Stop"

function Write-Banner([string]$Title, [ConsoleColor]$Color = [ConsoleColor]::Cyan) {
    Write-Host ""
    Write-Host "================================================================" -ForegroundColor $Color
    Write-Host ("  {0}" -f $Title) -ForegroundColor $Color
    Write-Host "================================================================" -ForegroundColor $Color
}

function Read-MetaMap([string]$Path) {
    $map = @{}
    if (-not (Test-Path -LiteralPath $Path)) { return $map }
    Get-Content -LiteralPath $Path | ForEach-Object {
        $line = $_.Trim()
        if ($line -and ($line.IndexOf('=') -gt 0)) {
            $k = $line.Substring(0, $line.IndexOf('=')).Trim()
            $v = $line.Substring($line.IndexOf('=') + 1).Trim()
            $map[$k] = $v
        }
    }
    return $map
}

function Resolve-ServiceAccount([string]$ServiceName, [string]$FallbackUser) {
    try {
        $cim = Get-CimInstance -ClassName Win32_Service -Filter ("Name='{0}'" -f $ServiceName.Replace("'", "''")) -ErrorAction Stop
        if ($cim -and -not [string]::IsNullOrWhiteSpace($cim.StartName)) {
            $startName = $cim.StartName.Trim()
            if ($startName -match '^(LocalSystem|NT AUTHORITY\\LocalService|NT AUTHORITY\\NetworkService)$') {
                Write-Host ("WARNING: service StartName={0}; falling back to {1}" -f $startName, $FallbackUser) -ForegroundColor Yellow
                return $FallbackUser
            }
            return $startName
        }
    }
    catch {
        Write-Host ("WARNING: could not read service account ({0}); using fallback {1}" -f $_.Exception.Message, $FallbackUser) -ForegroundColor Yellow
    }
    return $FallbackUser
}

$kitRoot = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
if (-not $DropDir) {
    $DropDir = Join-Path $kitRoot "AutodeskTokenDrop"
}

$dropToken = Join-Path $DropDir "refresh_token.json"
$dropMeta = Join-Path $DropDir "export_meta.txt"
$fallbackUser = if ($ServiceUser) { $ServiceUser } else { "SI-ENG\sieng" }

Write-Banner "Install AccService Admin Autodesk token"
Write-Host ("  Drop dir            : {0}" -f $DropDir)
Write-Host ("  Service name        : {0}" -f $ServiceName)
Write-Host ("  ExpectedAdminEmail  : {0}" -f $ExpectedAdminEmail)
Write-Host ("  Force               : {0}" -f $Force)
Write-Host ""

$isAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "Run elevated (Administrator). Use Install-AccAutodeskToken-FromShare.cmd."
}

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $svc) {
    throw ("Windows service '{0}' not found. Run this on SI-WIN-2K19." -f $ServiceName)
}

$resolvedUser = Resolve-ServiceAccount -ServiceName $ServiceName -FallbackUser $fallbackUser
Write-Host ("  Service account     : {0}" -f $resolvedUser)

if (-not (Test-Path $dropToken)) {
    Write-Banner "RESULT: FAILED - no new token in drop folder" Red
    Write-Host ("Missing: {0}" -f $dropToken)
    Write-Host ""
    Write-Host "On the workstation, run Export-AccAutodeskToken-ToShare.cmd first." -ForegroundColor Yellow
    Write-Host "That exports %LOCALAPPDATA%\SiNet\Autodesk\AccService\refresh_token.json only." -ForegroundColor Yellow
    exit 1
}

if (-not (Test-Path $dropMeta)) {
    Write-Banner "RESULT: FAILED - export_meta.txt missing" Red
    Write-Host "Refuse install without non-secret package metadata (TokenPurpose / ActualAdminEmail)."
    exit 2
}

$meta = Read-MetaMap $dropMeta
Write-Host "--- export_meta.txt ---" -ForegroundColor DarkCyan
Get-Content $dropMeta | ForEach-Object { Write-Host ("  {0}" -f $_) }
Write-Host "-----------------------" -ForegroundColor DarkCyan

if ($meta["TokenPurpose"] -ne "AccServiceAdmin") {
    Write-Banner "RESULT: FAILED - TokenPurpose is not AccServiceAdmin" Red
    exit 7
}
$actual = $meta["ActualAdminEmail"]
$expectedMeta = $meta["ExpectedAdminEmail"]
if ([string]::IsNullOrWhiteSpace($actual)) {
    Write-Banner "RESULT: FAILED - ActualAdminEmail missing from metadata" Red
    exit 7
}
if (-not [string]::Equals($actual.Trim(), $ExpectedAdminEmail.Trim(), [StringComparison]::OrdinalIgnoreCase)) {
    Write-Banner "RESULT: FAILED - package ActualAdminEmail != AccBootstrapAdminEmail" Red
    Write-Host ("Configured/expected : {0}" -f $ExpectedAdminEmail)
    Write-Host ("Package actual      : {0}" -f $actual)
    Write-Host "STOP - will not install or restart AccService with the wrong credential." -ForegroundColor Yellow
    exit 7
}
if ($expectedMeta -and -not [string]::Equals($actual.Trim(), $expectedMeta.Trim(), [StringComparison]::OrdinalIgnoreCase)) {
    Write-Banner "RESULT: FAILED - package ActualAdminEmail != package ExpectedAdminEmail" Red
    exit 7
}

$dropItem = Get-Item $dropToken
Write-Host ("Drop file LastWriteTime : {0}" -f $dropItem.LastWriteTime)
Write-Host ("Drop file Length        : {0}" -f $dropItem.Length)

$leaf = ($resolvedUser -split '\\')[-1]
if ($leaf -match '\$$') {
    # Machine account uncommon for AccService; keep leaf as-is after trim of trailing $
    $leaf = $leaf.TrimEnd('$')
}
$tokenDir = Join-Path $env:SystemDrive ("Users\{0}\AppData\Local\SiNet\Autodesk\AccService" -f $leaf)
$targetToken = Join-Path $tokenDir "refresh_token.json"
$desktopPath = Join-Path $env:SystemDrive ("Users\{0}\AppData\Local\SiNet\Autodesk\refresh_token.json" -f $leaf)

Write-Host ("  Install target       : {0}" -f $targetToken)
Write-Host ("  Desktop path (untouched): {0}" -f $desktopPath)

if ((Test-Path $targetToken) -and -not $Force) {
    $installed = Get-Item $targetToken
    Write-Host ("Installed LastWriteTime : {0}" -f $installed.LastWriteTime)
    if ($dropItem.LastWriteTime -le $installed.LastWriteTime) {
        Write-Banner "RESULT: FAILED - drop file is not newer" Red
        Write-Host "Export a FRESH validated AccService token from the workstation,"
        Write-Host "or re-run this installer with -Force to overwrite anyway."
        exit 3
    }
}

$desktopBefore = $null
if (Test-Path $desktopPath) {
    $desktopBefore = (Get-FileHash -LiteralPath $desktopPath -Algorithm SHA256).Hash
}

New-Item -ItemType Directory -Path $tokenDir -Force | Out-Null
Copy-Item $dropToken $targetToken -Force

# Ensure service account can read the file (Administrators copied it).
try {
    $acl = Get-Acl $targetToken
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        $resolvedUser, "ReadAndExecute", "Allow")
    $acl.SetAccessRule($rule)
    Set-Acl -Path $targetToken -AclObject $acl
}
catch {
    Write-Host ("WARNING: could not set ACL on token: {0}" -f $_.Exception.Message) -ForegroundColor Yellow
}

if ($desktopBefore -and (Test-Path $desktopPath)) {
    $desktopAfter = (Get-FileHash -LiteralPath $desktopPath -Algorithm SHA256).Hash
    if ($desktopBefore -ne $desktopAfter) {
        Write-Banner "RESULT: FAILED - desktop UserContext token was modified (unexpected)" Red
        exit 11
    }
}

$installedNow = Get-Item $targetToken
Write-Host ("Installed to: {0}" -f $installedNow.FullName) -ForegroundColor Green
Write-Host ("Length      : {0}" -f $installedNow.Length)
Write-Host ("LastWrite   : {0}" -f $installedNow.LastWriteTime)

Write-Host ""
Write-Host ("--- Restarting {0} ---" -f $ServiceName) -ForegroundColor Cyan
Restart-Service -Name $ServiceName -Force -ErrorAction Stop
(Get-Service -Name $ServiceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(60))
Get-Service -Name $ServiceName | Format-Table Name, Status, StartType -AutoSize

if (-not $KeepDropFile) {
    $usedDir = Join-Path $DropDir "used"
    New-Item -ItemType Directory -Path $usedDir -Force | Out-Null
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    Move-Item $dropToken (Join-Path $usedDir ("refresh_token.{0}.json" -f $stamp)) -Force
    if (Test-Path $dropMeta) {
        Move-Item $dropMeta (Join-Path $usedDir ("export_meta.{0}.txt" -f $stamp)) -Force
    }
    Write-Host ("Drop file moved to {0} (do not leave live tokens on the share)." -f $usedDir) -ForegroundColor DarkCyan
}

Write-Banner "RESULT: SUCCESS - AccService Admin token installed" Green
Write-Host "Authoritative proof is runtime AccService /v1/acc/admin-identity + System Health."
Write-Host "Require: TokenPurpose=AccServiceAdmin, AccService store path, ActualAdminEmail match."
exit 0
