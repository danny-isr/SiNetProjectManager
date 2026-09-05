# =============================================================================
#  Install-AccAutodeskToken-FromShare.ps1
#  SERVER (elevated): install drop\refresh_token.json into the Windows service
#  account's dedicated AccService store, restart SiOfficeAccService, verify
#  /v1/acc/admin-identity, then DELETE the drop refresh_token.json (keep meta).
#  Destination: ...\SiNet\Autodesk\AccService\refresh_token.json
#  Double-click: Install-AccAutodeskToken-FromShare.cmd
#  IMPORTANT: ASCII only (Windows PowerShell 5.1).
# =============================================================================

param(
    [string]$DropDir = "",
    [string]$ServiceUser = "",
    [string]$ServiceName = "SiOfficeAccService",
    [string]$ExpectedAdminEmail = "",
    [string]$AccServiceBaseUrl = "https://localhost:8443",
    [switch]$Force,
    [switch]$KeepDropFile
)

$ErrorActionPreference = "Stop"

function Write-Banner([string]$Title, [ConsoleColor]$Color = [ConsoleColor]::Cyan) {
    Write-Host ""
    Write-Host "================================================================" -ForegroundColor $Color
    Write-Host ("  {0}" -f $Title) -ForegroundColor $Color
    Write-Host "================================================================" -ForegroundColor $Color
}

function Read-MetaMap([string]$Path) {
    $map = @{}
    if (-not (Test-Path -LiteralPath $Path)) { return $map }
    Get-Content -LiteralPath $Path | ForEach-Object {
        $line = $_.Trim()
        if ($line -and ($line.IndexOf('=') -gt 0)) {
            $k = $line.Substring(0, $line.IndexOf('=')).Trim()
            $v = $line.Substring($line.IndexOf('=') + 1).Trim()
            $map[$k] = $v
        }
    }
    return $map
}

function Get-SiNetVaultSecret([string]$Target) {
    if (-not ("SiNetVaultHelperInstall" -as [type])) {
        Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class SiNetVaultHelperInstall {
  [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
  static extern bool CredRead(string target, uint type, uint flags, out IntPtr credPtr);
  [DllImport("advapi32.dll")] static extern void CredFree(IntPtr cred);
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
  struct CREDENTIAL {
    public uint Flags; public uint Type; public string TargetName; public string Comment;
    public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
    public uint CredentialBlobSize; public IntPtr CredentialBlob;
    public uint Persist; public uint AttributeCount; public IntPtr Attributes;
    public string TargetAlias; public string UserName;
  }
  public static string Get(string target) {
    IntPtr p;
    if (!CredRead(target, 1, 0, out p)) return null;
    try {
      var c = (CREDENTIAL)Marshal.PtrToStructure(p, typeof(CREDENTIAL));
      if (c.CredentialBlobSize == 0 || c.CredentialBlob == IntPtr.Zero) return null;
      var b = new byte[c.CredentialBlobSize];
      Marshal.Copy(c.CredentialBlob, b, 0, (int)c.CredentialBlobSize);
      return Encoding.UTF8.GetString(b);
    } finally { CredFree(p); }
  }
}
"@
    }
    return [SiNetVaultHelperInstall]::Get($Target)
}

function Get-AccBootstrapAdminEmailFromDb {
    $cs = Get-SiNetVaultSecret "SiNet/ConnectionStrings/SiNetDatabase"
    if ([string]::IsNullOrWhiteSpace($cs)) {
        throw "Vault key SiNet/ConnectionStrings/SiNetDatabase missing; cannot read AccBootstrapAdminEmail."
    }
    $conn = New-Object System.Data.SqlClient.SqlConnection $cs
    try {
        $conn.Open()
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = "SELECT TOP (1) SettingValue FROM dbo.SystemSettings WHERE SettingKey = @k"
        [void]$cmd.Parameters.AddWithValue("@k", "AccBootstrapAdminEmail")
        $val = [string]$cmd.ExecuteScalar()
        if ([string]::IsNullOrWhiteSpace($val)) {
            throw "dbo.SystemSettings.AccBootstrapAdminEmail is missing or empty."
        }
        return $val.Trim()
    }
    finally { $conn.Dispose() }
}

function Resolve-ServiceAccount([string]$ServiceName, [string]$FallbackUser) {
    try {
        $cim = Get-CimInstance -ClassName Win32_Service -Filter ("Name='{0}'" -f $ServiceName.Replace("'", "''")) -ErrorAction Stop
        if ($cim -and -not [string]::IsNullOrWhiteSpace($cim.StartName)) {
            $startName = $cim.StartName.Trim()
            if ($startName -match '^(LocalSystem|NT AUTHORITY\\LocalService|NT AUTHORITY\\NetworkService)$') {
                Write-Host ("WARNING: service StartName={0}; falling back to {1}" -f $startName, $FallbackUser) -ForegroundColor Yellow
                return $FallbackUser
            }
            return $startName
        }
    }
    catch {
        Write-Host ("WARNING: could not read service account ({0}); using fallback {1}" -f $_.Exception.Message, $FallbackUser) -ForegroundColor Yellow
    }
    return $FallbackUser
}

function Invoke-AccAdminIdentityProof([string]$BaseUrl, [string]$ApiKey) {
    # Returns hashtable of JSON fields, or throws.
    Add-Type -AssemblyName System.Net.Http | Out-Null
    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.ServerCertificateCustomValidationCallback = { $true }
    $client = New-Object System.Net.Http.HttpClient($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(30)
    try {
        $url = ($BaseUrl.TrimEnd('/') + "/v1/acc/admin-identity")
        $req = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Get, $url)
        [void]$req.Headers.TryAddWithoutValidation("X-AccService-Key", $ApiKey)
        $resp = $client.SendAsync($req).GetAwaiter().GetResult()
        $body = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if (-not $resp.IsSuccessStatusCode) {
            throw ("admin-identity HTTP {0}: {1}" -f [int]$resp.StatusCode, $body)
        }
        return $body | ConvertFrom-Json
    }
    finally {
        $client.Dispose()
        $handler.Dispose()
    }
}

$kitRoot = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
if (-not $DropDir) {
    $DropDir = Join-Path $kitRoot "AutodeskTokenDrop"
}

$dropToken = Join-Path $DropDir "refresh_token.json"
$dropMeta = Join-Path $DropDir "export_meta.txt"
$fallbackUser = if ($ServiceUser) { $ServiceUser } else { "SI-ENG\sieng" }

Write-Banner "Install AccService Admin Autodesk token"
Write-Host ("  Drop dir            : {0}" -f $DropDir)
Write-Host ("  Service name        : {0}" -f $ServiceName)
Write-Host ("  AccService URL      : {0}" -f $AccServiceBaseUrl)
Write-Host ("  Force               : {0}" -f $Force)
Write-Host ""

$isAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "Run elevated (Administrator). Use Install-AccAutodeskToken-FromShare.cmd."
}

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $svc) {
    throw ("Windows service '{0}' not found. Run this on SI-WIN-2K19." -f $ServiceName)
}

$dbExpected = Get-AccBootstrapAdminEmailFromDb
if (-not [string]::IsNullOrWhiteSpace($ExpectedAdminEmail) `
        -and -not [string]::Equals($ExpectedAdminEmail.Trim(), $dbExpected, [StringComparison]::OrdinalIgnoreCase)) {
    Write-Banner "RESULT: FAILED - CLI ExpectedAdminEmail != DB AccBootstrapAdminEmail" Red
    Write-Host ("DB (canonical): {0}" -f $dbExpected)
    Write-Host ("CLI override:   {0}" -f $ExpectedAdminEmail)
    exit 4
}
$ExpectedAdminEmail = $dbExpected
Write-Host ("  ExpectedAdminEmail  : {0} (dbo.SystemSettings.AccBootstrapAdminEmail)" -f $ExpectedAdminEmail)

$resolvedUser = Resolve-ServiceAccount -ServiceName $ServiceName -FallbackUser $fallbackUser
Write-Host ("  Service account     : {0}" -f $resolvedUser)

if (-not (Test-Path $dropToken)) {
    Write-Banner "RESULT: FAILED - no new token in drop folder" Red
    Write-Host ("Missing: {0}" -f $dropToken)
    Write-Host ""
    Write-Host "On the workstation, run Export-AccAutodeskToken-ToShare.cmd first." -ForegroundColor Yellow
    Write-Host "That exports %LOCALAPPDATA%\SiNet\Autodesk\AccService\refresh_token.json only." -ForegroundColor Yellow
    exit 1
}

if (-not (Test-Path $dropMeta)) {
    Write-Banner "RESULT: FAILED - export_meta.txt missing" Red
    Write-Host "Refuse install without non-secret package metadata (TokenPurpose / ActualAdminEmail)."
    exit 2
}

$meta = Read-MetaMap $dropMeta
Write-Host "--- export_meta.txt ---" -ForegroundColor DarkCyan
Get-Content $dropMeta | ForEach-Object { Write-Host ("  {0}" -f $_) }
Write-Host "-----------------------" -ForegroundColor DarkCyan

if ($meta["TokenPurpose"] -ne "AccServiceAdmin") {
    Write-Banner "RESULT: FAILED - TokenPurpose is not AccServiceAdmin" Red
    exit 7
}
$actual = $meta["ActualAdminEmail"]
$expectedMeta = $meta["ExpectedAdminEmail"]
if ([string]::IsNullOrWhiteSpace($actual)) {
    Write-Banner "RESULT: FAILED - ActualAdminEmail missing from metadata" Red
    exit 7
}
if (-not [string]::Equals($actual.Trim(), $ExpectedAdminEmail.Trim(), [StringComparison]::OrdinalIgnoreCase)) {
    Write-Banner "RESULT: FAILED - package ActualAdminEmail != AccBootstrapAdminEmail (DB)" Red
    Write-Host ("Configured/expected : {0}" -f $ExpectedAdminEmail)
    Write-Host ("Package actual      : {0}" -f $actual)
    Write-Host "STOP - will not install or restart AccService with the wrong credential." -ForegroundColor Yellow
    exit 7
}
if ($expectedMeta -and -not [string]::Equals($actual.Trim(), $expectedMeta.Trim(), [StringComparison]::OrdinalIgnoreCase)) {
    Write-Banner "RESULT: FAILED - package ActualAdminEmail != package ExpectedAdminEmail" Red
    exit 7
}
if ($expectedMeta -and -not [string]::Equals($expectedMeta.Trim(), $ExpectedAdminEmail.Trim(), [StringComparison]::OrdinalIgnoreCase)) {
    Write-Banner "RESULT: FAILED - package ExpectedAdminEmail != DB AccBootstrapAdminEmail" Red
    Write-Host ("Package expected : {0}" -f $expectedMeta)
    Write-Host ("DB expected      : {0}" -f $ExpectedAdminEmail)
    exit 7
}

$dropItem = Get-Item $dropToken
Write-Host ("Drop file LastWriteTime : {0}" -f $dropItem.LastWriteTime)
Write-Host ("Drop file Length        : {0}" -f $dropItem.Length)

$leaf = ($resolvedUser -split '\\')[-1]
if ($leaf -match '\$$') {
    $leaf = $leaf.TrimEnd('$')
}
$tokenDir = Join-Path $env:SystemDrive ("Users\{0}\AppData\Local\SiNet\Autodesk\AccService" -f $leaf)
$targetToken = Join-Path $tokenDir "refresh_token.json"
$desktopPath = Join-Path $env:SystemDrive ("Users\{0}\AppData\Local\SiNet\Autodesk\refresh_token.json" -f $leaf)

Write-Host ("  Install target       : {0}" -f $targetToken)
Write-Host ("  Desktop path (untouched): {0}" -f $desktopPath)

if ((Test-Path $targetToken) -and -not $Force) {
    $installed = Get-Item $targetToken
    Write-Host ("Installed LastWriteTime : {0}" -f $installed.LastWriteTime)
    if ($dropItem.LastWriteTime -le $installed.LastWriteTime) {
        Write-Banner "RESULT: FAILED - drop file is not newer" Red
        Write-Host "Export a FRESH validated AccService token from the workstation,"
        Write-Host "or re-run this installer with -Force to overwrite anyway."
        exit 3
    }
}

$desktopBefore = $null
if (Test-Path $desktopPath) {
    $desktopBefore = (Get-FileHash -LiteralPath $desktopPath -Algorithm SHA256).Hash
}

New-Item -ItemType Directory -Path $tokenDir -Force | Out-Null
Copy-Item $dropToken $targetToken -Force

try {
    $acl = Get-Acl $targetToken
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        $resolvedUser, "ReadAndExecute", "Allow")
    $acl.SetAccessRule($rule)
    Set-Acl -Path $targetToken -AclObject $acl
}
catch {
    Write-Host ("WARNING: could not set ACL on token: {0}" -f $_.Exception.Message) -ForegroundColor Yellow
}

if ($desktopBefore -and (Test-Path $desktopPath)) {
    $desktopAfter = (Get-FileHash -LiteralPath $desktopPath -Algorithm SHA256).Hash
    if ($desktopBefore -ne $desktopAfter) {
        Write-Banner "RESULT: FAILED - desktop UserContext token was modified (unexpected)" Red
        exit 11
    }
}

$installedNow = Get-Item $targetToken
Write-Host ("Installed to: {0}" -f $installedNow.FullName) -ForegroundColor Green
Write-Host ("Length      : {0}" -f $installedNow.Length)
Write-Host ("LastWrite   : {0}" -f $installedNow.LastWriteTime)

Write-Host ""
Write-Host ("--- Restarting {0} ---" -f $ServiceName) -ForegroundColor Cyan
Restart-Service -Name $ServiceName -Force -ErrorAction Stop
(Get-Service -Name $ServiceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(60))
Get-Service -Name $ServiceName | Format-Table Name, Status, StartType -AutoSize

Write-Banner "STEP: Runtime proof GET /v1/acc/admin-identity"
Start-Sleep -Seconds 3
$apiKey = Get-SiNetVaultSecret "SiNet/AccService/ApiKey"
if ([string]::IsNullOrWhiteSpace($apiKey)) {
    Write-Banner "RESULT: FAILED - AccService API key missing from vault; drop token NOT deleted" Red
    Write-Host "Installed token left in place for controlled recovery."
    Write-Host ("Drop token still at: {0}" -f $dropToken)
    exit 8
}

try {
    $proof = Invoke-AccAdminIdentityProof -BaseUrl $AccServiceBaseUrl -ApiKey $apiKey
}
catch {
    Write-Banner "RESULT: FAILED - runtime admin-identity verification failed; drop token NOT deleted" Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host "Installed token left in place for controlled recovery."
    Write-Host ("Drop token still at: {0}" -f $dropToken)
    exit 8
}

Write-Host ("  TokenPurpose     : {0}" -f $proof.tokenPurpose)
Write-Host ("  TokenStoragePath : {0}" -f $proof.tokenStoragePath)
Write-Host ("  TokenExists      : {0}" -f $proof.tokenExists)
Write-Host ("  ExpectedAdmin    : {0}" -f $proof.expectedAdminEmail)
Write-Host ("  ActualAdmin      : {0}" -f $proof.actualAdminEmail)
Write-Host ("  EmailMatch       : {0}" -f $proof.emailMatch)
Write-Host ("  Status           : {0}" -f $proof.status)
Write-Host ("  AdminApiStatus   : {0}" -f $proof.adminApiStatus)

$storeOk = ($proof.tokenPurpose -eq "AccServiceAdmin") `
    -and ($proof.tokenStoragePath -match '(?i)[\\/]Autodesk[\\/]AccService[\\/]refresh_token\.json$') `
    -and ([bool]$proof.tokenExists)
$emailOk = [bool]$proof.emailMatch `
    -and [string]::Equals([string]$proof.actualAdminEmail, $ExpectedAdminEmail, [StringComparison]::OrdinalIgnoreCase)
$apiOk = ([string]$proof.adminApiStatus -eq "200") -or ([string]$proof.adminApiStatus -eq "OK")
$statusOk = ([string]$proof.status -eq "Healthy")

if (-not ($storeOk -and $emailOk -and $apiOk -and $statusOk)) {
    Write-Banner "RESULT: FAILED - runtime proof did not pass; drop token NOT deleted" Red
    Write-Host "Installed token left in place for controlled recovery."
    Write-Host ("Drop token still at: {0}" -f $dropToken)
    exit 8
}

if (-not $KeepDropFile) {
    # Secure cleanup: delete live refresh token from the share (do NOT leave under used\).
    if (Test-Path -LiteralPath $dropToken) {
        Remove-Item -LiteralPath $dropToken -Force
        Write-Host ("Deleted drop refresh_token.json: {0}" -f $dropToken) -ForegroundColor DarkCyan
    }
    # Keep export_meta.txt for audit (optionally stamp into used\).
    if (Test-Path -LiteralPath $dropMeta) {
        $usedDir = Join-Path $DropDir "used"
        New-Item -ItemType Directory -Path $usedDir -Force | Out-Null
        $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $metaDest = Join-Path $usedDir ("export_meta.{0}.txt" -f $stamp)
        Move-Item -LiteralPath $dropMeta -Destination $metaDest -Force
        Write-Host ("Archived export_meta.txt to: {0}" -f $metaDest) -ForegroundColor DarkCyan
    }
}

Write-Banner "RESULT: SUCCESS - AccService Admin token installed and verified" Green
Write-Host "Runtime /v1/acc/admin-identity is Healthy (store + identity + Admin API 200)."
Write-Host "Drop refresh_token.json removed after verified install."
exit 0
