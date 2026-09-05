# =============================================================================
#  Export-AccAutodeskToken-ToShare.ps1
#  WORKSTATION: AuthOnce into dedicated AccService store, verify Autodesk identity,
#  then copy refresh_token.json + non-secret metadata to the Server drop folder.
#  Double-click: Export-AccAutodeskToken-ToShare.cmd  (NOT the .ps1)
#  IMPORTANT: ASCII only (Windows PowerShell 5.1).
# =============================================================================

param(
    [string]$SourceToken = "",
    [string]$DropDir = "\\SI-WIN-2K19\AppFolder\AppNet\Server\AutodeskTokenDrop",
    [string]$AuthOnceExe = "",
    [string]$ExpectedAdminEmail = "siad@si-eng.co.il",
    [int]$MaxTokenAgeMinutes = 30,
    [int]$AuthWaitSeconds = 600,
    [switch]$SkipCreate,
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$script:ExitCode = 1
$logFile = Join-Path $env:TEMP ("SiNet-Export-AccToken-{0}.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))

function Write-Banner([string]$Title, [ConsoleColor]$Color = [ConsoleColor]::Cyan) {
    Write-Host ""
    Write-Host "================================================================" -ForegroundColor $Color
    Write-Host ("  {0}" -f $Title) -ForegroundColor $Color
    Write-Host "================================================================" -ForegroundColor $Color
}

function Write-Log([string]$Message) {
    $line = "{0}  {1}" -f (Get-Date -Format "HH:mm:ss"), $Message
    Write-Host $line
    try { Add-Content -Path $script:logFile -Value $line -Encoding ASCII } catch { }
}

function Resolve-AuthOncePath {
    param([string]$Preferred, [string]$KitRoot)
    $candidates = @(
        $Preferred,
        (Join-Path $KitRoot "SiOffice.AccService.AuthOnce.exe"),
        "\\SI-WIN-2K19\AppFolder\AppNet\Server\SiOffice.AccService.AuthOnce.exe",
        "C:\AccService\AuthOnce\SiOffice.AccService.AuthOnce.exe"
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    foreach ($c in $candidates) {
        if (Test-Path -LiteralPath $c) { return $c }
    }
    return $null
}

function Test-IsDedicatedAccServiceTokenPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    $full = [System.IO.Path]::GetFullPath($Path)
    if (-not ($full -match '(?i)[\\/]SiNet[\\/]Autodesk[\\/]AccService[\\/]refresh_token\.json$')) {
        return $false
    }
    return $true
}

function Test-IsGenericDesktopTokenPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    $full = [System.IO.Path]::GetFullPath($Path)
    if ($full -match '(?i)[\\/]SiNet[\\/]Autodesk[\\/]AccService[\\/]') { return $false }
    return ($full -match '(?i)[\\/]SiNet[\\/]Autodesk[\\/]refresh_token\.json$')
}

function Read-IdentityMap([string]$Path) {
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

function Pause-End {
    Write-Host ""
    Write-Host ("Log file: {0}" -f $script:logFile) -ForegroundColor DarkCyan
    Write-Host "Press Enter to close this window..." -ForegroundColor Yellow
    try { [void](Read-Host) } catch { Start-Sleep -Seconds 8 }
}

try {
    try { Start-Transcript -Path $logFile -Force | Out-Null } catch { }

    $kitRoot = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
    $dedicatedDefault = Join-Path $env:LOCALAPPDATA "SiNet\Autodesk\AccService\refresh_token.json"
    $desktopForbidden = Join-Path $env:LOCALAPPDATA "SiNet\Autodesk\refresh_token.json"

    if (-not $SourceToken) {
        $SourceToken = $dedicatedDefault
    }

    Write-Banner "Export AccService Admin token to Server drop"
    Write-Log ("KitRoot           : {0}" -f $kitRoot)
    Write-Log ("Source token      : {0}" -f $SourceToken)
    Write-Log ("Drop folder       : {0}" -f $DropDir)
    Write-Log ("Expected Admin    : {0}" -f $ExpectedAdminEmail)
    Write-Log ("Windows user      : {0}\{1}" -f $env:USERDOMAIN, $env:USERNAME)
    Write-Log ("SkipCreate        : {0}" -f [bool]$SkipCreate)

    if (Test-IsGenericDesktopTokenPath $SourceToken) {
        Write-Banner "RESULT: FAILED - desktop UserContext token is not exportable" Red
        Write-Log ("Refused generic desktop path: {0}" -f $SourceToken)
        Write-Host "Export ONLY the dedicated AccService store:" -ForegroundColor Yellow
        Write-Host ("  {0}" -f $dedicatedDefault)
        $script:ExitCode = 10
        return
    }

    if (-not (Test-IsDedicatedAccServiceTokenPath $SourceToken)) {
        Write-Banner "RESULT: FAILED - source is not the AccService token store" Red
        Write-Log ("Refused path: {0}" -f $SourceToken)
        Write-Host "Required pattern: ...\SiNet\Autodesk\AccService\refresh_token.json" -ForegroundColor Yellow
        $script:ExitCode = 10
        return
    }

    if ([string]::Equals(
            [System.IO.Path]::GetFullPath($SourceToken),
            [System.IO.Path]::GetFullPath($desktopForbidden),
            [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-Banner "RESULT: FAILED - refused desktop token path" Red
        $script:ExitCode = 10
        return
    }

    $tokenDir = Split-Path $SourceToken -Parent
    $identitySidecar = Join-Path $tokenDir "token_identity.txt"

    Write-Host ""
    Write-Host "Sign in to Autodesk as AccBootstrapAdminEmail (steady-state: siad@si-eng.co.il)." -ForegroundColor Yellow
    Write-Host ""

    $needCreate = -not $SkipCreate
    if ($SkipCreate -and -not (Test-Path -LiteralPath $SourceToken)) {
        Write-Banner "RESULT: FAILED - no AccService token and -SkipCreate was set" Red
        Write-Log ("Missing dedicated token: {0}" -f $SourceToken)
        Write-Host "Desktop token presence is irrelevant." -ForegroundColor Yellow
        $script:ExitCode = 1
        return
    }

    if (-not $SkipCreate -and (Test-Path -LiteralPath $SourceToken) -and -not $Force) {
        $existing = Get-Item -LiteralPath $SourceToken
        $ageMin = ([datetime]::Now - $existing.LastWriteTime).TotalMinutes
        Write-Log ("Existing token LastWriteTime : {0}" -f $existing.LastWriteTime)
        Write-Log ("Existing token age (minutes) : {0:N0}" -f $ageMin)
        if ($ageMin -le $MaxTokenAgeMinutes) {
            Write-Host ("Token is newer than {0} minutes." -f $MaxTokenAgeMinutes) -ForegroundColor Green
            $ans = Read-Host "Create a brand-new Autodesk login anyway? [y/N]"
            if ($ans -notmatch '^(y|yes)$') {
                $needCreate = $false
                Write-Log "Using the existing AccService token (will still verify identity)."
            }
        }
        else {
            Write-Host "Existing AccService token is OLD. A new Autodesk browser login is required." -ForegroundColor Yellow
            $ans = Read-Host "Start new Autodesk token login now? [Y/n]"
            if ($ans -match '^(n|no)$') {
                Write-Log "Cancelled by user."
                $script:ExitCode = 2
                return
            }
            $needCreate = $true
        }
    }

    $authOnceSrc = Resolve-AuthOncePath -Preferred $AuthOnceExe -KitRoot $kitRoot
    if (-not $authOnceSrc) {
        Write-Banner "RESULT: FAILED - AuthOnce.exe not found" Red
        Write-Log "Expected SiOffice.AccService.AuthOnce.exe next to this script or under Server\"
        $script:ExitCode = 4
        return
    }

    $localAuthDir = Join-Path $env:TEMP "SiNetAuthOnce"
    New-Item -ItemType Directory -Path $localAuthDir -Force | Out-Null
    $authOnce = Join-Path $localAuthDir "SiOffice.AccService.AuthOnce.exe"
    Copy-Item -LiteralPath $authOnceSrc -Destination $authOnce -Force
    Write-Log ("AuthOnce local copy: {0}" -f $authOnce)

    if ($needCreate) {
        & netsh http add urlacl url=http://localhost:8080/ user=Everyone 2>$null | Out-Null
        New-Item -ItemType Directory -Path $tokenDir -Force | Out-Null
        if (Test-Path -LiteralPath $SourceToken) {
            $bak = Join-Path $tokenDir ("refresh_token.backup.{0}.json" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
            Copy-Item -LiteralPath $SourceToken -Destination $bak -Force
            Remove-Item -LiteralPath $SourceToken -Force
            Write-Log ("Old AccService token backed up to: {0}" -f $bak)
        }
        if (Test-Path -LiteralPath $identitySidecar) {
            Remove-Item -LiteralPath $identitySidecar -Force
        }

        Write-Banner "STEP: Autodesk browser login (AccService store only)"
        Write-Host "1) AuthOnce console will open."
        Write-Host "2) Browser opens - sign in as AccBootstrapAdminEmail."
        Write-Host "3) When AuthOnce prints OK (identity MATCH), press Enter in THAT window."
        Write-Host ""
        Read-Host "Press Enter here to launch AuthOnce"

        $startedUtc = [datetime]::UtcNow.AddSeconds(-5)
        $okMarker = Join-Path $tokenDir "auth_once_last_ok.txt"
        $authArgs = @("--force", "--no-pause", ("--expected-email={0}" -f $ExpectedAdminEmail))
        Write-Log ("Starting AuthOnce {0} ..." -f ($authArgs -join ' '))
        $proc = Start-Process -FilePath $authOnce -ArgumentList $authArgs -WorkingDirectory $localAuthDir -PassThru
        if (-not $proc) { throw "Start-Process returned nothing for AuthOnce." }
        if (-not $proc.WaitForExit($AuthWaitSeconds * 1000)) {
            try { $proc.Kill() } catch { }
            Write-Banner "RESULT: FAILED - AuthOnce timed out" Red
            $script:ExitCode = 5
            return
        }
        Write-Log ("AuthOnce exit code: {0}" -f $proc.ExitCode)
        if ($proc.ExitCode -ne 0) {
            Write-Banner "RESULT: FAILED - AuthOnce identity/auth failed" Red
            Write-Log "Wrong Autodesk user or auth failure - token will NOT be exported."
            $script:ExitCode = 6
            return
        }
        if (-not (Test-Path -LiteralPath $SourceToken)) {
            Write-Banner "RESULT: FAILED - no AccService token was created" Red
            $script:ExitCode = 6
            return
        }
        $tokenTime = (Get-Item -LiteralPath $SourceToken).LastWriteTimeUtc
        if ($tokenTime -lt $startedUtc) {
            Write-Banner "RESULT: FAILED - AccService token was not refreshed" Red
            $script:ExitCode = 6
            return
        }
        Write-Host "New AccService token created." -ForegroundColor Green
    }
    else {
        # Re-verify existing dedicated token before export (no browser if refresh still works).
        Write-Banner "STEP: Verify existing AccService token identity"
        $verifyArgs = @("--verify", "--no-pause", ("--expected-email={0}" -f $ExpectedAdminEmail))
        Write-Log ("Starting AuthOnce {0} ..." -f ($verifyArgs -join ' '))
        $proc = Start-Process -FilePath $authOnce -ArgumentList $verifyArgs -WorkingDirectory $localAuthDir -PassThru -Wait
        Write-Log ("AuthOnce verify exit code: {0}" -f $proc.ExitCode)
        if ($proc.ExitCode -ne 0) {
            Write-Banner "RESULT: FAILED - AccService token identity verification failed" Red
            Write-Host "Token will NOT be published to the server drop." -ForegroundColor Yellow
            $script:ExitCode = 7
            return
        }
    }

    if (-not (Test-Path -LiteralPath $SourceToken)) {
        Write-Banner "RESULT: FAILED - AccService token missing" Red
        Write-Log ("Missing: {0}" -f $SourceToken)
        if (Test-Path -LiteralPath $desktopForbidden) {
            Write-Log ("Desktop token exists at {0} but is NOT a valid export source." -f $desktopForbidden)
        }
        $script:ExitCode = 1
        return
    }

    $idMap = Read-IdentityMap $identitySidecar
    $actual = $idMap["ActualAdminEmail"]
    $expected = $idMap["ExpectedAdminEmail"]
    $purpose = $idMap["TokenPurpose"]
    $userId = $idMap["AutodeskUserId"]

    if (-not $purpose) { $purpose = "AccServiceAdmin" }
    if (-not $expected) { $expected = $ExpectedAdminEmail }

    if ($purpose -ne "AccServiceAdmin") {
        Write-Banner "RESULT: FAILED - TokenPurpose is not AccServiceAdmin" Red
        $script:ExitCode = 7
        return
    }
    if ([string]::IsNullOrWhiteSpace($actual)) {
        Write-Banner "RESULT: FAILED - ActualAdminEmail missing from identity sidecar" Red
        Write-Log ("Expected sidecar: {0}" -f $identitySidecar)
        $script:ExitCode = 7
        return
    }
    if (-not [string]::Equals($actual.Trim(), $ExpectedAdminEmail.Trim(), [StringComparison]::OrdinalIgnoreCase)) {
        Write-Banner "RESULT: FAILED - ActualAdminEmail does not match AccBootstrapAdminEmail" Red
        Write-Log ("Expected: {0}" -f $ExpectedAdminEmail)
        Write-Log ("Actual:   {0}" -f $actual)
        Write-Host "Wrong Autodesk account - token will NOT be exported." -ForegroundColor Yellow
        $script:ExitCode = 7
        return
    }

    $src = Get-Item -LiteralPath $SourceToken
    Write-Log ("Token size          : {0} bytes" -f $src.Length)
    Write-Log ("Token LastWriteTime : {0}" -f $src.LastWriteTime)
    Write-Log ("ActualAdminEmail    : {0}" -f $actual)

    New-Item -ItemType Directory -Path $DropDir -Force | Out-Null
    $destToken = Join-Path $DropDir "refresh_token.json"
    $destMeta = Join-Path $DropDir "export_meta.txt"

    Copy-Item -LiteralPath $SourceToken -Destination $destToken -Force

    $metaLines = New-Object System.Collections.Generic.List[string]
    [void]$metaLines.Add("TokenPurpose=AccServiceAdmin")
    [void]$metaLines.Add(("ExpectedAdminEmail={0}" -f $ExpectedAdminEmail.Trim()))
    [void]$metaLines.Add(("ActualAdminEmail={0}" -f $actual.Trim()))
    if (-not [string]::IsNullOrWhiteSpace($userId)) {
        [void]$metaLines.Add(("AutodeskUserId={0}" -f $userId))
    }
    [void]$metaLines.Add(("ExportedUtc={0}" -f [datetime]::UtcNow.ToString("o")))
    [void]$metaLines.Add(("SourceMachine={0}" -f $env:COMPUTERNAME))
    [void]$metaLines.Add(("SourcePath={0}" -f $src.FullName))
    [void]$metaLines.Add(("SourceLastWriteTime={0}" -f $src.LastWriteTime.ToString("o")))
    [void]$metaLines.Add(("SourceLength={0}" -f $src.Length))
    [void]$metaLines.Add(("CreatedInThisRun={0}" -f [bool]$needCreate))
    [void]$metaLines.Add(("LogFile={0}" -f $script:logFile))
    [System.IO.File]::WriteAllLines($destMeta, $metaLines.ToArray(), [System.Text.Encoding]::ASCII)

    try {
        Copy-Item -LiteralPath $script:logFile -Destination (Join-Path $DropDir "last-export.log") -Force
    } catch { }

    Write-Banner "RESULT: SUCCESS - validated AccService token dropped" Green
    Write-Log ("Drop file : {0}" -f $destToken)
    Write-Log ("Meta file : {0}" -f $destMeta)
    Write-Host ""
    Write-Host "NEXT: on the SERVER (SI-WIN-2K19), run:" -ForegroundColor Cyan
    Write-Host "  Install-AccAutodeskToken-FromShare.cmd"
    $script:ExitCode = 0
}
catch {
    Write-Banner "RESULT: FAILED - script error" Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Log ("ERROR: {0}" -f $_.Exception.Message)
    if ($_.ScriptStackTrace) { Write-Log $_.ScriptStackTrace }
    $script:ExitCode = 99
}
finally {
    try { Stop-Transcript | Out-Null } catch { }
    Pause-End
    exit $script:ExitCode
}
