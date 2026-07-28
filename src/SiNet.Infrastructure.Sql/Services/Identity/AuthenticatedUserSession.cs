using SiNet.Application.Identity;

namespace SiNet.Infrastructure.Sql.Services.Identity;

/// <summary>
/// Process-wide authenticated user for the standalone New System host.
/// Populated once at startup by <see cref="SqlWindowsCurrentUserAuthenticator"/>.
/// </summary>
public sealed class AuthenticatedUserSession : ICurrentUserContext, ICurrentUserProfileService
{
    private CurrentUserProfileDto? _profile;

    public int? UserId => _profile?.UserId;

    public bool HasAccess => _profile is { IsActive: true, Role: not AppRole.Unauthorized };

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
