# Publish SiNetProjectManagerV2 (WPF) as MSIX with .appinstaller for auto-update.
#
# REQUIRES: a Windows Application Packaging Project (.wapproj) sibling to this
# script's project, named SiNetProjectManagerV2.Package. See DEPLOYMENT.md for
# the one-time setup steps in Visual Studio.
#
# Pipeline (mirrors publish-service.ps1 conventions):
#   1. Auto-bump <Version> in csproj (patch component) unless -NoBump.
#   2. msbuild the .wapproj with sideload + auto-update settings.
#   3. robocopy /MIR -> network share so end-user workstations get the update
#      automatically on next launch (driven by .appinstaller polling).

param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    # Output folder for the MSIX bundle + .appinstaller (local scratch).
    [string]$OutputDir = (Join-Path $PSScriptRoot "..\artifacts\SiNetProjectManagerV2_Package"),
    # Network share where users install from / pick up updates from.
    [string]$DeployDir = "\\SI-WIN-2K19\AppFolder\AppNet\SiNetProjectManagerV2",
    # Path to the Windows Application Packaging Project (created once via VS GUI).
    [string]$WapProj = (Join-Path $PSScriptRoot "..\SiNetProjectManagerV2.Package\SiNetProjectManagerV2.Package.wapproj"),
    [switch]$SkipDeploy,
    [switch]$NoBump
)

$ErrorActionPreference = "Stop"
$projectPath = Join-Path $PSScriptRoot "SiNetProjectManagerV2.csproj"

if (-not (Test-Path $WapProj)) {
    throw @"
Packaging project not found at: $WapProj

You must create it once via Visual Studio:
  1. Right-click the solution -> Add -> New Project -> 'Windows Application Packaging Project'.
  2. Name it 'SiNetProjectManagerV2.Package' and place it next to SiNetProjectManagerV2.
  3. Add the WPF project as Application reference, set as Entry Point.
  4. See SiNetProjectManagerV2\DEPLOYMENT.md for full step-by-step.
"@
}

# ---------------------------------------------------------------
# Auto-bump <Version> in the WPF csproj (patch component).
# Note: the MSIX manifest also has its own version (Package/Identity/Version
# in Package.appxmanifest). DEPLOYMENT.md explains how those stay in sync.
# ---------------------------------------------------------------
if (-not $NoBump) {
    Write-Host "=== Bumping <Version> in csproj ===" -ForegroundColor Cyan
    [xml]$xmlDoc = Get-Content $projectPath
    $pgWithVersion = $null
    foreach ($pg in $xmlDoc.Project.PropertyGroup) {
        if ($pg.Version) { $pgWithVersion = $pg; break }
    }
    if ($pgWithVersion) {
        $current = [version]$pgWithVersion.Version
        $bumped  = [version]::new($current.Major, $current.Minor, $current.Build + 1)
        $pgWithVersion.Version = $bumped.ToString()
        Write-Host "Version bumped: $current -> $bumped" -ForegroundColor Yellow
        $newVersion = $bumped.ToString()
    }
    else {
        $firstPg = $xmlDoc.Project.PropertyGroup | Select-Object -First 1
        $verEl = $xmlDoc.CreateElement("Version")
        $verEl.InnerText = "1.0.0"
        [void]$firstPg.AppendChild($verEl)
        Write-Host "Seeded <Version>1.0.0</Version> in csproj." -ForegroundColor Yellow
        $newVersion = "1.0.0"
    }
    $xmlDoc.Save($projectPath)
}
else {
    [xml]$xmlDoc = Get-Content $projectPath
    $newVersion = ($xmlDoc.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
    if (-not $newVersion) { $newVersion = "1.0.0" }
}

# MSIX requires a 4-part version (a.b.c.d). Append .0 if needed.
$msixVersion = $newVersion
if (([version]$newVersion).Revision -eq -1) { $msixVersion = "$newVersion.0" }
Write-Host "MSIX Package version: $msixVersion" -ForegroundColor Cyan

# ---------------------------------------------------------------
# Locate Visual Studio MSBuild (same approach as publish-service.ps1).
# ---------------------------------------------------------------
Write-Host "`n=== Locating Visual Studio MSBuild ===" -ForegroundColor Cyan
$vsWhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
$vsPath = & $vsWhere -latest -prerelease -products Microsoft.VisualStudio.Product.Community,Microsoft.VisualStudio.Product.Professional,Microsoft.VisualStudio.Product.Enterprise -property installationPath
if (-not $vsPath) { $vsPath = & $vsWhere -latest -prerelease -all -property installationPath | Select-Object -First 1 }
$msbuild = Join-Path $vsPath "MSBuild\Current\Bin\amd64\MSBuild.exe"
if (-not (Test-Path $msbuild)) { throw "MSBuild not found at $msbuild" }
Write-Host "MSBuild: $msbuild"

Write-Host "`n=== Cleaning output ===" -ForegroundColor Cyan
if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# ---------------------------------------------------------------
# Build the MSIX package + .appinstaller via msbuild.
#
# Key MSBuild properties (must also be set in the .wapproj for VS GUI parity):
#   UapAppxPackageBuildMode=SideloadOnly  -> no Store association
#   AppxBundle=Always                     -> single .msixbundle
#   AppxPackageSigningEnabled=true        -> sign with cert from cert store
#   GenerateAppInstallerFile=true         -> emit .appinstaller for auto-update
#   AppInstallerUri=<UNC>                 -> where clients poll for updates
#   AppxAutoIncrementPackageRevision      -> bump build number automatically
# ---------------------------------------------------------------
Write-Host "`n=== Building MSIX package (msbuild) ===" -ForegroundColor Cyan
& $msbuild $WapProj `
    /t:Restore,Build `
    /p:Configuration=$Configuration `
    /p:Platform=$Platform `
    /p:UapAppxPackageBuildMode=SideloadOnly `
    /p:AppxBundlePlatforms=$Platform `
    /p:AppxBundle=Always `
    /p:AppxPackageDir=$OutputDir\ `
    /p:GenerateAppInstallerFile=true `
    /p:AppInstallerUri=$DeployDir\ `
    /p:AppxPackageSigningEnabled=true `
    /m /v:m
if ($LASTEXITCODE -ne 0) { throw "MSIX build failed" }

Write-Host "`n=== MSIX output: $OutputDir ===" -ForegroundColor Green
Get-ChildItem $OutputDir -Recurse | Where-Object { $_.Extension -in '.msix','.msixbundle','.appinstaller','.cer' } |
    Format-Table FullName, Length, LastWriteTime

if ($SkipDeploy) {
    Write-Host "`n-SkipDeploy specified; not copying to network share." -ForegroundColor Yellow
    return
}

# ---------------------------------------------------------------
# Mirror the package folder to the UNC. End-users' installed apps poll the
# .appinstaller URL on launch and pull the new bundle automatically.
# ---------------------------------------------------------------
Write-Host "`n=== Deploying to $DeployDir (robocopy /MIR) ===" -ForegroundColor Cyan
if (-not (Test-Path (Split-Path $DeployDir -Parent))) {
    throw "Network share parent '$(Split-Path $DeployDir -Parent)' is not reachable. Check VPN / credentials, or pass -SkipDeploy."
}
if (-not (Test-Path $DeployDir)) { New-Item -ItemType Directory -Path $DeployDir -Force | Out-Null }

robocopy $OutputDir $DeployDir /MIR /R:3 /W:5 /NFL /NDL
if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }

Write-Host "`n=== Done. End-users will auto-update on next launch. ===" -ForegroundColor Green
Write-Host "First-time install: each user double-clicks:" -ForegroundColor Green
Get-ChildItem $DeployDir -Filter *.appinstaller | Format-Table FullName, LastWriteTime
