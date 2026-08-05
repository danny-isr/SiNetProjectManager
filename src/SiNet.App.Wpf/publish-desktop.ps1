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
# UTF-8 round-trip + text edit are both required: Get-Content without -Encoding
# decodes the Hebrew AssemblyTitle/Product/Company as ANSI, and XmlDocument.Save
# then rewrites the whole file, so an XML round-trip corrupts the branding and
# reformats every line. Read and write the raw UTF-8 text instead.
# ---------------------------------------------------------------
$csprojUtf8 = New-Object System.Text.UTF8Encoding $false
$projectText = [System.IO.File]::ReadAllText($projectPath, $csprojUtf8)
$versionPattern = '(?<open><Version>)(?<value>[^<]+)(?<close></Version>)'

if (-not $NoBump) {
    Write-Host "=== Bumping <Version> in csproj ===" -ForegroundColor Cyan
    $versionMatch = [regex]::Match($projectText, $versionPattern)
    if ($versionMatch.Success) {
        $current = [version]$versionMatch.Groups['value'].Value
        $bumped  = [version]::new($current.Major, $current.Minor, $current.Build + 1)
        $projectText = $projectText.Remove($versionMatch.Index, $versionMatch.Length).Insert(
            $versionMatch.Index, "<Version>$($bumped.ToString())</Version>")
        Write-Host "Version bumped: $current -> $bumped" -ForegroundColor Yellow
    }
    else {
        $firstPgEnd = $projectText.IndexOf('</PropertyGroup>')
        if ($firstPgEnd -lt 0) { throw "No <PropertyGroup> found in $projectPath -- cannot seed <Version>." }
        $projectText = $projectText.Insert($firstPgEnd, "  <Version>1.0.0</Version>$([Environment]::NewLine)  ")
        Write-Host "Seeded <Version>1.0.0</Version> in csproj." -ForegroundColor Yellow
    }
    [System.IO.File]::WriteAllText($projectPath, $projectText, $csprojUtf8)
}

$rawVersion = [regex]::Match($projectText, $versionPattern).Groups['value'].Value
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
# UTF-8 round-trip is required: Windows PowerShell Get-Content without -Encoding
# corrupts Hebrew DisplayName in Package.appxmanifest (taskbar shows mojibake).
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
$manifestText = [System.IO.File]::ReadAllText($manifestSrc, $utf8NoBom).Replace('{VERSION}', $msixVersion)
$manifestOut = Join-Path $payloadDir "AppxManifest.xml"
# MakeAppx accepts UTF-8 with BOM for non-ASCII DisplayName.
$utf8Bom = New-Object System.Text.UTF8Encoding $true
[System.IO.File]::WriteAllText($manifestOut, $manifestText, $utf8Bom)

$requiredLogos = @("StoreLogo.png", "Square150x150Logo.png", "Square44x44Logo.png")
$logoSource = Join-Path $PSScriptRoot "Assets\sinet.ico"
if (-not (Test-Path $logoSource)) {
    $logoSource = Join-Path $PSScriptRoot "Assets\shia-chadash-mark.png"
}
$needGenerateLogos = -not (Test-Path $imagesSrc)
if (-not $needGenerateLogos) {
    foreach ($name in $requiredLogos) {
        if (-not (Test-Path (Join-Path $imagesSrc $name))) { $needGenerateLogos = $true; break }
    }
}
if ($needGenerateLogos -or -not (Test-Path $logoSource)) {
    if (-not (Test-Path $logoSource)) {
        throw "Logo source not found (Assets\sinet.ico / Assets\shia-chadash-mark.png). Cannot build MSIX icons."
    }
}
# Always regenerate MSIX logos from the branded ICO/PNG so placeholders cannot ship.
Write-Host "  Generating MSIX logos from $logoSource" -ForegroundColor Cyan
New-Item -ItemType Directory -Path $imagesSrc -Force | Out-Null
Add-Type -AssemblyName System.Drawing
$srcImg = [System.Drawing.Image]::FromFile((Resolve-Path $logoSource))
try {
    $sizes = @{
        "StoreLogo.png"           = 50
        "Square150x150Logo.png"   = 150
        "Square44x44Logo.png"     = 44
    }
    foreach ($entry in $sizes.GetEnumerator()) {
        $size = [int]$entry.Value
        $bmp = New-Object System.Drawing.Bitmap $size, $size
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.DrawImage($srcImg, 0, 0, $size, $size)
        $g.Dispose()
        $bmp.Save((Join-Path $imagesSrc $entry.Key), [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
    }
}
finally {
    $srcImg.Dispose()
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
        # CurrentUser\My (default). Do not pass /sm — that selects LocalMachine and "/sm:no" is invalid.
        $signArgs += @("/sha1", $CertThumbprint)
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
