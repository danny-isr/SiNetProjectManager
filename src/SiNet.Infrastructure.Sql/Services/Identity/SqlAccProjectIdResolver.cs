using Microsoft.EntityFrameworkCore;
using SiNet.Application.Identity;
using SiNetSQL.Data;

namespace SiNet.Infrastructure.Sql.Services.Identity;

/// <summary>
/// Resolves AccProjectId from ProjectAccMapping (SQL helper). Membership truth remains ACC readback.
/// </summary>
public sealed class SqlAccProjectIdResolver(IDbContextFactory<SiNetSQLDbContext> dbFactory)
    : IAccProjectIdResolver
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));

    /// <inheritdoc />
    public async Task<string?> ResolveAccProjectIdAsync(
        int siProjectId,
        CancellationToken cancellationToken = default)
    {
        if (siProjectId <= 0)
        {
            return null;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var accProjectId = await db.ProjectAccMappings
            .AsNoTracking()
            .Where(m => m.ProjectId == siProjectId)
            .Select(m => m.AccProjectId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(accProjectId) ? null : accProjectId.Trim();
    }
}
