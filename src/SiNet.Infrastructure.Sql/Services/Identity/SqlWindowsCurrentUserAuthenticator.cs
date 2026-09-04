using System.Security.Principal;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Abstractions.Logging;
using SiNet.Application.Identity;
using SiNet.Infrastructure.Sql.Data;
using SiNet.Infrastructure.Sql.Entities;

namespace SiNet.Infrastructure.Sql.Services.Identity;

/// <summary>
/// Looks up the Windows/runtime identity in <c>SIUser</c> and binds
/// <see cref="AuthenticatedUserSession"/>. Unknown LoginName auto-registers as Pending
/// (Role=Unauthorized). Inactive users remain Blocked (never auto-reactivated).
/// Concurrent registration uses applock + unique LoginName protection.
/// </summary>
public sealed class SqlWindowsCurrentUserAuthenticator(
    IDbContextFactory<SiNetDbContext> dbFactory,
    AuthenticatedUserSession session,
    IAppLogger logger) : IWindowsCurrentUserAuthenticator
{
    private readonly IDbContextFactory<SiNetDbContext> _dbFactory =
        dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly AuthenticatedUserSession _session =
        session ?? throw new ArgumentNullException(nameof(session));
    private readonly IAppLogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Optional override for tests (avoids WindowsIdentity).</summary>
    internal Func<string>? RuntimeLoginResolver { get; set; }

    /// <summary>Optional display-name override for auto-registration tests.</summary>
    internal Func<string, string>? RuntimeDisplayNameResolver { get; set; }

    /// <summary>
    /// Legacy bool gate used by older call sites/tests. Prefer <see cref="AuthenticateAsync"/>.
    /// Returns true for Authorized and PendingApproval (shell may open); false for Blocked.
    /// </summary>
    public async Task<bool> TryAuthenticateAsync(CancellationToken cancellationToken = default)
    {
        var result = await AuthenticateAsync(cancellationToken).ConfigureAwait(false);
        return result.Status is WindowsUserAuthStatus.Authorized or WindowsUserAuthStatus.PendingApproval;
    }

    /// <inheritdoc />
    public async Task<WindowsUserAuthenticationResult> AuthenticateAsync(
        CancellationToken cancellationToken = default)
    {
        var windowsLogin = RuntimeLoginResolver?.Invoke() ?? WindowsIdentity.GetCurrent().Name;
        if (string.IsNullOrWhiteSpace(windowsLogin))
        {
            _session.Clear();
            return new WindowsUserAuthenticationResult(
                WindowsUserAuthStatus.Blocked,
                Profile: null,
                FailureReason: "Runtime login identity is empty.");
        }

        _logger.Info($"SqlWindowsCurrentUserAuthenticator: initializing for '{windowsLogin}'.");

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var user = await FindUserAsync(db, windowsLogin, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            user = await EnsurePendingUserAsync(db, windowsLogin, cancellationToken).ConfigureAwait(false);
            if (user is null)
            {
                _session.Clear();
                return new WindowsUserAuthenticationResult(
                    WindowsUserAuthStatus.Blocked,
                    Profile: null,
                    FailureReason: "Failed to auto-register pending SIUser.");
            }

            _logger.Info(
                $"Pending SIUser auto-registered. UserId={user.Id}, LoginName={user.LoginName}");
        }

        if (!user.IsActive)
        {
            _logger.Error(
                $"User authorization blocked — inactive SIUser. UserId={user.Id}, LoginName={user.LoginName}");
            _session.Clear();
            return new WindowsUserAuthenticationResult(
                WindowsUserAuthStatus.Blocked,
                Profile: null,
                FailureReason: "SIUser is inactive.");
        }

        var profile = ToProfile(user);
        _session.SetAuthenticated(profile);

        if (profile.IsPendingApproval)
        {
            _logger.Info(
                $"User pending administrator approval. UserId={profile.UserId}, LoginName={profile.LoginName}");
            return new WindowsUserAuthenticationResult(
                WindowsUserAuthStatus.PendingApproval,
                profile);
        }

        _logger.Info(
            $"User authorized. UserId={profile.UserId}, LoginName={profile.LoginName}, Role={profile.Role}");
        return new WindowsUserAuthenticationResult(WindowsUserAuthStatus.Authorized, profile);
    }

    /// <summary>Maps a SIUser entity to the shared profile DTO (includes Email + AccUserType).</summary>
    internal static CurrentUserProfileDto ToProfile(SiUserEntity user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var displayName = string.IsNullOrWhiteSpace(user.Name)
            ? user.LoginName ?? user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : user.Name.Trim();

        return new CurrentUserProfileDto(
            UserId: user.Id,
            DisplayName: displayName,
            LoginName: user.LoginName,
            Role: (AppRole)user.Role,
            IsActive: user.IsActive,
            MasterPlanEmployeeId: user.MasterPlanEmployeeId,
            Email: string.IsNullOrWhiteSpace(user.Email) ? null : user.Email.Trim(),
            AccUserType: (AppAccUserType)user.AccUserType);
    }

    private async Task<SiUserEntity?> EnsurePendingUserAsync(
        SiNetDbContext db,
        string windowsLogin,
        CancellationToken cancellationToken)
    {
        var displayName = RuntimeDisplayNameResolver?.Invoke(windowsLogin)
            ?? DeriveDisplayName(windowsLogin);

        // In-memory / non-relational providers: serialize by LoginName (no applock/transactions).
        if (!db.Database.IsRelational())
        {
            var gate = PendingRegistrationGates.GetOrAdd(
                windowsLogin.Trim().ToLowerInvariant(),
                _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await InsertPendingUserCoreAsync(db, windowsLogin, displayName, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        // Prefer SQL applock when available so concurrent starts create exactly one row.
        await using var transaction = await db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await TryAcquireAppLockAsync(db, windowsLogin, cancellationToken).ConfigureAwait(false);

        var existing = await FindUserAsync(db, windowsLogin, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return existing;
        }

        try
        {
            var created = await InsertPendingUserCoreAsync(db, windowsLogin, displayName, cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return created;
        }
        catch (DbUpdateException ex)
        {
            _logger.Warn(
                $"Pending SIUser insert raced for LoginName='{windowsLogin}' — re-reading. {ex.GetType().Name}: {ex.Message}");
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            return await FindUserAsync(db, windowsLogin, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<SiUserEntity?> InsertPendingUserCoreAsync(
        SiNetDbContext db,
        string windowsLogin,
        string displayName,
        CancellationToken cancellationToken)
    {
        var existing = await FindUserAsync(db, windowsLogin, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var entity = new SiUserEntity
        {
            LoginName = windowsLogin.Trim(),
            Name = displayName,
            Email = null,
            IsActive = true,
            Role = (int)AppRole.Unauthorized,
            AccUserType = (int)AppAccUserType.NoAccUser,
            MasterPlanEmployeeId = null,
            Notes = null,
            IsDomainGroup = false,
        };

        db.Users.Add(entity);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return entity;
        }
        catch (DbUpdateException ex)
        {
            _logger.Warn(
                $"Pending SIUser insert raced for LoginName='{windowsLogin}' — re-reading. {ex.GetType().Name}: {ex.Message}");
            db.ChangeTracker.Clear();
            return await FindUserAsync(db, windowsLogin, cancellationToken).ConfigureAwait(false);
        }
    }

    private static class PendingRegistrationGates
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> Gates =
            new(StringComparer.Ordinal);

        public static SemaphoreSlim GetOrAdd(string key, Func<string, SemaphoreSlim> factory)
            => Gates.GetOrAdd(key, factory);
    }

    private static async Task TryAcquireAppLockAsync(
        SiNetDbContext db,
        string windowsLogin,
        CancellationToken cancellationToken)
    {
        // In-memory / non-SQL providers skip; uniqueness + re-read still protect.
        if (!db.Database.IsSqlServer())
        {
            return;
        }

        try
        {
            var resource = "SiNet:SIUser:Register:" + windowsLogin.Trim().ToLowerInvariant();
            if (resource.Length > 255)
            {
                resource = resource[..255];
            }

            var pResource = new SqlParameter("@Resource", resource);
            var pResult = new SqlParameter("@Result", System.Data.SqlDbType.Int)
            {
                Direction = System.Data.ParameterDirection.Output,
            };

            await db.Database.ExecuteSqlRawAsync(
                    "EXEC @Result = sp_getapplock @Resource=@Resource, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=30000;",
                    [pResult, pResource],
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Non-fatal: duplicate protection still relies on unique index + catch/re-read.
        }
    }

    private static string DeriveDisplayName(string windowsLogin)
    {
        var trimmed = windowsLogin.Trim();
        if (trimmed.Contains('\\', StringComparison.Ordinal))
        {
            return trimmed.Split('\\').Last();
        }

        return trimmed;
    }

    private static async Task<SiUserEntity?> FindUserAsync(
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
