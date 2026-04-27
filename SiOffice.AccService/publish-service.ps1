# Publish SiOffice.AccService for production deployment
# Workaround for .NET 10 SDK MSB4803 (COM ResolveComReference) — uses full VS MSBuild for Build, dotnet for Publish.

param(
    [string]$OutputDir = "D:\AccService_Publish",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$projectPath = Join-Path $PSScriptRoot "SiOffice.AccService.csproj"

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

Write-Host "`n=== Done. Output: $OutputDir ===" -ForegroundColor Green
Get-ChildItem $OutputDir | Where-Object { $_.Name -match "AccService\.(exe|dll)$|appsettings" } | Format-Table Name, Length
