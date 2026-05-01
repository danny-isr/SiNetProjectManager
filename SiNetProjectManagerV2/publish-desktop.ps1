# Publish SiNetProjectManagerV2 (WPF .NET 10) as MSIX with .appinstaller for
# auto-update. Uses the modern "self-contained MSIX" approach: NO .wapproj,
# NO Windows Application Packaging Project. Just `dotnet publish` -> MakeAppx
# -> SignTool -> robocopy. All tools ship with the Windows 10/11 SDK that VS
# installed for you.
#
# Pipeline (mirrors publish-service.ps1 conventions):
#   1. Auto-bump <Version> in csproj (patch component) unless -NoBump.
#   2. dotnet publish: self-contained, win-x64, layout to staging folder.
#   3. Copy Package.appxmanifest + Images\* into the staging folder, with
#      {VERSION} substituted to a 4-part version.
#   4. MakeAppx.exe pack -> .msix
#   5. SignTool.exe sign  -> signed .msix
#   6. Generate .appinstaller pointing at the UNC share (auto-update channel).
#   7. robocopy /MIR -> network share so end-user workstations auto-update.

param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    # Output folder for the published payload + MSIX + .appinstaller (local scratch).
    [string]$OutputDir = (Join-Path $PSScriptRoot "..\artifacts\SiNetProjectManagerV2_Package"),
    # Network share where users install from / pick up updates from.
    [string]$DeployDir = "\\SI-WIN-2K19\AppFolder\AppNet\SiNetProjectManagerV2",
    # Code-signing certificate. Either path to .pfx OR thumbprint in CurrentUser\My.
    [string]$CertThumbprint,
    [string]$CertPfxPath,
    [string]$CertPfxPassword,
    [switch]$SkipDeploy,
    [switch]$SkipSign,
    [switch]$NoBump
)

$ErrorActionPreference = "Stop"
$projectPath  = Join-Path $PSScriptRoot "SiNetProjectManagerV2.csproj"
$manifestSrc  = Join-Path $PSScriptRoot "Package.appxmanifest"
$imagesSrc    = Join-Path $PSScriptRoot "Images"

if (-not (Test-Path $manifestSrc)) {
    throw "Package.appxmanifest not found at $manifestSrc -- should be committed alongside the csproj."
}

# ---------------------------------------------------------------
# Auto-bump <Version> in the csproj (patch component).
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
        $firstPg = $xmlDoc.Project.PropertyGroup | Select-Object -First 1
        $verEl = $xmlDoc.CreateElement("Version")
        $verEl.InnerText = "1.0.0"
        [void]$firstPg.AppendChild($verEl)
        Write-Host "Seeded <Version>1.0.0</Version> in csproj." -ForegroundColor Yellow
    }
    $xmlDoc.Save($projectPath)
}

[xml]$csproj = Get-Content $projectPath
$rawVersion = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if (-not $rawVersion) { $rawVersion = "1.0.0" }
# MSIX requires 4-part version; revision MUST be 0 for .appinstaller updates.
$msixVersion = if (([version]$rawVersion).Revision -eq -1) { "$rawVersion.0" } else { $rawVersion }
Write-Host "MSIX Package version: $msixVersion" -ForegroundColor Cyan

# ---------------------------------------------------------------
# Locate Windows SDK tools (MakeAppx.exe, SignTool.exe).
# ---------------------------------------------------------------
Write-Host "`n=== Locating Windows SDK tools ===" -ForegroundColor Cyan
$sdkRoot = "C:\Program Files (x86)\Windows Kits\10\bin"
if (-not (Test-Path $sdkRoot)) { throw "Windows SDK not found at $sdkRoot" }
# Pick the highest-versioned bin folder that has both tools, preferring x64.
$sdkBin = Get-ChildItem $sdkRoot -Directory |
    Where-Object { $_.Name -match '^10\.' } |
    Sort-Object Name -Descending |
    ForEach-Object { Join-Path $_.FullName "x64" } |
    Where-Object { (Test-Path (Join-Path $_ "MakeAppx.exe")) -and (Test-Path (Join-Path $_ "SignTool.exe")) } |
    Select-Object -First 1
if (-not $sdkBin) { throw "MakeAppx.exe / SignTool.exe not found under $sdkRoot. Install the Windows 10/11 SDK." }
$makeAppx = Join-Path $sdkBin "MakeAppx.exe"
$signTool = Join-Path $sdkBin "SignTool.exe"
Write-Host "SDK bin: $sdkBin"

# ---------------------------------------------------------------
# Clean output / staging.
# ---------------------------------------------------------------
Write-Host "`n=== Cleaning output ===" -ForegroundColor Cyan
if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
$payloadDir = Join-Path $OutputDir "payload"   # what gets packed into .msix
$artefactDir = Join-Path $OutputDir "artefacts" # the signed .msix + .appinstaller
New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null
New-Item -ItemType Directory -Path $artefactDir -Force | Out-Null

# ---------------------------------------------------------------
# Locate Visual Studio MSBuild (handles COM ResolveComReference, which the
# .NET 10 SDK MSBuild does not support -- error MSB4803). Same workaround
# as publish-service.ps1.
# ---------------------------------------------------------------
Write-Host "`n=== Locating Visual Studio MSBuild ===" -ForegroundColor Cyan
$vsWhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
$vsPath = & $vsWhere -latest -prerelease -products Microsoft.VisualStudio.Product.Community,Microsoft.VisualStudio.Product.Professional,Microsoft.VisualStudio.Product.Enterprise -property installationPath
if (-not $vsPath) { $vsPath = & $vsWhere -latest -prerelease -all -property installationPath | Select-Object -First 1 }
$msbuild = Join-Path $vsPath "MSBuild\Current\Bin\amd64\MSBuild.exe"
if (-not (Test-Path $msbuild)) { throw "MSBuild not found at $msbuild" }
Write-Host "MSBuild: $msbuild"

# ---------------------------------------------------------------
# Build with VS MSBuild (handles COM refs), then dotnet publish --no-build
# to lay out the loose files MSIX needs (self-contained=true,
# single-file=false: MSIX requires loose files, not a single-file bundle).
# ---------------------------------------------------------------
Write-Host "`n=== Building via VS MSBuild (handles COM refs) ===" -ForegroundColor Cyan
& $msbuild $projectPath /t:Restore,Build `
    /p:Configuration=$Configuration `
    /p:RuntimeIdentifier=$Runtime `
    /p:SelfContained=true `
    /p:PublishSingleFile=false `
    /p:PublishReadyToRun=true `
    /m /v:m
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

Write-Host "`n=== Publishing (no rebuild) ===" -ForegroundColor Cyan
Push-Location $PSScriptRoot
try {
    dotnet publish $projectPath `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        --no-build `
        -o $payloadDir `
        /p:PublishSingleFile=false `
        /p:PublishReadyToRun=true `
        /p:DebugType=embedded
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
}
finally { Pop-Location }

# ---------------------------------------------------------------
# Stage manifest + image assets into the payload folder.
# MakeAppx requires AppxManifest.xml at the root.
# ---------------------------------------------------------------
Write-Host "`n=== Staging manifest + images ===" -ForegroundColor Cyan
$manifestText = (Get-Content $manifestSrc -Raw).Replace('{VERSION}', $msixVersion)
Set-Content -Path (Join-Path $payloadDir "AppxManifest.xml") -Value $manifestText -Encoding UTF8

if (-not (Test-Path $imagesSrc)) {
    Write-Host "  Images\ folder not found -- generating placeholder PNGs." -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $imagesSrc -Force | Out-Null
    # Generate trivial 1x1 transparent PNGs for the three required logos.
    # (Replace these with proper artwork later; MSIX install works with placeholders.)
    Add-Type -AssemblyName System.Drawing
    foreach ($name in @("StoreLogo.png", "Square150x150Logo.png", "Square44x44Logo.png")) {
        $bmp = New-Object System.Drawing.Bitmap 64,64
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.Clear([System.Drawing.Color]::FromArgb(0,120,215))
        $g.Dispose()
        $bmp.Save((Join-Path $imagesSrc $name), [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
    }
}
$payloadImagesDir = Join-Path $payloadDir "Images"
New-Item -ItemType Directory -Path $payloadImagesDir -Force | Out-Null
Copy-Item (Join-Path $imagesSrc "*") $payloadImagesDir -Force

# ---------------------------------------------------------------
# Pack into .msix
# ---------------------------------------------------------------
$msixName = "SiNetProjectManagerV2_$msixVersion`_x64.msix"
$msixPath = Join-Path $artefactDir $msixName
Write-Host "`n=== MakeAppx pack -> $msixName ===" -ForegroundColor Cyan
& $makeAppx pack /d $payloadDir /p $msixPath /o
if ($LASTEXITCODE -ne 0) { throw "MakeAppx pack failed" }

# ---------------------------------------------------------------
# Sign the .msix
# ---------------------------------------------------------------
if ($SkipSign) {
    Write-Host "`n-SkipSign specified; .msix is UNSIGNED and cannot be installed." -ForegroundColor Yellow
}
else {
    Write-Host "`n=== SignTool sign ===" -ForegroundColor Cyan
    $signArgs = @("sign", "/fd", "SHA256")
    if ($CertPfxPath) {
        if (-not (Test-Path $CertPfxPath)) { throw "Cert .pfx not found at $CertPfxPath" }
        $signArgs += @("/f", $CertPfxPath)
        if ($CertPfxPassword) { $signArgs += @("/p", $CertPfxPassword) }
    }
    elseif ($CertThumbprint) {
        $signArgs += @("/sha1", $CertThumbprint, "/sm:no")
    }
    else {
        # Auto-pick the first code-signing cert from CurrentUser\My with a friendly name match.
        $cert = Get-ChildItem Cert:\CurrentUser\My |
            Where-Object { $_.EnhancedKeyUsageList.ObjectId -contains "1.3.6.1.5.5.7.3.3" } |
            Select-Object -First 1
        if (-not $cert) {
            throw "No code-signing certificate found. Pass -CertThumbprint, -CertPfxPath, or create one (see DEPLOYMENT.md)."
        }
        Write-Host "Auto-selected cert: $($cert.Subject) ($($cert.Thumbprint))"
        $signArgs += @("/sha1", $cert.Thumbprint)
    }
    $signArgs += $msixPath
    & $signTool @signArgs
    if ($LASTEXITCODE -ne 0) { throw "SignTool failed" }
}

# ---------------------------------------------------------------
# Generate the .appinstaller file (auto-update channel descriptor).
# Clients double-click this once; afterwards Windows polls the URL on every
# launch and pulls the new .msix automatically.
# ---------------------------------------------------------------
$appInstallerName = "SiNetProjectManagerV2.appinstaller"
$appInstallerPath = Join-Path $artefactDir $appInstallerName
$msixUriOnShare = ($DeployDir.TrimEnd('\') + "\" + $msixName)
$appInstUriOnShare = ($DeployDir.TrimEnd('\') + "\" + $appInstallerName)

$appInstallerXml = @"
<?xml version="1.0" encoding="utf-8"?>
<AppInstaller
    xmlns="http://schemas.microsoft.com/appx/appinstaller/2018"
    Uri="$appInstUriOnShare"
    Version="$msixVersion">

  <MainPackage
      Name="SiNet.ProjectManagerV2"
      Publisher="CN=SI Office"
      Version="$msixVersion"
      ProcessorArchitecture="x64"
      Uri="$msixUriOnShare" />

  <UpdateSettings>
    <OnLaunch HoursBetweenUpdateChecks="0" />
    <AutomaticBackgroundTask />
  </UpdateSettings>
</AppInstaller>
"@
Set-Content -Path $appInstallerPath -Value $appInstallerXml -Encoding UTF8
Write-Host "`n=== Wrote .appinstaller -> $appInstallerPath ===" -ForegroundColor Green

# ---------------------------------------------------------------
# Mirror artefacts to the UNC share.
# ---------------------------------------------------------------
Write-Host "`n=== Output: $artefactDir ===" -ForegroundColor Green
Get-ChildItem $artefactDir | Format-Table Name, Length, LastWriteTime

if ($SkipDeploy) {
    Write-Host "`n-SkipDeploy specified; not copying to network share." -ForegroundColor Yellow
    return
}
if (-not (Test-Path (Split-Path $DeployDir -Parent))) {
    throw "Network share parent '$(Split-Path $DeployDir -Parent)' is not reachable. Check VPN / credentials, or pass -SkipDeploy."
}
if (-not (Test-Path $DeployDir)) { New-Item -ItemType Directory -Path $DeployDir -Force | Out-Null }

Write-Host "`n=== Deploying to $DeployDir (robocopy /MIR) ===" -ForegroundColor Cyan
robocopy $artefactDir $DeployDir /MIR /R:3 /W:5 /NFL /NDL
if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }

Write-Host "`n=== Done. End-users will auto-update on next launch. ===" -ForegroundColor Green
Write-Host "First-time install: each user double-clicks:" -ForegroundColor Green
Write-Host "    $appInstUriOnShare" -ForegroundColor Green
