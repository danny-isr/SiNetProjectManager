# ════════════════════════════════════════════════════════════════════════════
#  Install-OnServer.ps1 - The ONE script for installing SiOffice on the server.
# ════════════════════════════════════════════════════════════════════════════
#
#  WHAT IT DOES (in this order, single password prompt):
#    1. Asks ONCE for the Windows password of SI-ENG\sieng.
#    2. Imports the secrets file into sieng's Windows Credential Manager
#       (per-user DPAPI vault) - using SiNet.SecretImport.exe.
#    3. (Re)installs the SiOfficeAccService Windows Service so it runs as
#       SI-ENG\sieng (so it can read the same vault).
#    4. Verifies everything (vault status + service StartName + service state).
#
#  WHY IT EXISTS:
#    Windows Credential Manager is per-user. The service MUST run as the same
#    Windows account that owns the secrets, otherwise it cannot read them.
#    Doing the two steps separately is what caused confusion before. This
#    script does both, with one password prompt, and is the ONLY supported
#    install path on the server.
#
#  RUN AS:
#    - Local Administrator on SI-WIN-2K19 (UAC-elevated PowerShell).
#    - The interactive Windows session itself does NOT need to be sieng.
#      The script will run the secret-import EXE *as sieng* via
#      Start-Process -Credential, so the secrets land in sieng's vault.
#
#  PARAMETERS:
#    -SecretsFile   Path to the encrypted .secrets file produced by the WPF.
#    -SecretsPwd    Password used during Export (.secrets package password).
#                   Optional: if omitted, you'll be prompted (hidden input).
#    -ServiceUser   Windows account the service should run as.
#                   Default: SI-ENG\sieng
#    -SkipImport    Only (re)install the service, don't import secrets.
#    -SkipService   Only import secrets, don't touch the service.
#
#  EXAMPLES:
#    # Full install on the server (typical):
#    .\Install-OnServer.ps1 `
#        -SecretsFile "\\SI-WIN-2K19\AppFolder\AppNet\SiNetProjectManagerV2\SiNet.secrets"
#
#    # If service already correct, just re-import secrets:
#    .\Install-OnServer.ps1 -SecretsFile <...> -SkipService
#
#  TROUBLESHOOTING:
#    If you get "The term '-SkipService' is not recognized", this usually means
#    PowerShell split your command line incorrectly. Make sure to run as a single
#    line, or use backticks (`) for line continuation. Avoid copy-pasting commands
#    with invisible characters from web pages or rich-text editors.
# ════════════════════════════════════════════════════════════════════════════

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SecretsFile,

    [string]$SecretsPwd,

    [string]$ServiceUser = "SI-ENG\sieng",

    [string]$MsiPath = "\\SI-WIN-2K19\AppFolder\AppNet\SiProjecNet2026-Full\SiOfficeAccService.msi",

    [string]$SecretImportExe = "\\SI-WIN-2K19\AppFolder\AppNet\SiNet.SecretImport\SiNet.SecretImport.exe",

    [switch]$SkipImport,
    [switch]$SkipService
)

$ErrorActionPreference = "Stop"

# ─── Header ────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  SiOffice server install - unified script" -ForegroundColor Cyan
Write-Host "════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Service user : $ServiceUser"
Write-Host "  Secrets file : $SecretsFile"
Write-Host "  MSI path     : $MsiPath"
Write-Host ""

# ─── Pre-flight checks ─────────────────────────────────────────────────────
$isAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "This script must run from an elevated (Administrator) PowerShell. Right-click PowerShell -> Run as administrator."
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

# ─── Single password prompt for SI-ENG\sieng (GUI dialog) ──────────────────
# Same Windows password is used for BOTH:
#   - Start-Process -Credential (so secret import runs as sieng)
#   - msiexec SERVICEPASSWORD=... (so the Windows service starts as sieng)
# Using Get-Credential -> native Windows credential dialog (works reliably
# even when the script is launched from a UNC path / non-interactive host).
Write-Host "A Windows credential dialog will pop up for: $ServiceUser" -ForegroundColor Yellow
$cred = Get-Credential -UserName $ServiceUser -Message "Windows password for $ServiceUser (NOT the .secrets package password)"
if (-not $cred) { throw "Cancelled at Windows credential prompt." }
# Keep service user exactly as requested (Get-Credential may strip the domain
# from UserName if the same account is the current user).
$winPwdSecure = $cred.Password
$winPwdPlain  = [System.Net.NetworkCredential]::new("", $winPwdSecure).Password
$cred = New-Object System.Management.Automation.PSCredential($ServiceUser, $winPwdSecure)

# ─── Prompt for secrets package password (GUI dialog) ──────────────────────
if (-not $SkipImport) {
    if (-not $SecretsPwd) {
        Write-Host "A second dialog will pop up for the .secrets package password." -ForegroundColor Yellow
        $pkgCred = Get-Credential -UserName "package" -Message "Package password for the .secrets file (the password you typed in the WPF Export dialog). Username is ignored."
        if (-not $pkgCred) { throw "Cancelled at package password prompt." }
        $SecretsPwd = [System.Net.NetworkCredential]::new("", $pkgCred.Password).Password
    }
}

# ════════════════════════════════════════════════════════════════════════════
#  STEP 1 - Import secrets into sieng's Credential Manager
# ════════════════════════════════════════════════════════════════════════════
if (-not $SkipImport) {
    Write-Host ""
    Write-Host "─── Step 1/3: Importing secrets as $ServiceUser ───" -ForegroundColor Cyan

    # Run the importer AS sieng so the secrets land in sieng's per-user vault.
    # We capture stdout/stderr to temp files because Start-Process doesn't
    # stream them to the parent console when -Credential is used.
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
    Write-Host "─── Step 1/3: SKIPPED (-SkipImport) ───" -ForegroundColor DarkYellow
}

# ════════════════════════════════════════════════════════════════════════════
#  STEP 2 - (Re)install the Windows service to run as sieng
# ════════════════════════════════════════════════════════════════════════════
if (-not $SkipService) {
    Write-Host ""
    Write-Host "─── Step 2/3: Installing SiOfficeAccService as $ServiceUser ───" -ForegroundColor Cyan

    # If the service already exists with a DIFFERENT account, do an explicit
    # uninstall first. MSI MajorUpgrade does not always change ServiceAccount.
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
    Write-Host "─── Step 2/3: SKIPPED (-SkipService) ───" -ForegroundColor DarkYellow
}

# ════════════════════════════════════════════════════════════════════════════
#  STEP 3 - Verify
# ════════════════════════════════════════════════════════════════════════════
Write-Host ""
Write-Host "─── Step 3/3: Verifying ───" -ForegroundColor Cyan

# Service state
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

# Vault status (also has to run AS sieng to be meaningful)
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

# Clear plaintext password from memory
$winPwdPlain = $null
$SecretsPwd  = $null
[GC]::Collect()

Write-Host ""
Write-Host "════════════════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host "  Done." -ForegroundColor Green
Write-Host "════════════════════════════════════════════════════════════════" -ForegroundColor Green
