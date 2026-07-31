namespace SiNet.Application.Data;

/// <summary>
/// Startup gate: verifies SQL connectivity, that required Task Management tables exist,
/// and that no EF migrations from the deployed assembly are still pending.
/// Hosts must fail closed when <see cref="DatabaseSchemaGateResult.IsReady"/> is false.
/// </summary>
public interface IDatabaseSchemaGate
{
    Task<DatabaseSchemaGateResult> ValidateAsync(CancellationToken cancellationToken = default);
}

/// <param name="CanConnect">True when the database accepts a connection.</param>
/// <param name="IsSchemaPresent">True when required Task Management tables exist.</param>
/// <param name="MissingTables">Missing table names when the schema is incomplete.</param>
/// <param name="PendingMigrations">
/// Migration ids present in the assembly but missing from <c>__EFMigrationsHistory</c>.
/// Empty when the database is at the assembly head.
/// </param>
public sealed record DatabaseSchemaGateResult(
    bool CanConnect,
    bool IsSchemaPresent,
    IReadOnlyList<string> MissingTables,
    IReadOnlyList<string> PendingMigrations)
{
    public bool HasPendingMigrations => PendingMigrations.Count > 0;

    public bool IsReady => CanConnect && IsSchemaPresent && !HasPendingMigrations;
}
