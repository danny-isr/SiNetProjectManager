# Publish SiOffice.AccService for production deployment AND build the updater MSI.
# Workaround for .NET 10 SDK MSB4803 (COM ResolveComReference) — uses full VS MSBuild for Build, dotnet for Publish.
#
# Pipeline:
#   1. Publish AccService payload to a local intermediate folder ($OutputDir, default under repo \artifacts).
#   2. Build SiOfficeAccService.msi from that payload.
#   3. Copy the final MSI to the network share ($MsiDeployDir) so the server can run it directly.

param(
    # Intermediate publish folder. Local-only scratch space; you never run anything from here.
    [string]$OutputDir = (Join-Path $PSScriptRoot "..\artifacts\AccService_Publish"),
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    # Network share where the final MSI lands. The server picks it up from here.
    [string]$MsiDeployDir = "\\SI-WIN-2K19\AppFolder\AppNet\SiProjecNet2026-Full",
    [switch]$SkipMsi,
    [switch]$SkipDeploy,
    # Skip the automatic version bump (use the existing <Version> in csproj as-is).
    [switch]$NoBump
)

$ErrorActionPreference = "Stop"
$projectPath  = Join-Path $PSScriptRoot "SiOffice.AccService.csproj"
$installerDir = Resolve-Path (Join-Path $PSScriptRoot "..\SiOffice.AccService.Installer")
$installerProj = Join-Path $installerDir "SiOffice.AccService.Installer.wixproj"

# ---------------------------------------------------------------
# Auto-bump <Version> in the csproj (patch component) unless -NoBump.
# This guarantees that every publish produces a new MSI ProductVersion
# and FileVersion, so MajorUpgrade replaces the DLL on the server.
# ---------------------------------------------------------------
if (-not $NoBump) {
    Write-Host "=== Bumping <Version> in csproj ===" -ForegroundColor Cyan
    [xml]$xmlDoc = Get-Content $projectPath
    $versionNode = $xmlDoc.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $versionNode) { throw "No <Version> element found in $projectPath" }

    $current = [version]$versionNode
    $bumped  = [version]::new($current.Major, $current.Minor, $current.Build + 1)

    # Find the actual XML node and update its inner text
    foreach ($pg in $xmlDoc.Project.PropertyGroup) {
        if ($pg.Version) {
            $pg.Version = $bumped.ToString()
            break
        }
    }
    $xmlDoc.Save($projectPath)
    Write-Host "Version bumped: $current -> $bumped" -ForegroundColor Yellow
}


Write-Host "=== Locating Visual Studio MSBuild ===" -ForegroundColor Cyan
$vsWhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
$vsPath = & $vsWhere -latest -prerelease -products Microsoft.VisualStudio.Product.Community,Microsoft.VisualStudio.Product.Professional,Microsoft.VisualStudio.Product.Enterprise -property installationPath
if (-not $vsPath) { $vsPath = & $vsWhere -latest -prerelease -all -property installationPath | Select-Object -First 1 }
$msbuild = Join-Path $vsPath "MSBuild\Current\Bin\amd64\MSBuild.exe"
if (-not (Test-Path $msbuild)) { throw "MSBuild not found at $msbuild" }
Write-Host "MSBuild: $msbuild"

Write-Host "`n=== Cleaning output ===" -ForegroundColor Cyan
if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }

Write-Host "`n=== Building via VS MSBuild (handles COM refs) ===" -ForegroundColor Cyan
& $msbuild $projectPath /t:Restore,Build /p:Configuration=$Configuration /p:RuntimeIdentifier=$Runtime /p:SelfContained=false /m /v:m
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

Write-Host "`n=== Publishing (no rebuild) ===" -ForegroundColor Cyan
Push-Location $PSScriptRoot
try {
    dotnet publish -c $Configuration -r $Runtime --self-contained false --no-build -o $OutputDir
    if ($LASTEXITCODE -ne 0) { throw "Publish failed" }
}
finally { Pop-Location }

Write-Host "`n=== Publish output: $OutputDir ===" -ForegroundColor Green
Get-ChildItem $OutputDir | Where-Object { $_.Name -match "AccService\.(exe|dll)$|appsettings" } | Format-Table Name, Length

if ($SkipMsi) {
    Write-Host "`n-SkipMsi specified, not building installer." -ForegroundColor Yellow
    return
}

Write-Host "`n=== Reading version from csproj ===" -ForegroundColor Cyan
[xml]$csproj = Get-Content $projectPath
$productVersion = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if (-not $productVersion) { throw "Could not read <Version> from $projectPath" }
Write-Host "ProductVersion: $productVersion"

Write-Host "`n=== Building updater MSI ===" -ForegroundColor Cyan

# Create a "shared" payload folder that contains every published file
# EXCEPT the service exe. The WiX <Files> harvester reads this folder, while
# the ServiceHost component references the exe directly from $OutputDir. This
# avoids the duplicate-component / single-KeyPath conflict on the .exe.
$sharedDir = Join-Path (Split-Path $OutputDir -Parent) "AccService_MsiShared"
if (Test-Path $sharedDir) { Remove-Item $sharedDir -Recurse -Force }
Copy-Item $OutputDir $sharedDir -Recurse -Force
Remove-Item (Join-Path $sharedDir "SiOffice.AccService.exe") -Force -ErrorAction SilentlyContinue

dotnet build $installerProj `
    -c $Configuration `
    -p:PublishDir=$OutputDir `
    -p:SharedDir=$sharedDir `
    -p:ProductVersion=$productVersion
if ($LASTEXITCODE -ne 0) { throw "MSI build failed" }

$msiPath = Join-Path $installerDir "bin\$Configuration\SiOfficeAccService.msi"
if (-not (Test-Path $msiPath)) { throw "Expected MSI not found at $msiPath" }

Write-Host "`n=== Built MSI: $msiPath ===" -ForegroundColor Green
Get-Item $msiPath | Format-Table Name, Length, LastWriteTime

if ($SkipDeploy) {
    Write-Host "`n-SkipDeploy specified, MSI was NOT copied to network share." -ForegroundColor Yellow
    return
}

Write-Host "`n=== Deploying MSI to $MsiDeployDir ===" -ForegroundColor Cyan
if (-not (Test-Path $MsiDeployDir)) {
    throw "Network share '$MsiDeployDir' is not reachable. Check VPN / credentials and try again, or pass -SkipDeploy."
}

$deployedMsi = Join-Path $MsiDeployDir "SiOfficeAccService.msi"
Copy-Item $msiPath $deployedMsi -Force

Write-Host "`n=== Done. Server can now run: ===" -ForegroundColor Green
Write-Host "    msiexec /i `"$deployedMsi`" /qn /l*v upgrade.log" -ForegroundColor Green
Get-Item $deployedMsi | Format-Table Name, Length, LastWriteTime
