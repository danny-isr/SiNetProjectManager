using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace MasterPlan.SyncEngine;

/// <summary>
/// Idempotent server login + database user + role membership for monthly restore (DEV-022).
/// </summary>
public static class MonthlySqlAccessEnsurer
{
    /// <summary>Bracket-quote a SQL identifier; doubles embedded <c>]</c>.</summary>
    public static string QuoteBracket(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        return "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }

    /// <summary>Unicode string literal for a principal name (escapes single quotes).</summary>
    public static string QuoteNString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "N'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    public static string BuildEnsureLoginSql(string windowsPrincipal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowsPrincipal);
        var bracket = QuoteBracket(windowsPrincipal);
        var nName = QuoteNString(windowsPrincipal);
        return $"""
            IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = {nName})
            BEGIN
                CREATE LOGIN {bracket} FROM WINDOWS;
            END
            """;
    }

    public static string BuildEnsureUserAndRolesSql(
        string windowsPrincipal,
        IReadOnlyList<string> databaseRoles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowsPrincipal);
        ArgumentNullException.ThrowIfNull(databaseRoles);

        var bracket = QuoteBracket(windowsPrincipal);
        var nName = QuoteNString(windowsPrincipal);
        var roleBlocks = new List<string>();
        foreach (var role in databaseRoles)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                continue;
            }

            if (!IsSafeRoleName(role))
            {
                throw new ArgumentException($"Database role name is not allowed: '{role}'.", nameof(databaseRoles));
            }

            var roleN = QuoteNString(role);
            roleBlocks.Add($"""
                IF IS_ROLEMEMBER({roleN}, {nName}) = 0
                BEGIN
                    ALTER ROLE [{role}] ADD MEMBER {bracket};
                END
                """);
        }

        return $"""
            IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = {nName})
            BEGIN
                CREATE USER {bracket} FOR LOGIN {bracket};
            END
            ELSE
            BEGIN
                ALTER USER {bracket} WITH LOGIN = {bracket};
            END
            {string.Join(Environment.NewLine, roleBlocks)}
            """;
    }

    /// <summary>
    /// Ensures logins on the instance, then user+roles on each configured database.
    /// When <paramref name="databaseFilter"/> is set, only those databases (intersection with options) are updated.
    /// </summary>
    public static async Task EnsureAsync(
        string masterConnectionString,
        MonthlySqlAccessOptions options,
        ILogger logger,
        IReadOnlyList<string>? databaseFilter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(masterConnectionString);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        if (!options.Enabled)
        {
            logger.LogInformation("MonthlySqlAccess: skipped (Enabled=false)");
            Console.WriteLine("    [SQL ACCESS] skipped (Enabled=false)");
            return;
        }

        if (options.WindowsPrincipals.Count == 0)
        {
            logger.LogWarning("MonthlySqlAccess: Enabled but WindowsPrincipals is empty — nothing to do");
            return;
        }

        var databases = options.Databases
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (databaseFilter is { Count: > 0 })
        {
            var filter = new HashSet<string>(databaseFilter, StringComparer.OrdinalIgnoreCase);
            databases = databases.Where(d => filter.Contains(d)).ToList();
        }

        if (databases.Count == 0)
        {
            logger.LogInformation("MonthlySqlAccess: no databases in scope for this step");
            return;
        }

        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        foreach (var principal in options.WindowsPrincipals)
        {
            if (string.IsNullOrWhiteSpace(principal))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var loginSql = BuildEnsureLoginSql(principal);
            try
            {
                await using var loginCmd = new SqlCommand(loginSql, connection) { CommandTimeout = 60 };
                await loginCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                logger.LogWarning("MonthlySqlAccess: ensured server login for {Principal}", principal);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"כישלון ביצירת/אימות LOGIN עבור '{principal}'. " +
                    "וודא שהקבוצה קיימת ב־AD ושלחשבון SyncEngine יש הרשאה ליצור login. " +
                    ex.Message,
                    ex);
            }

            foreach (var database in databases)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsSafeDatabaseName(database))
                {
                    throw new InvalidOperationException($"שם מסד נתונים לא חוקי להרשאות: '{database}'.");
                }

                var useAndBody = $"""
                    USE {QuoteBracket(database)};
                    {BuildEnsureUserAndRolesSql(principal, options.DatabaseRoles)}
                    """;

                try
                {
                    await using var dbCmd = new SqlCommand(useAndBody, connection) { CommandTimeout = 60 };
                    await dbCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    logger.LogWarning(
                        "MonthlySqlAccess: ensured user/roles for {Principal} on {Database} ({Roles})",
                        principal,
                        database,
                        string.Join(",", options.DatabaseRoles));
                    Console.WriteLine($"    [SQL ACCESS] {principal} → {database} ({string.Join("+", options.DatabaseRoles)})");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"כישלון בהענקת הרשאות ל־'{principal}' על '{database}'. {ex.Message}",
                        ex);
                }
            }
        }
    }

    /// <summary>Allow only simple dbo-style role names (letters, digits, underscore).</summary>
    internal static bool IsSafeRoleName(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return false;
        }

        foreach (var ch in role)
        {
            if (!(char.IsAsciiLetterOrDigit(ch) || ch == '_'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Allow letters, digits, underscore (e.g. Db_Mp_SiEng, Replica_DB).</summary>
    internal static bool IsSafeDatabaseName(string database)
    {
        if (string.IsNullOrWhiteSpace(database))
        {
            return false;
        }

        foreach (var ch in database)
        {
            if (!(char.IsAsciiLetterOrDigit(ch) || ch == '_'))
            {
                return false;
            }
        }

        return true;
    }
}
