using System.Security.Principal;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Identity;
using SiNet.Infrastructure.Sql.Data;

namespace SiNet.Infrastructure.Sql.Services.Identity;

/// <summary>
/// Looks up the Windows identity in <c>SIUser</c> and binds
/// <see cref="AuthenticatedUserSession"/> (deny-by-default, mirrors legacy CurrentUserContext).
/// </summary>
public sealed class SqlWindowsCurrentUserAuthenticator(
    IDbContextFactory<SiNetDbContext> dbFactory,
    AuthenticatedUserSession session,
    IAppLogger logger)
{
    private readonly IDbContextFactory<SiNetDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly AuthenticatedUserSession _session =
        session ?? throw new ArgumentNullException(nameof(session));
    private readonly IAppLogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<bool> TryAuthenticateAsync(CancellationToken cancellationToken = default)
    {
        var windowsLogin = WindowsIdentity.GetCurrent().Name;
        _logger.Info($"SqlWindowsCurrentUserAuthenticator: initializing for Windows user '{windowsLogin}'.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var user = await FindUserAsync(db, windowsLogin, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            _logger.Error(
                $"User authorization failed — user not found in SIUser. WindowsLogin={windowsLogin}");
            _session.Clear();
            return false;
        }

        if (!user.IsActive)
        {
            _logger.Error(
                $"User authorization failed — user is inactive. UserId={user.Id}, LoginName={user.LoginName}");
            _session.Clear();
            return false;
        }

        var role = (AppRole)user.Role;
        if (role == AppRole.Unauthorized)
        {
            _logger.Error(
                $"User authorization failed — Unauthorized role. UserId={user.Id}, LoginName={user.LoginName}");
            _session.Clear();
            return false;
        }

        var displayName = string.IsNullOrWhiteSpace(user.Name)
            ? user.LoginName ?? user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : user.Name.Trim();

        _session.SetAuthenticated(new CurrentUserProfileDto(
            UserId: user.Id,
            DisplayName: displayName,
            LoginName: user.LoginName,
            Role: role,
            IsActive: true,
            MasterPlanEmployeeId: user.MasterPlanEmployeeId));

        _logger.Info(
            $"User authorized. UserId={user.Id}, LoginName={user.LoginName}, Role={role}");
        return true;
    }

    private static async Task<Entities.SiUserEntity?> FindUserAsync(
        SiNetDbContext db,
        string windowsLogin,
        CancellationToken cancellationToken)
    {
        var loginLower = windowsLogin.ToLowerInvariant();
        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                u => u.LoginName != null && u.LoginName.ToLower() == loginLower,
                cancellationToken)
            .ConfigureAwait(false);

        if (user is not null || !windowsLogin.Contains('\\', StringComparison.Ordinal))
        {
            return user;
        }

        var usernamePart = windowsLogin.Split('\\').Last();
        var suffix = "\\" + usernamePart.ToLowerInvariant();
        return await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                u => u.LoginName != null && u.LoginName.ToLower().EndsWith(suffix),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
