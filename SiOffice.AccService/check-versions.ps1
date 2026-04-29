# Diagnostic script - verifies version flow through every stage of deployment.
# Run from anywhere: .\check-versions.ps1

$ErrorActionPreference = "Continue"
$root = "D:\repos2026\SiNetProjectManager_GitHub"

Write-Host "`n=== 1. csproj <Version> (source of truth) ===" -ForegroundColor Cyan
$csproj = "$root\SiOffice.AccService\SiOffice.AccService.csproj"
if (Test-Path $csproj) {
    [xml]$xml = Get-Content $csproj
    $csprojVersion = ($xml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
    Write-Host "csproj Version: $csprojVersion" -ForegroundColor Yellow
} else {
    Write-Warning "csproj not found at $csproj"
}

Write-Host "`n=== 2. DLL in publish folder ===" -ForegroundColor Cyan
$publishDll = "$root\artifacts\AccService_Publish\SiOffice.AccService.dll"
if (Test-Path $publishDll) {
    $dll = Get-Item $publishDll
    Write-Host "  Path:           $($dll.FullName)"
    Write-Host "  LastWriteTime:  $($dll.LastWriteTime)"
    Write-Host "  FileVersion:    $($dll.VersionInfo.FileVersion)" -ForegroundColor Yellow
    Write-Host "  ProductVersion: $($dll.VersionInfo.ProductVersion)" -ForegroundColor Yellow
} else {
    Write-Warning "Publish DLL not found - run publish-service.ps1 first."
}

Write-Host "`n=== 3. MSI in installer output (local) ===" -ForegroundColor Cyan
$msi = "$root\SiOffice.AccService.Installer\bin\Release\SiOfficeAccService.msi"
if (Test-Path $msi) {
    $msiItem = Get-Item $msi
    Write-Host "  Path:          $($msiItem.FullName)"
    Write-Host "  LastWriteTime: $($msiItem.LastWriteTime)"
    Write-Host "  Length:        $($msiItem.Length) bytes"

    try {
        $wi  = New-Object -ComObject WindowsInstaller.Installer
        $db  = $wi.GetType().InvokeMember("OpenDatabase","InvokeMethod",$null,$wi,@($msi,0))
        $vw  = $db.GetType().InvokeMember("OpenView","InvokeMethod",$null,$db,@("SELECT Value FROM Property WHERE Property='ProductVersion'"))
        $vw.GetType().InvokeMember("Execute","InvokeMethod",$null,$vw,$null) | Out-Null
        $rec = $vw.GetType().InvokeMember("Fetch","InvokeMethod",$null,$vw,$null)
        $msiVersion = $rec.GetType().InvokeMember("StringData","GetProperty",$null,$rec,1)
        Write-Host "  MSI ProductVersion: $msiVersion" -ForegroundColor Yellow
    } catch {
        Write-Warning "Could not read MSI ProductVersion: $_"
    }
} else {
    Write-Warning "MSI not found - run publish-service.ps1 first."
}

Write-Host "`n=== 4. MSI on network share ===" -ForegroundColor Cyan
$share = "\\SI-WIN-2K19\AppFolder\AppNet\SiProjecNet2026-Full\SiOfficeAccService.msi"
if (Test-Path $share) {
    $shareItem = Get-Item $share
    Write-Host "  LastWriteTime: $($shareItem.LastWriteTime)"
    Write-Host "  Length:        $($shareItem.Length) bytes"
} else {
    Write-Warning "Share not reachable: $share"
}

Write-Host "`n=== Summary ===" -ForegroundColor Green
Write-Host "csproj:        $csprojVersion"
if ($dll)       { Write-Host "publish DLL:   $($dll.VersionInfo.FileVersion)" }
if ($msiVersion){ Write-Host "MSI:           $msiVersion" }
Write-Host ""
Write-Host "All three should match. If not, the bottleneck is between the row above and below."
