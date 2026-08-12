using Microsoft.Extensions.Configuration;

namespace MasterPlan.SyncEngine;

/// <summary>
/// DEV-022: after monthly restore, ensure configured Windows principals can read/write
/// <c>Db_Mp_SiEng</c> and <c>Replica_DB</c> (bak replace drops local DB users).
/// </summary>
public sealed record MonthlySqlAccessOptions
{
    public const string ConfigurationSectionName = "MonthlySqlAccess";

    public const string DefaultPrincipal = @"SI-ENG\שרטטים";

    public static IReadOnlyList<string> DefaultRoles { get; } =
        ["db_datareader", "db_datawriter"];

    public static IReadOnlyList<string> DefaultDatabases { get; } =
        ["Db_Mp_SiEng", "Replica_DB"];

    /// <summary>When false, skip ACL ensurance entirely.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Windows logins/groups (DOMAIN\name).</summary>
    public IReadOnlyList<string> WindowsPrincipals { get; init; } = [DefaultPrincipal];

    /// <summary>Database roles to grant (must already exist).</summary>
    public IReadOnlyList<string> DatabaseRoles { get; init; } = DefaultRoles;

    /// <summary>Target database names on the same instance.</summary>
    public IReadOnlyList<string> Databases { get; init; } = DefaultDatabases;

    public static MonthlySqlAccessOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(ConfigurationSectionName);
        if (!section.Exists())
        {
            return new MonthlySqlAccessOptions();
        }

        var enabled = true;
        if (bool.TryParse(section["Enabled"], out var parsedEnabled))
        {
            enabled = parsedEnabled;
        }

        var principals = section.GetSection("WindowsPrincipals").GetChildren()
            .Select(c => c.Value?.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Cast<string>()
            .ToList();

        var roles = section.GetSection("DatabaseRoles").GetChildren()
            .Select(c => c.Value?.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Cast<string>()
            .ToList();

        var databases = section.GetSection("Databases").GetChildren()
            .Select(c => c.Value?.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Cast<string>()
            .ToList();

        return new MonthlySqlAccessOptions
        {
            Enabled = enabled,
            WindowsPrincipals = principals.Count > 0 ? principals : [DefaultPrincipal],
            DatabaseRoles = roles.Count > 0 ? roles : DefaultRoles,
            Databases = databases.Count > 0 ? databases : DefaultDatabases
        };
    }
}
