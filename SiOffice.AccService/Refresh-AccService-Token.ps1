# =============================================================================
#  Refresh-AccService-Token.ps1
#  Stop AccService, run AuthOnce as SI-ENG\sieng (browser OAuth), start service.
#  Prefer the CMD wrapper: Refresh-AccService-Token.cmd (self-elevates).
#  IMPORTANT: save this file as ASCII (no smart punctuation) for Windows PowerShell 5.1.
# =============================================================================

param(
    [string]$ServiceUser = "SI-ENG\sieng",
    [string]$ServiceName = "SiOfficeAccService",
    [string]$AuthOnceExe = "",
    [string]$LocalStageDir = "C:\AccService\AuthOnce",
    [int]$WaitSeconds = 600,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

function Write-Banner {
    param(
        [string]$Title,
        [ConsoleColor]$Color = [ConsoleColor]::Cyan
    )
    Write-Host ""
    Write-Host "================================================================" -ForegroundColor $Color
    Write-Host ("  {0}" -f $Title) -ForegroundColor $Color
    Write-Host "================================================================" -ForegroundColor $Color
}

function Start-ProcessAsUserVisible {
    param(
        [Parameter(Mandatory = $true)][System.Management.Automation.PSCredential]$Credential,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string]$Arguments = "",
        [string]$WorkingDirectory = "C:\"
    )

    $userName = $Credential.UserName
    $domain = ""
    if ($userName -match '\\') {
        $parts = $userName -split '\\', 2
        $domain = $parts[0]
        $userName = $parts[1]
    }

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $psi.Arguments = $Arguments
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.LoadUserProfile = $true
    $psi.UserName = $userName
    if (-not [string]::IsNullOrWhiteSpace($domain)) {
        $psi.Domain = $domain
    }
    $psi.Password = $Credential.Password
    # Visible console for AuthOnce; browser is launched by TokenProvider.
    $psi.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Normal

    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    if (-not $proc.Start()) {
        throw "Failed to start AuthOnce as the service user."
    }
    return $proc
}

$kitRoot = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
if (-not $AuthOnceExe) {
    $AuthOnceExe = Join-Path $kitRoot "SiOffice.AccService.AuthOnce.exe"
}

Write-Banner "AccService - refresh Autodesk 3-legged token"
Write-Host ("  Service     : {0}" -f $ServiceName)
Write-Host ("  Service user: {0}" -f $ServiceUser)
Write-Host ("  AuthOnce    : {0}" -f $AuthOnceExe)
Write-Host ("  Force       : {0}" -f $Force)
Write-Host ""

$isAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "Run elevated (Administrator). Use Refresh-AccService-Token.cmd (double-click)."
}

if (-not (Test-Path $AuthOnceExe)) {
    throw ("SiOffice.AccService.AuthOnce.exe not found at: {0}. Publish AuthOnce into the Server kit first." -f $AuthOnceExe)
}

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $svc) {
    throw ("Windows service '{0}' was not found on this machine. Run this on SI-WIN-2K19 where AccService is installed." -f $ServiceName)
}

# Dedicated AccService Admin token store (never the desktop UserContext path).
$serviceAccountLeaf = ($ServiceUser -split '\\')[-1]
$tokenDir = Join-Path $env:SystemDrive ("Users\{0}\AppData\Local\SiNet\Autodesk\AccService" -f $serviceAccountLeaf)
$tokenPath = Join-Path $tokenDir "refresh_token.json"
$okMarker = Join-Path $tokenDir "auth_once_last_ok.txt"

if ((Test-Path $tokenPath) -and -not $Force) {
    Write-Host ("Existing token file: {0}" -f $tokenPath) -ForegroundColor Yellow
    Write-Host ("LastWriteTime: {0}" -f (Get-Item $tokenPath).LastWriteTime) -ForegroundColor Yellow
    $ans = Read-Host "Force a brand-new browser login (delete token first)? [y/N]"
    if ($ans -match '^(y|yes)$') {
        $Force = $true
    }
}

Write-Host ""
Write-Host "A Windows credential dialog will open for the service account." -ForegroundColor Yellow
Write-Host ("User is pre-filled: {0}" -f $ServiceUser) -ForegroundColor Yellow
Write-Host "Enter the Windows password for that account (NOT Autodesk)." -ForegroundColor Yellow
Write-Host ""

$cred = Get-Credential -UserName $ServiceUser -Message "Windows password for AccService account (SI-ENG\sieng). This is NOT the Autodesk password."
if (-not $cred) {
    throw "Cancelled at Windows credential dialog."
}

Write-Host "Ensuring HttpListener URL ACL for OAuth callback (localhost:8080)..." -ForegroundColor DarkCyan
& netsh http add urlacl url=http://localhost:8080/ user=Everyone 2>$null | Out-Null

Write-Host ("Staging AuthOnce to {0} ..." -f $LocalStageDir) -ForegroundColor DarkCyan
New-Item -ItemType Directory -Path $LocalStageDir -Force | Out-Null
$localExe = Join-Path $LocalStageDir "SiOffice.AccService.AuthOnce.exe"
Copy-Item $AuthOnceExe $localExe -Force

# Allow the service account to execute the staged binary.
try {
    $acl = Get-Acl $LocalStageDir
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        $ServiceUser, "ReadAndExecute", "ContainerInherit,ObjectInherit", "None", "Allow")
    $acl.SetAccessRule($rule)
    Set-Acl -Path $LocalStageDir -AclObject $acl
}
catch {
    Write-Host ("WARNING: could not set ACL on stage dir: {0}" -f $_.Exception.Message) -ForegroundColor Yellow
}

$startedUtc = [datetime]::UtcNow.AddSeconds(-5)
$authExitCode = -1
$ok = $false

try {
    Write-Host ""
    Write-Host ("--- Stopping {0} ---" -f $ServiceName) -ForegroundColor Cyan
    if ($svc.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force -ErrorAction Stop
        $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
    }
    Write-Host "Service stopped." -ForegroundColor Green

    $authArgs = ""
    if ($Force) { $authArgs = "--force" }

    Write-Host ""
    Write-Host ("--- Launching AuthOnce as {0} ---" -f $ServiceUser) -ForegroundColor Cyan
    Write-Host "Next: a console window opens as sieng, then Autodesk browser login." -ForegroundColor Yellow
    Write-Host "Sign in with an ACC Account Admin user." -ForegroundColor Yellow
    Write-Host "When AuthOnce prints OK, press Enter in THAT window." -ForegroundColor Yellow
    Write-Host ""

    $authProc = Start-ProcessAsUserVisible -Credential $cred -FilePath $localExe -Arguments $authArgs -WorkingDirectory $LocalStageDir
    Write-Host ("AuthOnce started (PID {0}). Waiting for it to finish..." -f $authProc.Id) -ForegroundColor DarkCyan
    if (-not $authProc.WaitForExit($WaitSeconds * 1000)) {
        try { $authProc.Kill() } catch { }
        throw ("AuthOnce timed out after {0} seconds." -f $WaitSeconds)
    }
    $authExitCode = $authProc.ExitCode
    Write-Host ("AuthOnce exit code: {0}" -f $authExitCode) -ForegroundColor DarkCyan

    if ((Test-Path $okMarker) -and (Test-Path $tokenPath)) {
        $markerUtc = (Get-Item $okMarker).LastWriteTimeUtc
        if ($markerUtc -ge $startedUtc -and $authExitCode -eq 0) {
            $ok = $true
        }
    }
}
finally {
    Write-Host ""
    Write-Host ("--- Starting {0} ---" -f $ServiceName) -ForegroundColor Cyan
    try {
        Start-Service -Name $ServiceName -ErrorAction Stop
        (Get-Service -Name $ServiceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(60))
        Get-Service -Name $ServiceName | Format-Table Name, Status, StartType -AutoSize
    }
    catch {
        Write-Host ("ERROR starting service: {0}" -f $_.Exception.Message) -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "------------------------------ RESULT ------------------------------" -ForegroundColor White
Write-Host ("  AuthOnce exit code : {0}" -f $authExitCode)
Write-Host ("  Token file exists  : {0}" -f (Test-Path $tokenPath))
if (Test-Path $tokenPath) {
    $ti = Get-Item $tokenPath
    Write-Host ("  Token path         : {0}" -f $ti.FullName)
    Write-Host ("  Token last write   : {0}" -f $ti.LastWriteTime)
}
Write-Host ("  Success marker OK  : {0}" -f $ok)
$svcNow = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svcNow) {
    Write-Host ("  Service status     : {0}" -f $svcNow.Status)
}

if ($ok -and $svcNow -and $svcNow.Status -eq 'Running') {
    Write-Banner "RESULT: SUCCESS - token refreshed" Green
    Write-Host "Retry Jumbo -> ACC from the client." -ForegroundColor Green
    exit 0
}

Write-Banner "RESULT: FAILED - token not confirmed" Red
Write-Host "Possible causes:" -ForegroundColor Yellow
Write-Host "  - Wrong Windows password for sieng in the credential dialog"
Write-Host "  - Autodesk browser login was cancelled or timed out"
Write-Host "  - Autodesk ClientId/Secret missing in sieng Credential Manager"
Write-Host "  - AuthOnce window did not appear (session/UI issue)"
Write-Host ""
Write-Host "See docs/OPS_ACCSERVICE_TOKEN_REFRESH.md" -ForegroundColor Yellow
exit 1
