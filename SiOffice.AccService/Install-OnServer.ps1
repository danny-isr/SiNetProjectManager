# =============================================================================
#  Install-OnServer.ps1 - The ONE script for installing SiOffice on the server.
# =============================================================================
#
#  WHAT IT DOES (in this order, single password prompt):
#    1. Asks ONCE for the Windows password of SI-ENG\sieng.
#    2. Imports the secrets file into sieng's Windows Credential Manager
#       (per-user DPAPI vault) - using SiNet.SecretImport.exe. (Full / SecretsOnly)
#    3. (Re)installs the SiOfficeAccService Windows Service so it runs as
#       SI-ENG\sieng. (Full / Upgrade)
#    4. Verifies everything (vault status + service StartName + service state).
#
#  RUN AS:
#    - Local Administrator on SI-WIN-2K19 (UAC-elevated).
#    - Prefer the CMD wrappers in the Server kit (no switch-parameter bugs):
#        Upgrade-AccService.cmd
#        Install-Full.cmd
#
#  MODE (first positional argument - do NOT use -SkipImport switches):
#    Upgrade      - service MSI only (default) - vault already configured
#    Full         - import secrets + install/upgrade service
#    SecretsOnly  - import secrets only
#
#  EXAMPLES:
#    powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-OnServer.ps1 Upgrade
#    powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-OnServer.ps1 Full
#
#  Kit-relative defaults: MSI / SecretImport / SiNet.secrets next to this script.
# =============================================================================

param(
    [Parameter(Position = 0)]
    [ValidateSet('Upgrade', 'Full', 'SecretsOnly')]
    [string]$Mode = 'Upgrade',

    [string]$SecretsFile = "",

    [string]$SecretsPwd = "",

    [string]$ServiceUser = "SI-ENG\sieng",

    [string]$MsiPath = "",

    [string]$SecretImportExe = ""
)

$ErrorActionPreference = "Stop"

$SkipImport = ($Mode -eq 'Upgrade')
$SkipService = ($Mode -eq 'SecretsOnly')

# Resolve kit-relative defaults (works from share / D:\SharedFolder without repos).
$kitRoot = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
if (-not $MsiPath) {
    $MsiPath = Join-Path $kitRoot "SiOfficeAccService.msi"
}
if (-not $SecretImportExe) {
    $SecretImportExe = Join-Path $kitRoot "SiNet.SecretImport.exe"
}
if (-not $SecretsFile) {
    $localSecrets = Join-Path $kitRoot "SiNet.secrets"
    if (Test-Path $localSecrets) {
        $SecretsFile = $localSecrets
    }
    else {
        $SecretsFile = "\\SI-WIN-2K19\AppFolder\AppNet\SiNet.secrets"
    }
}

Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  SiOffice server install - unified script" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  Mode         : $Mode"
Write-Host "  Service user : $ServiceUser"
Write-Host "  Secrets file : $SecretsFile"
Write-Host "  MSI path     : $MsiPath"
Write-Host ""

$isAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "This script must run from an elevated (Administrator) PowerShell. Right-click PowerShell -> Run as administrator."
}

if (-not $SkipImport -and [string]::IsNullOrWhiteSpace($SecretsFile)) {
    throw "SecretsFile is required for Mode Full/SecretsOnly. Place SiNet.secrets next to this script or pass -SecretsFile."
}
if (-not $SkipImport -and -not (Test-Path $SecretsFile)) {
    throw "Secrets file not found: $SecretsFile"
}
if (-not $SkipImport -and -not (Test-Path $SecretImportExe)) {
    throw "SiNet.SecretImport.exe not found at: $SecretImportExe"
}
if (-not $SkipService -and -not (Test-Path $MsiPath)) {
    throw "MSI not found at: $MsiPath"
}

Write-Host "A Windows credential dialog will pop up for: $ServiceUser" -ForegroundColor Yellow
$cred = Get-Credential -UserName $ServiceUser -Message "Windows password for $ServiceUser (NOT the .secrets package password)"
if (-not $cred) { throw "Cancelled at Windows credential prompt." }

$winPwdSecure = $cred.Password
$winPwdPlain  = [System.Net.NetworkCredential]::new("", $winPwdSecure).Password
$cred = New-Object System.Management.Automation.PSCredential($ServiceUser, $winPwdSecure)

if (-not $SkipImport) {
    if (-not $SecretsPwd) {
        Write-Host "A second dialog will pop up for the .secrets package password." -ForegroundColor Yellow
        $pkgCred = Get-Credential -UserName "package" -Message "Package password for the .secrets file (the password you typed in the WPF Export dialog). Username is ignored."
        if (-not $pkgCred) { throw "Cancelled at package password prompt." }
        $SecretsPwd = [System.Net.NetworkCredential]::new("", $pkgCred.Password).Password
    }
}

# --- Step 1: Import secrets -------------------------------------------------
if (-not $SkipImport) {
    Write-Host ""
    Write-Host "--- Step 1/3: Importing secrets as $ServiceUser ---" -ForegroundColor Cyan

    $stdout = [IO.Path]::GetTempFileName()
    $stderr = [IO.Path]::GetTempFileName()
    try {
        $proc = Start-Process -FilePath $SecretImportExe `
            -ArgumentList @("import", "`"$SecretsFile`"", "`"$SecretsPwd`"") `
            -Credential $cred `
            -WorkingDirectory "C:\" `
            -RedirectStandardOutput $stdout `
            -RedirectStandardError  $stderr `
            -WindowStyle Hidden `
            -Wait -PassThru

        Get-Content $stdout -ErrorAction SilentlyContinue | Write-Host
        $errText = Get-Content $stderr -ErrorAction SilentlyContinue
        if ($errText) { $errText | Write-Host -ForegroundColor Red }

        if ($proc.ExitCode -ne 0) {
            throw "Secret import failed with exit code $($proc.ExitCode). See messages above."
        }
        Write-Host "Secrets imported successfully into $ServiceUser's vault." -ForegroundColor Green
    }
    finally {
        Remove-Item $stdout, $stderr -Force -ErrorAction SilentlyContinue
    }
}
else {
    Write-Host ""
    Write-Host "--- Step 1/3: SKIPPED (Mode=Upgrade) ---" -ForegroundColor DarkYellow
}

# --- Step 2: Install / upgrade Windows service ------------------------------
if (-not $SkipService) {
    Write-Host ""
    Write-Host "--- Step 2/3: Installing SiOfficeAccService as $ServiceUser ---" -ForegroundColor Cyan

    $existing = Get-CimInstance Win32_Service -Filter "Name='SiOfficeAccService'" -ErrorAction SilentlyContinue
    if ($existing) {
        $currentAcct = $existing.StartName
        Write-Host "Existing service found, running as: $currentAcct"
        if ($currentAcct -ne $ServiceUser) {
            Write-Host "Account differs from target ($ServiceUser). Uninstalling first..." -ForegroundColor Yellow
            $uninstallLog = Join-Path $env:TEMP "accservice-uninstall.log"
            $p = Start-Process msiexec.exe `
                -ArgumentList @("/x", "`"$MsiPath`"", "/qn", "/l*v", "`"$uninstallLog`"") `
                -Wait -PassThru -NoNewWindow
            if ($p.ExitCode -ne 0 -and $p.ExitCode -ne 1605) {
                throw "Uninstall failed with exit code $($p.ExitCode). See $uninstallLog"
            }
        }
    }

    $installLog = Join-Path $env:TEMP "accservice-install.log"
    Write-Host "Running msiexec... (log: $installLog)"
    $p = Start-Process msiexec.exe `
        -ArgumentList @(
            "/i", "`"$MsiPath`"",
            "SERVICEACCOUNT=`"$ServiceUser`"",
            "SERVICEPASSWORD=`"$winPwdPlain`"",
            "/qn",
            "/l*v", "`"$installLog`""
        ) `
        -Wait -PassThru -NoNewWindow
    if ($p.ExitCode -ne 0) {
        throw "MSI install failed with exit code $($p.ExitCode). See $installLog"
    }
    Write-Host "Service installed." -ForegroundColor Green
}
else {
    Write-Host ""
    Write-Host "--- Step 2/3: SKIPPED (Mode=SecretsOnly) ---" -ForegroundColor DarkYellow
}

# --- Step 3: Verify ---------------------------------------------------------
Write-Host ""
Write-Host "--- Step 3/3: Verifying ---" -ForegroundColor Cyan

$svc = Get-CimInstance Win32_Service -Filter "Name='SiOfficeAccService'" -ErrorAction SilentlyContinue
if ($svc) {
    Write-Host ""
    Write-Host "Windows service:" -ForegroundColor White
    $svc | Select-Object Name, StartName, State, StartMode | Format-Table -AutoSize | Out-String | Write-Host
    if ($svc.StartName -ne $ServiceUser) {
        Write-Host "WARNING: service is running as '$($svc.StartName)', expected '$ServiceUser'." -ForegroundColor Red
    }
    if ($svc.State -ne "Running") {
        Write-Host "WARNING: service state is '$($svc.State)', expected 'Running'." -ForegroundColor Red
    }
}
else {
    Write-Host "Service SiOfficeAccService is NOT installed." -ForegroundColor Red
}

if (Test-Path $SecretImportExe) {
    Write-Host ""
    Write-Host "Vault status for $ServiceUser :" -ForegroundColor White
    $stdout = [IO.Path]::GetTempFileName()
    try {
        Start-Process -FilePath $SecretImportExe -ArgumentList "status" `
            -Credential $cred -WorkingDirectory "C:\" `
            -RedirectStandardOutput $stdout -WindowStyle Hidden -Wait | Out-Null
        Get-Content $stdout -ErrorAction SilentlyContinue | Write-Host
    }
    finally {
        Remove-Item $stdout -Force -ErrorAction SilentlyContinue
    }
}

$winPwdPlain = $null
$SecretsPwd  = $null
[GC]::Collect()

Write-Host ""
Write-Host "================================================================" -ForegroundColor Green
Write-Host "  Done. Mode=$Mode" -ForegroundColor Green
Write-Host "================================================================" -ForegroundColor Green
