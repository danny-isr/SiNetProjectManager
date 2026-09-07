# Publish SiOffice.AccService.AuthOnce (interactive Autodesk token tool) and
# stage it into the Server install kit folder.
#
#   .\publish-tool.ps1
#   .\publish-tool.ps1 -NoBump -SkipDeploy

param(
    [string]$OutputDir = (Join-Path $PSScriptRoot "..\artifacts\SiOffice.AccService.AuthOnce_Publish"),
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$DeployDir = "\\SI-WIN-2K19\AppFolder\AppNet\Server",
    [switch]$SkipDeploy,
    [switch]$NoBump
)

$ErrorActionPreference = "Stop"
$projectPath = Join-Path $PSScriptRoot "SiOffice.AccService.AuthOnce.csproj"

# Edit the raw UTF-8 text: Get-Content without -Encoding decodes non-ASCII csproj
# content as ANSI and XmlDocument.Save then rewrites the whole file, which corrupts
# any Hebrew metadata and reformats every line.
if (-not $NoBump) {
    Write-Host "=== Bumping <Version> in csproj ===" -ForegroundColor Cyan
    $csprojUtf8 = New-Object System.Text.UTF8Encoding $false
    $projectText = [System.IO.File]::ReadAllText($projectPath, $csprojUtf8)
    $versionMatch = [regex]::Match($projectText, '<Version>[^<]+</Version>')
    if ($versionMatch.Success) {
        $current = [version]($versionMatch.Value -replace '</?Version>', '')
        $bumped = [version]::new($current.Major, $current.Minor, $current.Build + 1)
        $projectText = $projectText.Remove($versionMatch.Index, $versionMatch.Length).Insert(
            $versionMatch.Index, "<Version>$($bumped.ToString())</Version>")
        Write-Host "Version bumped: $current -> $bumped" -ForegroundColor Yellow
    }
    else {
        $firstPgEnd = $projectText.IndexOf('</PropertyGroup>')
        if ($firstPgEnd -lt 0) { throw "No <PropertyGroup> found in $projectPath -- cannot seed <Version>." }
        $projectText = $projectText.Insert($firstPgEnd, "  <Version>1.0.0</Version>$([Environment]::NewLine)  ")
        Write-Host "Seeded <Version>1.0.0</Version>." -ForegroundColor Yellow
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

$exeName = "SiOffice.AccService.AuthOnce.exe"
Get-ChildItem $OutputDir -Filter $exeName | Format-Table Name, Length, LastWriteTime

# Fail-closed stamp so publish-server-kit refuses a stale Server-kit EXE fallback.
$session = $env:SINET_AUTHONCE_BUILD_SESSION
if ([string]::IsNullOrWhiteSpace($session)) {
    $session = [guid]::NewGuid().ToString("N")
    $env:SINET_AUTHONCE_BUILD_SESSION = $session
}
$stampPath = Join-Path $OutputDir "AUTHONCE_PUBLISH_STAMP.txt"
$stampLines = @(
    ("BuiltUtc={0}" -f [datetime]::UtcNow.ToString("o")),
    ("Session={0}" -f $session),
    ("Machine={0}" -f $env:COMPUTERNAME),
    ("Exe={0}" -f $exeName),
    ("OutputDir={0}" -f $OutputDir)
)
[System.IO.File]::WriteAllLines($stampPath, $stampLines, [System.Text.Encoding]::ASCII)
Write-Host ("AuthOnce build stamp: {0} (Session={1})" -f $stampPath, $session) -ForegroundColor Cyan

if ($SkipDeploy) {
    Write-Host "`n-SkipDeploy specified; not copying to network share." -ForegroundColor Yellow
    Write-Host "Server kit must copy this artifact (publish-server-kit.ps1) — no silent reuse of an older UNC EXE." -ForegroundColor Yellow
    return
}

Write-Host "`n=== Deploying AuthOnce + wrappers to $DeployDir ===" -ForegroundColor Cyan
if (-not (Test-Path $DeployDir)) {
    New-Item -ItemType Directory -Path $DeployDir -Force | Out-Null
}

Copy-Item (Join-Path $OutputDir $exeName) (Join-Path $DeployDir $exeName) -Force

$repoRoot = Split-Path $PSScriptRoot -Parent
$refreshPs1 = Join-Path $repoRoot "SiOffice.AccService\Refresh-AccService-Token.ps1"
if (-not (Test-Path $refreshPs1)) {
    throw "Refresh-AccService-Token.ps1 not found: $refreshPs1"
}
Copy-Item $refreshPs1 (Join-Path $DeployDir "Refresh-AccService-Token.ps1") -Force
# PowerShell 5.1 on the server mis-parses UTF-8 smart punctuation (em-dash / arrows).
$refreshDest = Join-Path $DeployDir "Refresh-AccService-Token.ps1"
$refreshText = [System.IO.File]::ReadAllText($refreshPs1)
$refreshAscii = -join ($refreshText.ToCharArray() | ForEach-Object { if ([int]$_ -lt 128) { $_ } else { '-' } })
[System.IO.File]::WriteAllText($refreshDest, $refreshAscii, [System.Text.Encoding]::ASCII)

# ASCII CMD wrapper (no BOM) — UNC-safe pushd (same pattern as Server kit wrappers)
$cmdLines = @(
    "@echo off",
    "setlocal",
    "pushd ""%~dp0""",
    "if errorlevel 1 (",
    "  echo ERROR: Cannot access %~dp0",
    "  echo CMD cannot use UNC as a working directory without pushd.",
    "  pause",
    "  exit /b 1",
    ")",
    "net session >nul 2>&1",
    "if %errorlevel% neq 0 (",
    "  echo Requesting Administrator elevation...",
    "  powershell.exe -NoProfile -Command ""Start-Process -FilePath '%~f0' -Verb RunAs""",
    "  popd",
    "  exit /b",
    ")",
    "echo Refresh AccService Autodesk token from:",
    "echo   %CD%",
    "powershell.exe -NoProfile -ExecutionPolicy Bypass -File ""%~dp0Refresh-AccService-Token.ps1"" %*",
    "set ERR=%ERRORLEVEL%",
    "echo.",
    "echo Exit code: %ERR%",
    "pause",
    "popd",
    "exit /b %ERR%"
)
$ascii = [System.Text.Encoding]::ASCII
[System.IO.File]::WriteAllLines((Join-Path $DeployDir "Refresh-AccService-Token.cmd"), $cmdLines, $ascii)

Write-Host "`n=== Done. Double-click on the server: ===" -ForegroundColor Green
Write-Host "  $DeployDir\Refresh-AccService-Token.cmd" -ForegroundColor Green
Get-ChildItem $DeployDir -Filter "Refresh-AccService-Token.*" | Format-Table Name, Length, LastWriteTime
Get-ChildItem $DeployDir -Filter $exeName | Format-Table Name, Length, LastWriteTime
