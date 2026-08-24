using Microsoft.Data.SqlClient;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>
/// Reads the actual SQL Server identity from an established session and matches it against explicit allowlists.
/// The connection string <c>Data Source</c> is a declared endpoint only — it is never treated as proof of
/// which server answered the connection (fail-closed after the first live Preflight mismatch).
/// </summary>
internal static class SystemCertificationSqlTargetIdentity
{
    internal sealed record Identity(
        string ServerName,
        string? MachineName,
        string? InstanceName,
        string DatabaseName);

    internal sealed record VerificationResult(
        bool IsApproved,
        string? Violation,
        Identity? Identity);

    /// <summary>
    /// Fail-closed allowlist check against the server/database reported by the live SQL session.
    /// </summary>
    internal static string? EvaluateAllowlist(
        string actualServerName,
        string actualDatabaseName,
        IReadOnlyList<string> allowedServers,
        IReadOnlyList<string> allowedDatabases)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actualServerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(actualDatabaseName);
        ArgumentNullException.ThrowIfNull(allowedServers);
        ArgumentNullException.ThrowIfNull(allowedDatabases);

        if (allowedServers.Count == 0)
        {
            return $"{SystemCertificationEnvironment.AllowedServersEnv} is required: the approved SQL "
                   + "server(s), supplied independently of the connection string.";
        }

        if (allowedDatabases.Count == 0)
        {
            return $"{SystemCertificationEnvironment.AllowedDatabasesEnv} is required: the approved "
                   + "database name(s).";
        }

        if (!allowedServers.Contains(actualServerName, StringComparer.OrdinalIgnoreCase))
        {
            return $"Actual SQL server '{actualServerName}' is not on "
                   + $"{SystemCertificationEnvironment.AllowedServersEnv} "
                   + $"([{string.Join(", ", allowedServers)}]). The connection string Data Source is only "
                   + "a declared endpoint and does not prove which server answered.";
        }

        if (!allowedDatabases.Contains(actualDatabaseName, StringComparer.OrdinalIgnoreCase))
        {
            return $"Actual database '{actualDatabaseName}' is not on "
                   + $"{SystemCertificationEnvironment.AllowedDatabasesEnv} "
                   + $"([{string.Join(", ", allowedDatabases)}]).";
        }

        return null;
    }

    internal static async Task<VerificationResult> VerifyAsync(
        string connectionString,
        IReadOnlyList<string> allowedServers,
        IReadOnlyList<string> allowedDatabases,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        Identity identity;
        try
        {
            identity = await ReadIdentityAsync(connectionString, cancellationToken);
        }
        catch (SqlException ex)
        {
            return new VerificationResult(
                false,
                $"Could not read actual SQL server identity: {ex.Message}",
                null);
        }

        var violation = EvaluateAllowlist(
            identity.ServerName,
            identity.DatabaseName,
            allowedServers,
            allowedDatabases);

        return violation is null
            ? new VerificationResult(true, null, identity)
            : new VerificationResult(false, violation, identity);
    }

    internal static async Task<Identity> ReadIdentityAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                CAST(SERVERPROPERTY('ServerName') AS nvarchar(256)) AS ServerName,
                CAST(SERVERPROPERTY('MachineName') AS nvarchar(256)) AS MachineName,
                CAST(SERVERPROPERTY('InstanceName') AS nvarchar(256)) AS InstanceName,
                DB_NAME() AS DatabaseName;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Actual SQL identity query returned no rows.");
        }

        var serverName = reader.GetString(0);
        if (string.IsNullOrWhiteSpace(serverName))
        {
            throw new InvalidOperationException(
                "SERVERPROPERTY('ServerName') returned an empty value; cannot authorize writes.");
        }

        var databaseName = reader.GetString(3);
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException("DB_NAME() returned an empty value; cannot authorize writes.");
        }

        return new Identity(
            serverName.Trim(),
            reader.IsDBNull(1) ? null : reader.GetString(1).Trim(),
            reader.IsDBNull(2) ? null : reader.GetString(2).Trim(),
            databaseName.Trim());
    }
}
