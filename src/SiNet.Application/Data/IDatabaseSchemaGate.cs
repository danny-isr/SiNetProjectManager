namespace SiNet.Application.Data;

/// <summary>
/// Startup gate: verifies SQL connectivity and that required Task Management tables exist.
/// Hosts must fail closed when <see cref="DatabaseSchemaGateResult.IsReady"/> is false.
/// </summary>
public interface IDatabaseSchemaGate
{
    Task<DatabaseSchemaGateResult> ValidateAsync(CancellationToken cancellationToken = default);
}

/// <param name="CanConnect">True when the database accepts a connection.</param>
/// <param name="IsSchemaPresent">True when required Task Management tables exist.</param>
/// <param name="MissingTables">Missing table names when the schema is incomplete.</param>
public sealed record DatabaseSchemaGateResult(
    bool CanConnect,
    bool IsSchemaPresent,
    IReadOnlyList<string> MissingTables)
{
    public bool IsReady => CanConnect && IsSchemaPresent;
}
