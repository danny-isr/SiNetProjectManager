# build_efbundle.ps1
# Builds the EF Core Migration Bundle for SiNetSQL
# Run this script from the SiNetProjectManager solution directory

param(
    [string]$OutputPath = ".\efbundle.exe",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  EF Core Migration Bundle Builder" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# Verify we're in the right directory
if (-not (Test-Path "SiNetProjectManager.sln" -PathType Leaf)) {
    Write-Host "Error: SiNetProjectManager.sln not found." -ForegroundColor Red
    Write-Host "Please run this script from the solution root directory." -ForegroundColor Yellow
    exit 1
}

# Check if dotnet ef is installed
$efVersion = dotnet ef --version 2>$null
if (-not $efVersion) {
    Write-Host "Error: dotnet ef tool not found." -ForegroundColor Red
    Write-Host "Install it with: dotnet tool install --global dotnet-ef" -ForegroundColor Yellow
    exit 1
}

Write-Host "Using EF Core Tools version: $efVersion" -ForegroundColor Green
Write-Host ""

# Build the bundle
Write-Host "Building migration bundle..." -ForegroundColor Yellow
Write-Host "  Project: ..\SiNetSQL\SiNetSQL\SiNetSQL.csproj" -ForegroundColor Gray
Write-Host "  Startup: SiNetProjectManager\SiNetProjectManager.csproj" -ForegroundColor Gray
Write-Host "  Output:  $OutputPath" -ForegroundColor Gray
Write-Host ""

try {
    dotnet ef migrations bundle `
        --project "..\SiNetSQL\SiNetSQL\SiNetSQL.csproj" `
        --startup-project "SiNetProjectManager\SiNetProjectManager.csproj" `
        --context SiNetSQLDbContext `
        --configuration $Configuration `
        --output $OutputPath `
        --force `
        --self-contained

    if ($LASTEXITCODE -ne 0) {
        throw "Migration bundle build failed with exit code $LASTEXITCODE"
    }

    Write-Host ""
    Write-Host "============================================" -ForegroundColor Green
    Write-Host "  Bundle created successfully!" -ForegroundColor Green
    Write-Host "============================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Output file: $OutputPath" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "  1. Copy efbundle.exe to the deployment target" -ForegroundColor Gray
    Write-Host "  2. Run: .\run_efbundle.ps1 -ConnectionString '<your-connection-string>'" -ForegroundColor Gray
    Write-Host ""
}
catch {
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Red
    Write-Host "  Build FAILED" -ForegroundColor Red
    Write-Host "============================================" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error: $_" -ForegroundColor Red
    exit 1
}
