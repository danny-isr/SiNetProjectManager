# Publish MasterPlan.SyncEngine to a network share so the Task Scheduler on the
# server picks up the latest single-file EXE on its next run.
#
# Pipeline (mirrors publish-service.ps1 conventions):
#   1. Auto-bump <Version> in csproj (patch component) unless -NoBump.
#   2. dotnet publish: self-contained, single-file, win-x64 -> intermediate folder.
#   3. robocopy /MIR -> network share. The Task Scheduler keeps pointing at the
#      same UNC; next scheduled run executes the new exe.
#
# No installer is needed: the console app has no SCM registration, no per-user
# state, no shortcuts. Files-on-a-share is the simplest correct deployment.

param(
    # Intermediate publish folder (local scratch - never run from here).
    [string]$OutputDir = (Join-Path $PSScriptRoot "..\artifacts\MasterPlanSync_Publish"),
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    # Network share where the Task Scheduler runs the exe from.
    [string]$DeployDir = "\\SI-WIN-2K19\AppFolder\AppNet\MasterPlan.SyncEngine",
    [switch]$SkipDeploy,
    # Skip the automatic version bump (use the existing <Version> as-is, or 1.0.0 if missing).
    [switch]$NoBump
)

$ErrorActionPreference = "Stop"
$projectPath = Join-Path $PSScriptRoot "MasterPlan.SyncEngine.csproj"

# ---------------------------------------------------------------
# Auto-bump <Version> (or seed it at 1.0.0 if absent).
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
    }
    else {
        # Seed the first <Version> in the first PropertyGroup.
        $firstPg = $xmlDoc.Project.PropertyGroup | Select-Object -First 1
        $verEl = $xmlDoc.CreateElement("Version")
        $verEl.InnerText = "1.0.0"
        [void]$firstPg.AppendChild($verEl)
        Write-Host "Seeded <Version>1.0.0</Version> in csproj." -ForegroundColor Yellow
    }
    $xmlDoc.Save($projectPath)
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
Get-ChildItem $OutputDir | Format-Table Name, Length, LastWriteTime

if ($SkipDeploy) {
    Write-Host "`n-SkipDeploy specified; not copying to network share." -ForegroundColor Yellow
    return
}

Write-Host "`n=== Deploying to $DeployDir (robocopy /MIR) ===" -ForegroundColor Cyan
if (-not (Test-Path (Split-Path $DeployDir -Parent))) {
    throw "Network share parent '$(Split-Path $DeployDir -Parent)' is not reachable. Check VPN / credentials, or pass -SkipDeploy."
}
if (-not (Test-Path $DeployDir)) { New-Item -ItemType Directory -Path $DeployDir -Force | Out-Null }

# /MIR = mirror (removes deleted files). /R:3 /W:5 = retry policy.
# /NFL /NDL = quiet output. Exit codes 0-7 are success, >=8 is failure.
robocopy $OutputDir $DeployDir /MIR /R:3 /W:5 /NFL /NDL
if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }

Write-Host "`n=== Done. Task Scheduler on the server will pick up the new exe on its next run. ===" -ForegroundColor Green
Get-ChildItem $DeployDir -Filter "MasterPlan.SyncEngine.exe" | Format-Table Name, Length, LastWriteTime
