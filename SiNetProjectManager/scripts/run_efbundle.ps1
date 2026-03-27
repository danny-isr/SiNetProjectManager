# run_efbundle.ps1
# Executes the EF Core Migration Bundle against the target database
# Run this script from the directory containing efbundle.exe

param(
    [Parameter(Mandatory=$false)]
    [string]$ConnectionString,
    
    [string]$BundlePath = ".\efbundle.exe",
    
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  EF Core Migration Bundle Executor" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# Verify bundle exists
if (-not (Test-Path $BundlePath -PathType Leaf)) {
    Write-Host "Error: efbundle.exe not found at: $BundlePath" -ForegroundColor Red
    Write-Host "Run build_efbundle.ps1 first to create the bundle." -ForegroundColor Yellow
    exit 1
}

# Default connection string (development)
$defaultConnectionString = "Data Source=SI-WIN-2K19\SIDATA;Initial Catalog=SIData;Integrated Security=True;TrustServerCertificate=True;"

if (-not $ConnectionString) {
    Write-Host "No connection string provided. Using default (development):" -ForegroundColor Yellow
    Write-Host "  $defaultConnectionString" -ForegroundColor Gray
    Write-Host ""
    
    $confirm = Read-Host "Continue with default connection string? (Y/N)"
    if ($confirm -ne "Y" -and $confirm -ne "y") {
        Write-Host ""
        Write-Host "Usage: .\run_efbundle.ps1 -ConnectionString '<your-connection-string>'" -ForegroundColor Cyan
        exit 0
    }
    
    $ConnectionString = $defaultConnectionString
}

Write-Host ""
Write-Host "Target database:" -ForegroundColor Yellow

# Parse and display connection info (hide password if present)
$safeConnectionString = $ConnectionString -replace "Password=[^;]*", "Password=***"
Write-Host "  $safeConnectionString" -ForegroundColor Gray
Write-Host ""

if ($WhatIf) {
    Write-Host "[WhatIf] Would execute: $BundlePath --connection `"$safeConnectionString`"" -ForegroundColor Magenta
    exit 0
}

# Confirm before executing
Write-Host "WARNING: This will apply pending migrations to the database." -ForegroundColor Yellow
Write-Host "Make sure you have a backup before proceeding." -ForegroundColor Yellow
Write-Host ""

$confirm = Read-Host "Apply migrations? (Y/N)"
if ($confirm -ne "Y" -and $confirm -ne "y") {
    Write-Host "Aborted." -ForegroundColor Red
    exit 0
}

Write-Host ""
Write-Host "Applying migrations..." -ForegroundColor Yellow
Write-Host ""

try {
    & $BundlePath --connection $ConnectionString

    if ($LASTEXITCODE -ne 0) {
        throw "Migration bundle execution failed with exit code $LASTEXITCODE"
    }

    Write-Host ""
    Write-Host "============================================" -ForegroundColor Green
    Write-Host "  Migrations applied successfully!" -ForegroundColor Green
    Write-Host "============================================" -ForegroundColor Green
    Write-Host ""
}
catch {
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Red
    Write-Host "  Migration FAILED" -ForegroundColor Red
    Write-Host "============================================" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error: $_" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please check:" -ForegroundColor Yellow
    Write-Host "  1. Database server is accessible" -ForegroundColor Gray
    Write-Host "  2. Connection string is correct" -ForegroundColor Gray
    Write-Host "  3. User has sufficient permissions" -ForegroundColor Gray
    exit 1
}
