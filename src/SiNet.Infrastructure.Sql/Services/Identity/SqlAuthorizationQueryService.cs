using Microsoft.EntityFrameworkCore;
using SiNet.Application.Identity;
using SiNet.Infrastructure.Sql.Data;

namespace SiNet.Infrastructure.Sql.Services.Identity;

/// <summary>
/// Native New System implementation of <see cref="IAuthorizationQueryService"/> — EF/SQL only, no WPF
/// and no SiNetSQL project references (see <c>docs/NEW_SYSTEM_BOUNDARY.md</c>). Resolves the current
/// user's <see cref="AppRole"/> from the database (<see cref="SiNetDbContext.Users"/>) and evaluates
/// feature access through <see cref="AppFeatureAuthorization"/> (hierarchical, deny-by-default).
/// <para>
/// Authorization is fail-closed: when there is no authenticated user (<see cref="ICurrentUserContext.UserId"/>
/// is <see langword="null"/>) or the user row is missing/inactive, role resolution yields
/// <see langword="null"/> and every check returns <see langword="false"/>.
/// </para>
/// </summary>
public sealed class SqlAuthorizationQueryService : IAuthorizationQueryService
{
    private readonly IDbContextFactory<SiNetDbContext> _dbFactory;
    private readonly ICurrentUserContext _currentUser;

    public SqlAuthorizationQueryService(
        IDbContextFactory<SiNetDbContext> dbFactory,
        ICurrentUserContext currentUser)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    }

    /// <inheritdoc />
    public async Task<bool> IsCurrentUserInRoleAsync(
        AppRole requiredRole,
        CancellationToken cancellationToken = default)
    {
        var role = await ResolveCurrentRoleAsync(cancellationToken).ConfigureAwait(false);
        return role is AppRole current && AppFeatureAuthorization.SatisfiesRole(current, requiredRole);
    }

    /// <inheritdoc />
    public async Task<bool> CanCurrentUserAccessFeatureAsync(
        string featureCode,
        CancellationToken cancellationToken = default)
    {
        // Validate the feature code first so unknown codes are rejected (never silently approved),
        // regardless of whether a user is authenticated. Matches the interface contract.
        var requiredRole = AppFeatureAuthorization.GetRequiredRole(featureCode);

        var role = await ResolveCurrentRoleAsync(cancellationToken).ConfigureAwait(false);
        return role is AppRole current && AppFeatureAuthorization.SatisfiesRole(current, requiredRole);
    }

    private async Task<AppRole?> ResolveCurrentRoleAsync(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not int userId || userId <= 0)
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var roleValue = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && u.IsActive)
            .Select(u => (int?)u.Role)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return roleValue is int value ? (AppRole)value : null;
    }
}
