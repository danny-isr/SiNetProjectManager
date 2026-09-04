using Microsoft.EntityFrameworkCore;
using SiNet.Application.Identity;
using SiNet.Infrastructure.Sql.Data;
using SiNet.Infrastructure.Sql.Entities;

namespace SiNet.Infrastructure.Sql.Services.Identity;

/// <summary>
/// Reloads the current SIUser row into <see cref="AuthenticatedUserSession"/> after administrator approval.
/// Never reactivates an inactive user.
/// </summary>
public sealed class SqlCurrentUserSessionRefreshService(
    IDbContextFactory<SiNetDbContext> dbFactory,
    AuthenticatedUserSession session) : ICurrentUserSessionRefreshService
{
    private readonly IDbContextFactory<SiNetDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly AuthenticatedUserSession _session =
        session ?? throw new ArgumentNullException(nameof(session));

    /// <inheritdoc />
    public async Task<CurrentUserProfileDto?> RefreshCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var current = await _session.GetCurrentUserAsync(cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return null;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        SiUserEntity? user = null;
        if (current.UserId > 0)
        {
            user = await db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == current.UserId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (user is null && !string.IsNullOrWhiteSpace(current.LoginName))
        {
            var loginLower = current.LoginName.Trim().ToLowerInvariant();
            user = await db.Users.AsNoTracking()
                .FirstOrDefaultAsync(
                    u => u.LoginName != null && u.LoginName.ToLower() == loginLower,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (user is null)
        {
            _session.Clear();
            return null;
        }

        if (!user.IsActive)
        {
            _session.Clear();
            return null;
        }

        var profile = SqlWindowsCurrentUserAuthenticator.ToProfile(user);
        _session.SetAuthenticated(profile);
        return profile;
    }
}
