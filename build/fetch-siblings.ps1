<#
.SYNOPSIS
    Materializes the pinned sibling repositories required to build SiNet.sln.

.DESCRIPTION
    SiNet.sln is NOT self-contained: SiNet.Infrastructure.Autodesk and SiNetProjectManagerV2
    reference projects that live in sibling repositories checked out next to this repository.
    This script reads build/sibling-pins.json and brings every sibling to its pinned commit
    at the relative path the .csproj files expect.

    Used by both CI (.github/workflows/ci.yml) and developers preparing a clean machine.

    Authentication (private repositories) is taken from the SIBLING_REPOS_TOKEN environment
    variable only. The token is passed per-invocation via an HTTP auth header and is never
    written to .git/config, the remote URL, or any file on disk.

.PARAMETER PinsFile
    Path to the pins file. Defaults to build/sibling-pins.json next to this script.

.PARAMETER ValidateOnly
    Validate the pins file without contacting any remote. Used as a fast CI pre-check.

.EXAMPLE
    pwsh build/fetch-siblings.ps1

.EXAMPLE
    $env:SIBLING_REPOS_TOKEN = '<pat>'; pwsh build/fetch-siblings.ps1
#>
[CmdletBinding()]
param(
    [string]$PinsFile,
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step { param([string]$Message) Write-Host "[siblings] $Message" }

function Fail {
    param([string]$Message)
    Write-Host "::error::[siblings] $Message"
    exit 1
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot

if (-not $PinsFile) {
    $PinsFile = Join-Path $scriptRoot 'sibling-pins.json'
}

if (-not (Test-Path -LiteralPath $PinsFile)) {
    Fail "Pins file not found: $PinsFile"
}

try {
    $pins = Get-Content -LiteralPath $PinsFile -Raw | ConvertFrom-Json
}
catch {
    Fail "Pins file is not valid JSON ($PinsFile): $($_.Exception.Message)"
}

if (($pins.PSObject.Properties.Name -notcontains 'siblings') -or -not $pins.siblings) {
    Fail "Pins file has no 'siblings' array: $PinsFile"
}

# --- Validation pass: every pin must be complete and syntactically valid before we touch the network.
$shaPattern = '^[0-9a-f]{40}$'
$index = 0
foreach ($sibling in $pins.siblings) {
    $index++
    foreach ($field in @('name', 'url', 'path', 'sha')) {
        $value = $null
        if ($sibling.PSObject.Properties.Name -contains $field) { $value = $sibling.$field }
        if ([string]::IsNullOrWhiteSpace($value)) {
            Fail "sibling #$index is missing required field '$field' in $PinsFile"
        }
    }

    if ($sibling.sha -notmatch $shaPattern) {
        Fail "sibling '$($sibling.name)' has an invalid pin '$($sibling.sha)'. Expected a full 40-character lowercase commit SHA."
    }
}

Write-Step "Validated $index pin(s) in $PinsFile"

if ($ValidateOnly) {
    Write-Step 'ValidateOnly requested - no repositories were fetched.'
    exit 0
}

$git = Get-Command git -ErrorAction SilentlyContinue
if (-not $git) {
    Fail "git was not found on PATH. Install Git and retry."
}

$authArgs = @()
$token = $env:SIBLING_REPOS_TOKEN
if (-not [string]::IsNullOrWhiteSpace($token)) {
    $basic = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("x-access-token:$token"))
    # Passed per invocation (-c) so it never lands in .git/config.
    $authArgs = @('-c', "http.extraheader=AUTHORIZATION: basic $basic")
    Write-Step 'Using SIBLING_REPOS_TOKEN for authentication.'
}
else {
    Write-Step 'SIBLING_REPOS_TOKEN not set - using anonymous access (public repositories only).'
}

function Invoke-Git {
    param(
        [string]$WorkingDirectory,
        [string[]]$Arguments,
        [switch]$AllowFailure
    )

    $all = @()
    if ($WorkingDirectory) { $all += @('-C', $WorkingDirectory) }
    $all += $Arguments

    # git reports progress on stderr even on success. Under $ErrorActionPreference = 'Stop' a
    # redirected native stderr line becomes a terminating error, so relax it for the call and
    # judge the outcome by the exit code alone.
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & git @all 2>&1
        $exit = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }

    if ($exit -ne 0 -and -not $AllowFailure) {
        $redacted = ($output | Out-String).Trim()
        Fail "git $($Arguments -join ' ') failed with exit code $exit`n$redacted"
    }

    return [pscustomobject]@{ ExitCode = $exit; Output = ($output | Out-String).Trim() }
}

foreach ($sibling in $pins.siblings) {
    $name = $sibling.name
    $sha = $sibling.sha
    $branch = if ($sibling.PSObject.Properties.Name -contains 'branch' -and $sibling.branch) { $sibling.branch } else { $null }
    $target = [IO.Path]::GetFullPath((Join-Path $repoRoot $sibling.path))

    Write-Step "$name -> $target @ $sha"

    if (-not (Test-Path -LiteralPath (Join-Path $target '.git'))) {
        if (Test-Path -LiteralPath $target) {
            $existing = Get-ChildItem -LiteralPath $target -Force -ErrorAction SilentlyContinue
            if ($existing) {
                Fail "$name target '$target' exists, is not a git repository, and is not empty. Move it aside and retry."
            }
        }
        else {
            New-Item -ItemType Directory -Path $target -Force | Out-Null
        }

        Invoke-Git -WorkingDirectory $target -Arguments @('init', '--quiet') | Out-Null
        Invoke-Git -WorkingDirectory $target -Arguments @('remote', 'add', 'origin', $sibling.url) | Out-Null
    }
    else {
        $head = (Invoke-Git -WorkingDirectory $target -Arguments @('rev-parse', 'HEAD') -AllowFailure).Output
        if ($head -eq $sha) {
            Write-Step "$name already at pinned commit - skipping fetch."
            continue
        }

        $dirty = (Invoke-Git -WorkingDirectory $target -Arguments @('status', '--porcelain') -AllowFailure).Output
        if ($dirty) {
            Fail "$name at '$target' has uncommitted changes and is not on the pinned commit. Commit, stash or clean it manually - this script will not discard local work."
        }
    }

    $fetchArgs = @('fetch', '--no-tags', '--prune', 'origin')
    if ($branch) {
        $fetchArgs += "+refs/heads/${branch}:refs/remotes/origin/$branch"
    }

    Invoke-Git -WorkingDirectory $target -Arguments ($authArgs + $fetchArgs) | Out-Null

    $hasCommit = Invoke-Git -WorkingDirectory $target -Arguments @('cat-file', '-e', "$sha^{commit}") -AllowFailure
    if ($hasCommit.ExitCode -ne 0) {
        # The pin may live on another branch; widen the fetch once before giving up.
        Write-Step "$name pinned commit not on '$branch' - fetching all heads."
        Invoke-Git -WorkingDirectory $target -Arguments ($authArgs + @('fetch', '--no-tags', '--prune', 'origin', '+refs/heads/*:refs/remotes/origin/*')) | Out-Null
        $hasCommit = Invoke-Git -WorkingDirectory $target -Arguments @('cat-file', '-e', "$sha^{commit}") -AllowFailure
    }

    if ($hasCommit.ExitCode -ne 0) {
        Fail "$name pinned commit '$sha' does not exist on $($sibling.url). Update build/sibling-pins.json to a commit that has been pushed."
    }

    Invoke-Git -WorkingDirectory $target -Arguments @('-c', 'advice.detachedHead=false', 'checkout', '--force', $sha) | Out-Null

    $actual = (Invoke-Git -WorkingDirectory $target -Arguments @('rev-parse', 'HEAD')).Output
    if ($actual -ne $sha) {
        Fail "$name checkout verification failed. Expected $sha but HEAD is $actual."
    }

    Write-Step "$name ready at $sha"
}

Write-Step 'All pinned sibling repositories are ready.'
exit 0
