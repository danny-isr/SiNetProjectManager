# Publish SiNet.SecretImport (portable Credential Manager provisioner) to the
# network share so any administrator can grab the EXE from the server, log in
# as the target service account, and import a SiNet.secrets file.
#
# Pipeline (mirrors publish-console.ps1 conventions):
#   1. Auto-bump <Version> in csproj (patch component) unless -NoBump.
#   2. dotnet publish: self-contained, single-file, win-x64 -> intermediate folder.
#   3. robocopy /MIR -> network share. One ~80 MB exe; no installer needed.
#
# The tool is intentionally tiny (no WPF, no DB, no Google) and self-contained,
# so it runs anywhere - including a fresh Windows Server 2019 with no .NET runtime.

param(
    # Intermediate publish folder (local scratch).
    [string]$OutputDir = (Join-Path $PSScriptRoot "..\artifacts\SiNet.SecretImport_Publish"),
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    # Network share where admins fetch the EXE.
    [string]$DeployDir = "\\SI-WIN-2K19\AppFolder\AppNet\SiNet.SecretImport",
    [switch]$SkipDeploy,
    [switch]$NoBump
)

$ErrorActionPreference = "Stop"
$projectPath = Join-Path $PSScriptRoot "SiNet.SecretImport.csproj"

# ---------------------------------------------------------------
# Auto-bump <Version>.
# Edit the raw UTF-8 text: Get-Content without -Encoding decodes non-ASCII
# csproj content as ANSI and XmlDocument.Save then rewrites the whole file,
# which corrupts any Hebrew metadata and reformats every line.
# ---------------------------------------------------------------
if (-not $NoBump) {
    Write-Host "=== Bumping <Version> in csproj ===" -ForegroundColor Cyan
    $csprojUtf8 = New-Object System.Text.UTF8Encoding $false
    $projectText = [System.IO.File]::ReadAllText($projectPath, $csprojUtf8)
    $versionMatch = [regex]::Match($projectText, '<Version>[^<]+</Version>')
    if ($versionMatch.Success) {
        $current = [version]($versionMatch.Value -replace '</?Version>', '')
        $bumped  = [version]::new($current.Major, $current.Minor, $current.Build + 1)
        $projectText = $projectText.Remove($versionMatch.Index, $versionMatch.Length).Insert(
            $versionMatch.Index, "<Version>$($bumped.ToString())</Version>")
        Write-Host "Version bumped: $current -> $bumped" -ForegroundColor Yellow
    }
    else {
        $firstPgEnd = $projectText.IndexOf('</PropertyGroup>')
        if ($firstPgEnd -lt 0) { throw "No <PropertyGroup> found in $projectPath -- cannot seed <Version>." }
        $projectText = $projectText.Insert($firstPgEnd, "  <Version>1.0.0</Version>$([Environment]::NewLine)  ")
        Write-Host "Seeded <Version>1.0.0</Version> in csproj." -ForegroundColor Yellow
    }
    [System.IO.File]::WriteAllText($projectPath, $projectText, $csprojUtf8)
}

Write-Host "`n=== Cleaning output ===" -ForegroundColor Cyan
if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }

Write-Host "`n=== dotnet publish (self-contained, single-file) ===" -ForegroundColor Cyan
Push-Location $PSScriptRoot
try {
    dotnet publish $projectPath `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -o $OutputDir `
        /p:PublishSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:DebugType=embedded
    if ($LASTEXITCODE -ne 0) { throw "Publish failed" }
}
finally { Pop-Location }

Write-Host "`n=== Publish output: $OutputDir ===" -ForegroundColor Green
Get-ChildItem $OutputDir -Filter "SiNet.SecretImport.exe" | Format-Table Name, Length, LastWriteTime

# Stage a small README next to the EXE so admins remember the workflow.
$readme = @"
SiNet.SecretImport - portable Credential Manager provisioner
=============================================================

Run this on the SERVER to import a SiNet.secrets package into Windows
Credential Manager of the account that hosts the AccService Windows
Service and/or the MasterPlan.SyncEngine scheduled tasks.

On SI-WIN-2K19 the scheduled tasks (MasterPlandaily, MasterPlanMonthly)
run as 'sieng'. RDP to the server AS 'sieng' before running this tool.

Usage:
  SiNet.SecretImport.exe whoami
  SiNet.SecretImport.exe import C:\Temp\SiNet.secrets
  SiNet.SecretImport.exe status

Windows Credential Manager is per-user (DPAPI). Secrets imported under
account A are invisible to account B - confirm 'whoami' before importing.
"@
Set-Content -Path (Join-Path $OutputDir "README.txt") -Value $readme -Encoding UTF8

if ($SkipDeploy) {
    Write-Host "`n-SkipDeploy specified; not copying to network share." -ForegroundColor Yellow
    return
}

Write-Host "`n=== Deploying to $DeployDir (robocopy /MIR) ===" -ForegroundColor Cyan
if (-not (Test-Path (Split-Path $DeployDir -Parent))) {
    throw "Network share parent '$(Split-Path $DeployDir -Parent)' is not reachable. Check VPN / credentials, or pass -SkipDeploy."
}
if (-not (Test-Path $DeployDir)) { New-Item -ItemType Directory -Path $DeployDir -Force | Out-Null }

robocopy $OutputDir $DeployDir /MIR /R:3 /W:5 /NFL /NDL
if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }

Write-Host "`n=== Done. Admins can now run \\SI-WIN-2K19\AppFolder\AppNet\SiNet.SecretImport\SiNet.SecretImport.exe ===" -ForegroundColor Green
Get-ChildItem $DeployDir -Filter "SiNet.SecretImport.exe" | Format-Table Name, Length, LastWriteTime
