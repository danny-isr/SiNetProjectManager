using Microsoft.EntityFrameworkCore;
using SiNet.Application.Identity;
using SiNet.Infrastructure.Sql.Data;

namespace SiNet.Infrastructure.Sql.Services.Identity;

/// <summary>
/// Active-user lookup from native <see cref="SiNetDbContext"/> for Task Workbench scope selector and similar dropdowns.
/// </summary>
public sealed class SqlUserLookupService : IUserLookupService
{
    private readonly IDbContextFactory<SiNetDbContext> _dbFactory;

    public SqlUserLookupService(IDbContextFactory<SiNetDbContext> dbFactory)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    }

    public async Task<IReadOnlyList<UserLookupDto>> GetActiveUsersAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await db.Users
            .AsNoTracking()
            .Where(u => u.IsActive)
            .OrderBy(u => u.Name)
            .ThenBy(u => u.Id)
            .Select(u => new UserLookupDto(
                u.Id,
                u.Name ?? u.LoginName ?? $"User {u.Id}",
                u.IsActive))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
