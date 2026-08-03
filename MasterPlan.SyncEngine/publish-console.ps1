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
#
# SECRETS WARNING:
#   appsettings.json is published alongside the exe but must NOT contain API keys.
#   MasterPlan API key must live in Windows Credential Manager (SiNet/MasterPlanApi/ApiKey)
#   or the MASTERPLAN_API_KEY env var on the server. Use appsettings.template.json as
#   the reference structure when provisioning a new machine — never copy secrets to Git
#   or to the network share.

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
        # Seed the first <Version> in the first PropertyGroup.
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

# Ensure template is present on the share; warn if deployed appsettings contains a non-empty ApiKey.
$deployAppSettings = Join-Path $DeployDir "appsettings.json"
$templatePath = Join-Path $PSScriptRoot "appsettings.template.json"
if (Test-Path $templatePath) {
    Copy-Item $templatePath (Join-Path $DeployDir "appsettings.template.json") -Force
}
if (Test-Path $deployAppSettings) {
    try {
        $settings = Get-Content $deployAppSettings -Raw | ConvertFrom-Json
        $apiKey = $settings.MasterPlanApi.ApiKey
        if (-not [string]::IsNullOrWhiteSpace($apiKey)) {
            Write-Host "`nWARNING: Deployed appsettings.json contains a non-empty MasterPlanApi:ApiKey." -ForegroundColor Red
            Write-Host "         Remove the key from the file and provision via vault or MASTERPLAN_API_KEY env var." -ForegroundColor Red
        }
    }
    catch {
        Write-Host "`nWARNING: Could not validate deployed appsettings.json for embedded secrets." -ForegroundColor Yellow
    }
}

Write-Host "`n=== Done. Task Scheduler on the server will pick up the new exe on its next run. ===" -ForegroundColor Green
Get-ChildItem $DeployDir -Filter "MasterPlan.SyncEngine.exe" | Format-Table Name, Length, LastWriteTime
