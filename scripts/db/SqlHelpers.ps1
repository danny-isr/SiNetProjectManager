<#
    Shared SQL helpers for the scripts under scripts/db.
    Dot-source it: . (Join-Path $PSScriptRoot 'SqlHelpers.ps1')

    Uses System.Data.SqlClient (in-box on Windows PowerShell) so the scripts run on an operator
    machine without installing the SqlServer PowerShell module.
#>

Set-StrictMode -Version Latest

function New-SiNetSqlConnectionString {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Server,
        [string]$Database = 'master',
        [string]$UserId,
        [string]$Password
    )

    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder['Data Source'] = $Server
    $builder['Initial Catalog'] = $Database
    $builder['TrustServerCertificate'] = $true
    $builder['Connect Timeout'] = 30

    if ([string]::IsNullOrWhiteSpace($UserId)) {
        $builder['Integrated Security'] = $true
    }
    else {
        $builder['User ID'] = $UserId
        $builder['Password'] = $Password
    }

    return $builder.ConnectionString
}

function Split-SqlBatches {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Script)

    $batches = [regex]::Split($Script, '(?im)^[\t ]*GO[\t ]*(?:--.*)?$')
    return $batches | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
}

function Invoke-SiNetSqlNonQuery {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ConnectionString,
        [Parameter(Mandatory = $true)][string]$Sql,
        [int]$CommandTimeoutSeconds = 600,
        [hashtable]$Parameters
    )

    $connection = New-Object System.Data.SqlClient.SqlConnection $ConnectionString
    try {
        $connection.Open()
        foreach ($batch in (Split-SqlBatches -Script $Sql)) {
            $command = $connection.CreateCommand()
            $command.CommandText = $batch
            $command.CommandTimeout = $CommandTimeoutSeconds
            if ($Parameters) {
                foreach ($key in $Parameters.Keys) {
                    [void]$command.Parameters.AddWithValue($key, $Parameters[$key])
                }
            }
            [void]$command.ExecuteNonQuery()
        }
    }
    finally {
        $connection.Dispose()
    }
}

function Invoke-SiNetSqlQuery {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ConnectionString,
        [Parameter(Mandatory = $true)][string]$Sql,
        [int]$CommandTimeoutSeconds = 600,
        [hashtable]$Parameters
    )

    $connection = New-Object System.Data.SqlClient.SqlConnection $ConnectionString
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $Sql
        $command.CommandTimeout = $CommandTimeoutSeconds
        if ($Parameters) {
            foreach ($key in $Parameters.Keys) {
                [void]$command.Parameters.AddWithValue($key, $Parameters[$key])
            }
        }

        $adapter = New-Object System.Data.SqlClient.SqlDataAdapter $command
        $table = New-Object System.Data.DataTable
        [void]$adapter.Fill($table)
        return $table
    }
    finally {
        $connection.Dispose()
    }
}

function Write-SqlPlan {
    <#
        Dry-run output. Every script in this folder prints the exact statements it would run and
        exits unless -Execute was passed, so nothing touches a live database by accident.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Title,
        [Parameter(Mandatory = $true)][string[]]$Statements
    )

    Write-Host ''
    Write-Host "=== DRY RUN: $Title ===" -ForegroundColor Yellow
    foreach ($statement in $Statements) {
        Write-Host $statement
    }
    Write-Host '=== end of plan. Re-run with -Execute to apply. ===' -ForegroundColor Yellow
}
