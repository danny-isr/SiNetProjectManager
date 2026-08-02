# Master publish script for the entire SiNetProjectManager solution.
#
# Runs the four independent deployment channels in order:
#   1. SiOffice.AccService    -> WiX MSI    -> \\SI-WIN-2K19\...\SiProjecNet2026-Full\
#   2. MasterPlan.SyncEngine  -> robocopy   -> \\SI-WIN-2K19\...\MasterPlanSync\
#   3. SiNet.App.Wpf          -> MSIX       -> \\SI-WIN-2K19\...\SiNet.App.Wpf\
#   4. SiNet.SecretImport     -> robocopy   -> \\SI-WIN-2K19\...\SiNet.SecretImport\
#
# Channel 3 previously published SiNetProjectManagerV2. That host remains in the
# repo for reference/build only and is no longer distributed (see docs/DESKTOP_CUTOVER.md).
#
# Each component bumps its own <Version> independently. Pass -SkipXxx to omit
# a channel (useful when only one component changed).

param(
    [switch]$SkipService,
    [switch]$SkipConsole,
    [switch]$SkipDesktop,
    [switch]$SkipTool,
    [switch]$NoBump,
    [switch]$SkipDeploy
)

$ErrorActionPreference = "Stop"

$forwardArgs = @{}
if ($NoBump)     { $forwardArgs['NoBump']     = $true }
if ($SkipDeploy) { $forwardArgs['SkipDeploy'] = $true }

function Invoke-Channel {
    param([string]$Title, [string]$Script)
    Write-Host "`n############################################################" -ForegroundColor Magenta
    Write-Host "  $Title"                                                       -ForegroundColor Magenta
    Write-Host "############################################################`n" -ForegroundColor Magenta
    # Child scripts use $ErrorActionPreference=Stop + throw on real failures.
    # We do NOT check $LASTEXITCODE here because it reflects the last native
    # command in the child (e.g. robocopy returns 1-7 for normal success).
    & $Script @forwardArgs
    $global:LASTEXITCODE = 0
}

if (-not $SkipService) {
    Invoke-Channel "1/4  SiOffice.AccService (Windows Service -> MSI)" `
        (Join-Path $PSScriptRoot "SiOffice.AccService\publish-service.ps1")
}
else { Write-Host "`n[SKIPPED] SiOffice.AccService" -ForegroundColor DarkGray }

if (-not $SkipConsole) {
    Invoke-Channel "2/4  MasterPlan.SyncEngine (Console -> network share)" `
        (Join-Path $PSScriptRoot "MasterPlan.SyncEngine\publish-console.ps1")
}
else { Write-Host "`n[SKIPPED] MasterPlan.SyncEngine" -ForegroundColor DarkGray }

if (-not $SkipDesktop) {
    Invoke-Channel "3/4  SiNet.App.Wpf (WPF -> MSIX + .appinstaller)" `
        (Join-Path $PSScriptRoot "src\SiNet.App.Wpf\publish-desktop.ps1")
}
else { Write-Host "`n[SKIPPED] SiNet.App.Wpf" -ForegroundColor DarkGray }

if (-not $SkipTool) {
    Invoke-Channel "4/4  SiNet.SecretImport (portable provisioner -> network share)" `
        (Join-Path $PSScriptRoot "SiNet.SecretImport\publish-tool.ps1")
}
else { Write-Host "`n[SKIPPED] SiNet.SecretImport" -ForegroundColor DarkGray }

Write-Host "`n============================================================" -ForegroundColor Green
Write-Host "  All requested channels published successfully."             -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
