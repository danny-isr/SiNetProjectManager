using SiNet.Application.Identity;

namespace SiNet.Infrastructure.Sql.Services.Identity;

/// <summary>
/// Process-wide authenticated user for the standalone New System host.
/// Populated at startup by <see cref="SqlWindowsCurrentUserAuthenticator"/>
/// (including Pending/Unauthorized sessions that open the restricted shell).
/// </summary>
public sealed class AuthenticatedUserSession : ICurrentUserContext, ICurrentUserProfileService
{
    private CurrentUserProfileDto? _profile;

    public int? UserId => _profile?.UserId;

    /// <summary>True when an SIUser row is bound (including PendingApproval).</summary>
    public bool HasSession => _profile is not null;

    /// <summary>Business access: active SIUser with Role ≥ Employee (excludes Pending/Unauthorized).</summary>
    public bool HasAccess => _profile is { IsActive: true } profile && profile.HasBusinessAccess;

    /// <summary>Active Unauthorized SIUser — restricted pending shell.</summary>
    public bool IsPendingApproval => _profile?.IsPendingApproval == true;

    public void SetAuthenticated(CurrentUserProfileDto profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.UserId <= 0)
        {
            throw new ArgumentException("UserId must be positive.", nameof(profile));
        }

        _profile = profile;
    }

    public void Clear() => _profile = null;

    public Task<CurrentUserProfileDto?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_profile);
    }
}
