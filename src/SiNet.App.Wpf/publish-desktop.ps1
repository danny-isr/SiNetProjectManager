# Publish SiNet.App.Wpf (WPF .NET 10) as MSIX with .appinstaller for auto-update.
# Uses the modern "self-contained MSIX" approach: NO .wapproj.
# Pipeline mirrors SiNetProjectManagerV2/publish-desktop.ps1 with a distinct package identity.
#
#   1. Auto-bump <Version> in csproj (patch) unless -NoBump.
#   2. dotnet publish: self-contained, win-x64, layout to staging folder.
#   3. Copy Package.appxmanifest + Images\* into staging ({VERSION} substituted).
#   4. MakeAppx.exe pack -> .msix
#   5. SignTool.exe sign  -> signed .msix
#   6. Generate .appinstaller pointing at the UNC share.
#   7. robocopy /MIR -> network share.

param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = (Join-Path $PSScriptRoot "..\..\artifacts\SiNet.App.Wpf_Package"),
    [string]$DeployDir = "\\SI-WIN-2K19\AppFolder\AppNet\SiNet.App.Wpf",
    [string]$CertThumbprint,
    [string]$CertPfxPath,
    [string]$CertPfxPassword,
    [switch]$SkipDeploy,
    [switch]$SkipSign,
    [switch]$NoBump
)

$ErrorActionPreference = "Stop"
$projectPath  = Join-Path $PSScriptRoot "SiNet.App.Wpf.csproj"
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
$payloadDir = Join-Path $OutputDir "payload"
$artefactDir = Join-Path $OutputDir "artefacts"
New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null
New-Item -ItemType Directory -Path $artefactDir -Force | Out-Null

# ---------------------------------------------------------------
# Publish self-contained loose files for MSIX.
# ---------------------------------------------------------------
Write-Host "`n=== Publishing SiNet.App.Wpf ===" -ForegroundColor Cyan
Push-Location $PSScriptRoot
try {
    dotnet publish $projectPath `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -o $payloadDir `
        /p:PublishSingleFile=false `
        /p:PublishReadyToRun=true `
        /p:DebugType=embedded
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
}
finally { Pop-Location }

# ---------------------------------------------------------------
# Stage manifest + image assets into the payload folder.
# ---------------------------------------------------------------
Write-Host "`n=== Staging manifest + images ===" -ForegroundColor Cyan
$manifestText = (Get-Content $manifestSrc -Raw).Replace('{VERSION}', $msixVersion)
Set-Content -Path (Join-Path $payloadDir "AppxManifest.xml") -Value $manifestText -Encoding UTF8

if (-not (Test-Path $imagesSrc)) {
    Write-Host "  Images\ folder not found -- generating placeholder PNGs." -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $imagesSrc -Force | Out-Null
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
$msixName = "SiNet.App.Wpf_$msixVersion`_x64.msix"
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
        $cert = Get-ChildItem Cert:\CurrentUser\My |
            Where-Object { $_.EnhancedKeyUsageList.ObjectId -contains "1.3.6.1.5.5.7.3.3" } |
            Select-Object -First 1
        if (-not $cert) {
            throw "No code-signing certificate found. Pass -CertThumbprint, -CertPfxPath, or create one."
        }
        Write-Host "Auto-selected cert: $($cert.Subject) ($($cert.Thumbprint))"
        $signArgs += @("/sha1", $cert.Thumbprint)
    }
    $signArgs += $msixPath
    & $signTool @signArgs
    if ($LASTEXITCODE -ne 0) { throw "SignTool failed" }
}

# ---------------------------------------------------------------
# Generate the .appinstaller file.
# ---------------------------------------------------------------
$appInstallerName = "SiNet.App.Wpf.appinstaller"
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
      Name="SiNet.App.Wpf"
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
