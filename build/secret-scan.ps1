<#
.SYNOPSIS
    Fails the build when a credential-looking value is committed to this repository.

.DESCRIPTION
    Replaces the inline appsettings.json-only check that used to live in ci.yml. Scans every
    tracked text file for credential patterns. The MasterPlan API key incident (see
    docs/OPS-P0-SECRET-ROTATION.md) is the reason this gate exists: HEAD must stay clean even
    though the historical commit is intentionally not rewritten.

    Scope is "everything git would accept into a commit": tracked files plus untracked files
    that are not gitignored. Gitignored local secrets (credentials.json, a real appsettings.json
    on a workstation) are out of scope by design. Including the untracked-but-not-ignored set is
    what makes the script useful before a commit, not only after one.

.PARAMETER RepoRoot
    Repository root. Defaults to the parent of this script's directory.

.EXAMPLE
    pwsh build/secret-scan.ps1
#>
[CmdletBinding()]
param(
    [string]$RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $RepoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
}

Push-Location $RepoRoot
try {
    function Get-GitFileList {
        param([string[]]$Arguments)

        # git can write to stderr on success; under 'Stop' that would terminate the script.
        $previousPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $raw = & git @Arguments 2>&1
            $exit = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previousPreference
        }

        if ($exit -ne 0) {
            Write-Host "::error::[secret-scan] 'git $($Arguments -join ' ')' failed. Run this from inside a git working tree."
            exit 1
        }

        return ($raw -join "`0") -split "`0" | Where-Object { $_ }
    }

    $tracked = @(Get-GitFileList @('ls-files', '-z'))
    $addable = @(Get-GitFileList @('ls-files', '--others', '--exclude-standard', '-z'))
    $tracked = @($tracked + $addable | Sort-Object -Unique)

    # Text-ish files only. Binary and editor-cache files produce noise, not signal.
    $scannableExtensions = @(
        '.cs', '.json', '.xaml', '.xml', '.config', '.props', '.targets', '.csproj', '.sln',
        '.ps1', '.psm1', '.yml', '.yaml', '.md', '.sql', '.txt', '.env', '.ini', '.sh', '.bat', '.cmd'
    )

    # Paths that are allowed to contain matches (documented placeholders / detection patterns).
    $allowedPathPatterns = @(
        '^build/secret-scan\.ps1$',
        '^\.github/workflows/ci\.yml$'
    )

    $rules = @(
        @{ Name = 'Non-empty ApiKey in config'; Pattern = '"ApiKey"\s*:\s*"[^"]+"' },
        @{ Name = 'Non-empty secret-like config value'; Pattern = '"(ClientSecret|SecretKey|AccessToken|RefreshToken|PfxPassword|CertificatePassword)"\s*:\s*"[^"]+"' },
        @{ Name = 'GitHub token'; Pattern = '(ghp_[A-Za-z0-9]{36}|github_pat_[A-Za-z0-9_]{20,})' },
        @{ Name = 'Google API key'; Pattern = 'AIza[0-9A-Za-z\-_]{35}' },
        @{ Name = 'Private key block'; Pattern = '-----BEGIN [A-Z ]*PRIVATE KEY-----' },
        @{ Name = 'Inline password assignment'; Pattern = '(?i)(password|pwd)\s*=\s*"(?!\s*")[^"$%{;]{6,}"' }
    )

    # Matches that are structurally credential-shaped but are known not to be values:
    # vault key names live under the "SiNet/" namespace and are safe to commit.
    $falsePositivePatterns = @(
        '=\s*"SiNet/'
    )

    $candidates = foreach ($path in $tracked) {
        $ext = [IO.Path]::GetExtension($path)
        if ($scannableExtensions -notcontains $ext) { continue }

        $normalized = $path -replace '\\', '/'
        $allowed = $false
        foreach ($allowPattern in $allowedPathPatterns) {
            if ($normalized -match $allowPattern) { $allowed = $true; break }
        }
        if ($allowed) { continue }

        if (-not (Test-Path -LiteralPath $path)) { continue }
        $path
    }

    $violations = New-Object System.Collections.Generic.List[string]
    $scanned = 0

    foreach ($path in $candidates) {
        $scanned++
        $content = Get-Content -LiteralPath $path -Raw -ErrorAction SilentlyContinue
        if (-not $content) { continue }

        foreach ($rule in $rules) {
            foreach ($match in [regex]::Matches($content, $rule.Pattern)) {
                $ignored = $false
                foreach ($falsePositive in $falsePositivePatterns) {
                    if ($match.Value -match $falsePositive) { $ignored = $true; break }
                }
                if ($ignored) { continue }

                $line = ($content.Substring(0, $match.Index) -split "`n").Count
                $violations.Add("$path`:$line - $($rule.Name)")
                break
            }
        }
    }

    Write-Host "[secret-scan] scanned $scanned committable text file(s)."

    if ($violations.Count -gt 0) {
        Write-Host "::error::[secret-scan] potential secrets found in committable files:"
        foreach ($violation in $violations) {
            Write-Host "::error::  $violation"
        }
        Write-Host "[secret-scan] Remove the value, load it from the credential vault or an environment variable, and re-run."
        exit 1
    }

    Write-Host '[secret-scan] passed: no credential patterns in committable text files.'
    exit 0
}
finally {
    Pop-Location
}
