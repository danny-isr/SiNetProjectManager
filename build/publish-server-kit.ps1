# Assemble a self-contained server install kit on the UNC share.
#
# Target:
#   \\SI-WIN-2K19\AppFolder\AppNet\Server\
#
# Contains everything an admin needs on SI-WIN-2K19 without access to D:\repos:
#   Install-OnServer.ps1, SiOfficeAccService.msi, SiNet.SecretImport.exe,
#   README.txt, and (when present) a copy of SiNet.secrets.
#
# Safe to re-run after publish-all / individual channel publishes.

param(
    [string]$DeployDir = "\\SI-WIN-2K19\AppFolder\AppNet\Server",
    [string]$MsiSource = "\\SI-WIN-2K19\AppFolder\AppNet\SiProjecNet2026-Full\SiOfficeAccService.msi",
    [string]$SecretImportSource = "\\SI-WIN-2K19\AppFolder\AppNet\SiNet.SecretImport\SiNet.SecretImport.exe",
    [string]$SecretsSource = "\\SI-WIN-2K19\AppFolder\AppNet\SiNet.secrets",
    [string]$InstallScriptSource = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $InstallScriptSource) {
    $InstallScriptSource = Join-Path $repoRoot "SiOffice.AccService\Install-OnServer.ps1"
}

Write-Host "=== Publishing server install kit ===" -ForegroundColor Cyan
Write-Host "DeployDir: $DeployDir"

if (-not (Test-Path $InstallScriptSource)) {
    throw "Install-OnServer.ps1 not found: $InstallScriptSource"
}
if (-not (Test-Path $MsiSource)) {
    throw "MSI not found: $MsiSource. Run AccService publish first."
}
if (-not (Test-Path $SecretImportSource)) {
    throw "SecretImport exe not found: $SecretImportSource. Run SecretImport publish first."
}

if (-not (Test-Path $DeployDir)) {
    New-Item -ItemType Directory -Path $DeployDir -Force | Out-Null
}

Copy-Item $InstallScriptSource (Join-Path $DeployDir "Install-OnServer.ps1") -Force
Copy-Item $MsiSource (Join-Path $DeployDir "SiOfficeAccService.msi") -Force
Copy-Item $SecretImportSource (Join-Path $DeployDir "SiNet.SecretImport.exe") -Force

# Prefer root SiNet.secrets; fall back to legacy V2 share path.
$secretsCandidates = @(
    $SecretsSource,
    "\\SI-WIN-2K19\AppFolder\AppNet\SiNetProjectManagerV2\SiNet.secrets"
)
$secretsCopied = $false
foreach ($candidate in $secretsCandidates) {
    if (Test-Path $candidate) {
        Copy-Item $candidate (Join-Path $DeployDir "SiNet.secrets") -Force
        Write-Host "Copied secrets from $candidate" -ForegroundColor Green
        $secretsCopied = $true
        break
    }
}
if (-not $secretsCopied) {
    Write-Host "WARNING: SiNet.secrets not found on share. Kit still usable with -SkipImport or -SecretsFile." -ForegroundColor Yellow
}

# CMD wrappers must be ASCII / UTF-8 WITHOUT BOM. A UTF-8 BOM makes cmd.exe
# treat the first line as "???@echo off" and fail.
# They also self-elevate via PowerShell Start-Process -Verb RunAs.
function Write-AsciiCmd([string]$Path, [string[]]$Lines) {
    $ascii = [System.Text.Encoding]::ASCII
    [System.IO.File]::WriteAllLines($Path, $Lines, $ascii)
}

$upgradeCmd = @(
    "@echo off",
    "setlocal",
    "cd /d ""%~dp0""",
    "net session >nul 2>&1",
    "if %errorlevel% neq 0 (",
    "  echo Requesting Administrator elevation...",
    "  powershell.exe -NoProfile -Command ""Start-Process -FilePath '%~f0' -Verb RunAs""",
    "  exit /b",
    ")",
    "echo Running AccService UPGRADE from:",
    "echo   %CD%",
    "powershell.exe -NoProfile -ExecutionPolicy Bypass -File ""%~dp0Install-OnServer.ps1"" Upgrade",
    "set ERR=%ERRORLEVEL%",
    "echo.",
    "echo Exit code: %ERR%",
    "pause",
    "exit /b %ERR%"
)
$fullCmd = @(
    "@echo off",
    "setlocal",
    "cd /d ""%~dp0""",
    "net session >nul 2>&1",
    "if %errorlevel% neq 0 (",
    "  echo Requesting Administrator elevation...",
    "  powershell.exe -NoProfile -Command ""Start-Process -FilePath '%~f0' -Verb RunAs""",
    "  exit /b",
    ")",
    "echo Running AccService FULL install from:",
    "echo   %CD%",
    "powershell.exe -NoProfile -ExecutionPolicy Bypass -File ""%~dp0Install-OnServer.ps1"" Full",
    "set ERR=%ERRORLEVEL%",
    "echo.",
    "echo Exit code: %ERR%",
    "pause",
    "exit /b %ERR%"
)
Write-AsciiCmd (Join-Path $DeployDir "Upgrade-AccService.cmd") $upgradeCmd
Write-AsciiCmd (Join-Path $DeployDir "Install-Full.cmd") $fullCmd

$refreshPs1Source = Join-Path $repoRoot "SiOffice.AccService\Refresh-AccService-Token.ps1"
if (Test-Path $refreshPs1Source) {
    Copy-Item $refreshPs1Source (Join-Path $DeployDir "Refresh-AccService-Token.ps1") -Force
    $refreshCmd = @(
        "@echo off",
        "setlocal",
        "cd /d ""%~dp0""",
        "net session >nul 2>&1",
        "if %errorlevel% neq 0 (",
        "  echo Requesting Administrator elevation...",
        "  powershell.exe -NoProfile -Command ""Start-Process -FilePath '%~f0' -Verb RunAs""",
        "  exit /b",
        ")",
        "echo Refresh AccService Autodesk token from:",
        "echo   %CD%",
        "powershell.exe -NoProfile -ExecutionPolicy Bypass -File ""%~dp0Refresh-AccService-Token.ps1"" %*",
        "set ERR=%ERRORLEVEL%",
        "echo.",
        "echo Exit code: %ERR%",
        "pause",
        "exit /b %ERR%"
    )
    Write-AsciiCmd (Join-Path $DeployDir "Refresh-AccService-Token.cmd") $refreshCmd
}
else {
    Write-Host "WARNING: Refresh-AccService-Token.ps1 not found; skipping token-refresh wrappers." -ForegroundColor Yellow
}

$authOnceCandidates = @(
    (Join-Path $repoRoot "artifacts\SiOffice.AccService.AuthOnce_Publish\SiOffice.AccService.AuthOnce.exe"),
    (Join-Path $DeployDir "SiOffice.AccService.AuthOnce.exe")
)
$authOnceCopied = $false
foreach ($candidate in $authOnceCandidates) {
    if (Test-Path $candidate) {
        Copy-Item $candidate (Join-Path $DeployDir "SiOffice.AccService.AuthOnce.exe") -Force
        Write-Host "Copied AuthOnce from $candidate" -ForegroundColor Green
        $authOnceCopied = $true
        break
    }
}
if (-not $authOnceCopied) {
    Write-Host "WARNING: SiOffice.AccService.AuthOnce.exe not staged. Run SiOffice.AccService.AuthOnce\publish-tool.ps1" -ForegroundColor Yellow
}

# Workstation export + server install (preferred when server browser is blocked)
$tokenScriptPairs = @(
    @{ Ps1 = "Export-AccAutodeskToken-ToShare.ps1"; Cmd = "Export-AccAutodeskToken-ToShare.cmd"; NeedsAdmin = $false; Echo = "Export Autodesk token to Server drop from:" },
    @{ Ps1 = "Install-AccAutodeskToken-FromShare.ps1"; Cmd = "Install-AccAutodeskToken-FromShare.cmd"; NeedsAdmin = $true; Echo = "Install Autodesk token from Server drop:" }
)
foreach ($pair in $tokenScriptPairs) {
    $ps1Source = Join-Path $repoRoot ("SiOffice.AccService\{0}" -f $pair.Ps1)
    if (-not (Test-Path $ps1Source)) {
        Write-Host ("WARNING: {0} not found; skipping." -f $pair.Ps1) -ForegroundColor Yellow
        continue
    }
    $ps1Dest = Join-Path $DeployDir $pair.Ps1
    $ps1Text = [System.IO.File]::ReadAllText($ps1Source)
    $ps1Ascii = -join ($ps1Text.ToCharArray() | ForEach-Object { if ([int]$_ -lt 128) { $_ } else { '-' } })
    [System.IO.File]::WriteAllText($ps1Dest, $ps1Ascii, [System.Text.Encoding]::ASCII)

    $cmdLines = @(
        "@echo off",
        "setlocal",
        "cd /d ""%~dp0"""
    )
    if ($pair.NeedsAdmin) {
        $cmdLines += @(
            "net session >nul 2>&1",
            "if %errorlevel% neq 0 (",
            "  echo Requesting Administrator elevation...",
            "  powershell.exe -NoProfile -Command ""Start-Process -FilePath '%~f0' -Verb RunAs""",
            "  exit /b",
            ")"
        )
    }
    $cmdLines += @(
        ("echo {0}" -f $pair.Echo),
        "echo   %CD%",
        ("powershell.exe -NoProfile -ExecutionPolicy Bypass -File ""%~dp0{0}"" %*" -f $pair.Ps1),
        "set ERR=%ERRORLEVEL%",
        "echo.",
        "echo Exit code: %ERR%",
        "pause",
        "exit /b %ERR%"
    )
    Write-AsciiCmd (Join-Path $DeployDir $pair.Cmd) $cmdLines
}

$dropDir = Join-Path $DeployDir "AutodeskTokenDrop"
if (-not (Test-Path $dropDir)) {
    New-Item -ItemType Directory -Path $dropDir -Force | Out-Null
}

$readmeLines = @(
    "SiNet - Server install kit",
    "==========================",
    "",
    "Location: $DeployDir",
    "",
    "This folder is self-contained. Run on SI-WIN-2K19 as Administrator.",
    "No D:\repos access is required.",
    "",
    "RECOMMENDED:",
    "  Double-click Upgrade-AccService.cmd  (or run it elevated)",
    "",
    "Full install (secrets + service):",
    "  Install-Full.cmd",
    "",
    "Autodesk 3-legged token for AccService (preferred - no server browser):",
    "  1) On workstation: Export-AccAutodeskToken-ToShare.cmd",
    "  2) On SI-WIN-2K19:   Install-AccAutodeskToken-FromShare.cmd",
    "  Drop folder: AutodeskTokenDrop\",
    "",
    "Optional AuthOnce on server (often blocked by server UI policy):",
    "  Refresh-AccService-Token.cmd",
    "",
    "Or from elevated PowerShell (positional Mode - no switches):",
    "  powershell -NoProfile -ExecutionPolicy Bypass -File D:\SharedFolder\AppFolder\AppNet\Server\Install-OnServer.ps1 Upgrade",
    "  powershell -NoProfile -ExecutionPolicy Bypass -File D:\SharedFolder\AppFolder\AppNet\Server\Install-OnServer.ps1 Full",
    "",
    "Defaults resolve MSI / SecretImport / SiNet.secrets from THIS folder.",
    "",
    "SyncEngine is NOT installed from this kit. It runs from",
    "  \\SI-WIN-2K19\AppFolder\AppNet\MasterPlan.SyncEngine\",
    "via Task Scheduler.",
    "",
    "Desktop clients:",
    "  \\SI-WIN-2K19\AppFolder\AppNet\SiNet.App.Wpf\SiNet.App.Wpf.appinstaller"
)
$utf8Bom = New-Object System.Text.UTF8Encoding $true
[System.IO.File]::WriteAllLines((Join-Path $DeployDir "README.txt"), $readmeLines, $utf8Bom)

Write-Host ""
Write-Host "=== Server kit contents ===" -ForegroundColor Green
Get-ChildItem $DeployDir | Format-Table Name, Length, LastWriteTime -AutoSize
Write-Host "On the server (elevated), preferred:" -ForegroundColor Green
Write-Host "  \\SI-WIN-2K19\AppFolder\AppNet\Server\Upgrade-AccService.cmd" -ForegroundColor Green
