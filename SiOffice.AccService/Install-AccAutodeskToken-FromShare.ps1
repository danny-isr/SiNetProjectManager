# =============================================================================
#  Install-AccAutodeskToken-FromShare.ps1
#  SERVER (elevated): take drop\refresh_token.json and install for SI-ENG\sieng,
#  then restart SiOfficeAccService. Rejects missing/stale drop files.
#  Double-click: Install-AccAutodeskToken-FromShare.cmd
#  IMPORTANT: ASCII only (Windows PowerShell 5.1).
# =============================================================================

param(
    [string]$DropDir = "",
    [string]$ServiceUser = "SI-ENG\sieng",
    [string]$ServiceName = "SiOfficeAccService",
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

$kitRoot = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
if (-not $DropDir) {
    $DropDir = Join-Path $kitRoot "AutodeskTokenDrop"
}

$dropToken = Join-Path $DropDir "refresh_token.json"
$dropMeta = Join-Path $DropDir "export_meta.txt"

Write-Banner "Install Autodesk refresh token for AccService"
Write-Host ("  Drop dir     : {0}" -f $DropDir)
Write-Host ("  Service user : {0}" -f $ServiceUser)
Write-Host ("  Service name : {0}" -f $ServiceName)
Write-Host ("  Force        : {0}" -f $Force)
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

if (-not (Test-Path $dropToken)) {
    Write-Banner "RESULT: FAILED - no new token in drop folder" Red
    Write-Host ("Missing: {0}" -f $dropToken)
    Write-Host ""
    Write-Host "On the workstation, run Export-AccAutodeskToken-ToShare.cmd first." -ForegroundColor Yellow
    Write-Host "That copies %LOCALAPPDATA%\SiNet\Autodesk\refresh_token.json to the share." -ForegroundColor Yellow
    exit 1
}

$dropItem = Get-Item $dropToken
Write-Host ("Drop file LastWriteTime : {0}" -f $dropItem.LastWriteTime)
Write-Host ("Drop file Length        : {0}" -f $dropItem.Length)
if (Test-Path $dropMeta) {
    Write-Host "--- export_meta.txt ---" -ForegroundColor DarkCyan
    Get-Content $dropMeta | ForEach-Object { Write-Host ("  {0}" -f $_) }
    Write-Host "-----------------------" -ForegroundColor DarkCyan
}

$leaf = ($ServiceUser -split '\\')[-1]
$tokenDir = Join-Path $env:SystemDrive ("Users\{0}\AppData\Local\SiNet\Autodesk" -f $leaf)
$targetToken = Join-Path $tokenDir "refresh_token.json"

if ((Test-Path $targetToken) -and -not $Force) {
    $installed = Get-Item $targetToken
    Write-Host ("Installed LastWriteTime : {0}" -f $installed.LastWriteTime)
    if ($dropItem.LastWriteTime -le $installed.LastWriteTime) {
        Write-Banner "RESULT: FAILED - drop file is not newer" Red
        Write-Host "The share file is not newer than the token already installed for sieng."
        Write-Host "Export a FRESH token from the workstation (re-auth Autodesk as ACC Admin),"
        Write-Host "then run Export-AccAutodeskToken-ToShare.cmd again."
        Write-Host "Or re-run this installer with -Force to overwrite anyway."
        exit 3
    }
}

New-Item -ItemType Directory -Path $tokenDir -Force | Out-Null
Copy-Item $dropToken $targetToken -Force

# Ensure sieng can read the file (Administrators copied it).
try {
    $acl = Get-Acl $targetToken
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        $ServiceUser, "ReadAndExecute", "Allow")
    $acl.SetAccessRule($rule)
    Set-Acl -Path $targetToken -AclObject $acl
}
catch {
    Write-Host ("WARNING: could not set ACL on token: {0}" -f $_.Exception.Message) -ForegroundColor Yellow
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

Write-Banner "RESULT: SUCCESS - token installed for sieng" Green
Write-Host "Retry Jumbo -> ACC from the client."
Write-Host "Optional: check AccService log for refreshTokenFileExists=true"
exit 0
