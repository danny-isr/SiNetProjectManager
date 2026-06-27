using Microsoft.EntityFrameworkCore;

namespace SiNetSQL.Data;

/// <summary>
/// Lightweight IDbContextFactory implementation for non-DI scenarios (e.g., ViewModels).
/// Uses the parameterless SiNetSQLDbContext constructor which has a built-in connection string.
/// </summary>
public sealed class SiNetSQLDbContextFactory : IDbContextFactory<SiNetSQLDbContext>
{
    public SiNetSQLDbContext CreateDbContext() => new();
}
