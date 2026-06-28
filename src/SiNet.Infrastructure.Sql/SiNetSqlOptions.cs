namespace SiNet.Infrastructure.Sql;

/// <summary>
/// Options for the SQL module registration (<see cref="SqlServiceCollectionExtensions.AddSiNetSql(Microsoft.Extensions.DependencyInjection.IServiceCollection, string, System.Action{SiNetSqlOptions})"/>).
/// Defaults are chosen to keep Release behavior identical to a registration with no options.
/// </summary>
public sealed class SiNetSqlOptions
{
    /// <summary>
    /// When <see langword="true"/>, the registered <c>DbContextFactory</c> enables
    /// <c>EnableSensitiveDataLogging()</c> and <c>EnableDetailedErrors()</c>.
    /// Defaults to <see langword="false"/> (no EF diagnostics).
    /// </summary>
    /// <remarks>
    /// A host should set this only under <c>#if DEBUG</c> to reproduce the legacy host's
    /// development-time diagnostics. It must remain disabled in Release so production SQL/runtime
    /// behavior is unchanged.
    /// </remarks>
    public bool EnableEfDebugDiagnostics { get; set; }
}
