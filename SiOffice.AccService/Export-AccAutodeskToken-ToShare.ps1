# =============================================================================
#  Export-AccAutodeskToken-ToShare.ps1
#  WORKSTATION: force a NEW Autodesk browser login (AuthOnce), then copy
#  refresh_token.json to the Server drop folder.
#  Double-click: Export-AccAutodeskToken-ToShare.cmd  (NOT the .ps1)
#  IMPORTANT: ASCII only (Windows PowerShell 5.1).
# =============================================================================

param(
    [string]$SourceToken = "",
    [string]$DropDir = "\\SI-WIN-2K19\AppFolder\AppNet\Server\AutodeskTokenDrop",
    [string]$AuthOnceExe = "",
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

function Pause-End {
    Write-Host ""
    Write-Host ("Log file: {0}" -f $script:logFile) -ForegroundColor DarkCyan
    Write-Host "Press Enter to close this window..." -ForegroundColor Yellow
    try { [void](Read-Host) } catch { Start-Sleep -Seconds 8 }
}

try {
    try { Start-Transcript -Path $logFile -Force | Out-Null } catch { }

    $kitRoot = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
    if (-not $SourceToken) {
        $SourceToken = Join-Path $env:LOCALAPPDATA "SiNet\Autodesk\refresh_token.json"
    }
    $tokenDir = Split-Path $SourceToken -Parent

    Write-Banner "Export Autodesk refresh token to Server drop"
    Write-Log ("KitRoot      : {0}" -f $kitRoot)
    Write-Log ("Source token : {0}" -f $SourceToken)
    Write-Log ("Drop folder  : {0}" -f $DropDir)
    Write-Log ("Windows user : {0}\{1}" -f $env:USERDOMAIN, $env:USERNAME)
    Write-Log ("SkipCreate   : {0}" -f [bool]$SkipCreate)
    Write-Host ""
    Write-Host "Sign in to Autodesk as the OFFICE ACC Account Admin." -ForegroundColor Yellow
    Write-Host ""

    $needCreate = -not $SkipCreate
    if ($SkipCreate -and -not (Test-Path -LiteralPath $SourceToken)) {
        Write-Banner "RESULT: FAILED - no token and -SkipCreate was set" Red
        Write-Log ("Missing: {0}" -f $SourceToken)
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
                Write-Log "Using the existing fresh token (no new browser login)."
            }
        }
        else {
            Write-Host "Existing token is OLD. A new Autodesk browser login is required." -ForegroundColor Yellow
            Write-Host ""
            $ans = Read-Host "Start new Autodesk token login now? [Y/n]"
            if ($ans -match '^(n|no)$') {
                Write-Log "Cancelled by user."
                $script:ExitCode = 2
                return
            }
            $needCreate = $true
        }
    }

    if ($needCreate) {
        $authOnceSrc = Resolve-AuthOncePath -Preferred $AuthOnceExe -KitRoot $kitRoot
        if (-not $authOnceSrc) {
            Write-Banner "RESULT: FAILED - AuthOnce.exe not found" Red
            Write-Log "Expected SiOffice.AccService.AuthOnce.exe next to this script or under Server\"
            $script:ExitCode = 4
            return
        }
        Write-Log ("AuthOnce source: {0}" -f $authOnceSrc)

        # Never run the exe from UNC (Windows often blocks / flashes and closes).
        $localAuthDir = Join-Path $env:TEMP "SiNetAuthOnce"
        New-Item -ItemType Directory -Path $localAuthDir -Force | Out-Null
        $authOnce = Join-Path $localAuthDir "SiOffice.AccService.AuthOnce.exe"
        Write-Log ("Copying AuthOnce to local: {0}" -f $authOnce)
        Copy-Item -LiteralPath $authOnceSrc -Destination $authOnce -Force

        Write-Log "Ensuring OAuth callback URL ACL (localhost:8080)..."
        & netsh http add urlacl url=http://localhost:8080/ user=Everyone 2>$null | Out-Null

        New-Item -ItemType Directory -Path $tokenDir -Force | Out-Null
        if (Test-Path -LiteralPath $SourceToken) {
            $bak = Join-Path $tokenDir ("refresh_token.backup.{0}.json" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
            Copy-Item -LiteralPath $SourceToken -Destination $bak -Force
            Remove-Item -LiteralPath $SourceToken -Force
            Write-Log ("Old token backed up to: {0}" -f $bak)
            Write-Log "Old token removed so Autodesk must show a new login."
        }

        Write-Banner "STEP: Autodesk browser login"
        Write-Host "1) AuthOnce console will open."
        Write-Host "2) Browser opens - sign in as ACC Account Admin."
        Write-Host "3) When AuthOnce prints OK, press Enter in THAT window."
        Write-Host ""
        Read-Host "Press Enter here to launch AuthOnce"

        $startedUtc = [datetime]::UtcNow.AddSeconds(-5)
        $okMarker = Join-Path $tokenDir "auth_once_last_ok.txt"

        Write-Log "Starting AuthOnce --force ..."
        $proc = Start-Process -FilePath $authOnce -ArgumentList @("--force") -WorkingDirectory $localAuthDir -PassThru
        if (-not $proc) {
            throw "Start-Process returned nothing for AuthOnce."
        }
        Write-Log ("AuthOnce PID {0} - waiting up to {1}s..." -f $proc.Id, $AuthWaitSeconds)
        if (-not $proc.WaitForExit($AuthWaitSeconds * 1000)) {
            try { $proc.Kill() } catch { }
            Write-Banner "RESULT: FAILED - AuthOnce timed out" Red
            $script:ExitCode = 5
            return
        }
        Write-Log ("AuthOnce exit code: {0}" -f $proc.ExitCode)

        $fresh = $false
        if ((Test-Path -LiteralPath $SourceToken) -and (Test-Path -LiteralPath $okMarker)) {
            $tokenTime = (Get-Item -LiteralPath $SourceToken).LastWriteTimeUtc
            $markerTime = (Get-Item -LiteralPath $okMarker).LastWriteTimeUtc
            if ($tokenTime -ge $startedUtc -and $markerTime -ge $startedUtc -and $proc.ExitCode -eq 0) {
                $fresh = $true
            }
        }
        elseif ((Test-Path -LiteralPath $SourceToken) -and $proc.ExitCode -eq 0) {
            $tokenTime = (Get-Item -LiteralPath $SourceToken).LastWriteTimeUtc
            if ($tokenTime -ge $startedUtc) { $fresh = $true }
        }

        if (-not $fresh) {
            Write-Banner "RESULT: FAILED - no new token was created" Red
            Write-Log ("Expected a new file at: {0}" -f $SourceToken)
            Write-Host "Common causes:"
            Write-Host "  - Browser login cancelled / timed out"
            Write-Host "  - Autodesk ClientId/Secret missing in THIS Windows user's Credential Manager"
            Write-Host "  - Port 8080 blocked (OAuth callback)"
            $script:ExitCode = 6
            return
        }

        Write-Host "New token created successfully on this PC." -ForegroundColor Green
        Write-Log "New token OK."
    }

    if (-not (Test-Path -LiteralPath $SourceToken)) {
        Write-Banner "RESULT: FAILED - token file still missing" Red
        Write-Log ("Missing: {0}" -f $SourceToken)
        $script:ExitCode = 1
        return
    }

    $src = Get-Item -LiteralPath $SourceToken
    Write-Log ("Token size          : {0} bytes" -f $src.Length)
    Write-Log ("Token LastWriteTime : {0}" -f $src.LastWriteTime)

    New-Item -ItemType Directory -Path $DropDir -Force | Out-Null
    $destToken = Join-Path $DropDir "refresh_token.json"
    $destMeta = Join-Path $DropDir "export_meta.txt"

    Copy-Item -LiteralPath $SourceToken -Destination $destToken -Force

    $meta = @(
        ("ExportedUtc={0}" -f [datetime]::UtcNow.ToString("o")),
        ("SourceMachine={0}" -f $env:COMPUTERNAME),
        ("SourceUser={0}\{1}" -f $env:USERDOMAIN, $env:USERNAME),
        ("SourcePath={0}" -f $src.FullName),
        ("SourceLastWriteTime={0}" -f $src.LastWriteTime.ToString("o")),
        ("SourceLength={0}" -f $src.Length),
        ("CreatedInThisRun={0}" -f [bool]$needCreate),
        ("LogFile={0}" -f $script:logFile)
    )
    [System.IO.File]::WriteAllLines($destMeta, $meta, [System.Text.Encoding]::ASCII)

    try {
        Copy-Item -LiteralPath $script:logFile -Destination (Join-Path $DropDir "last-export.log") -Force
    } catch { }

    Write-Banner "RESULT: SUCCESS - token dropped on share" Green
    Write-Log ("Drop file : {0}" -f $destToken)
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
