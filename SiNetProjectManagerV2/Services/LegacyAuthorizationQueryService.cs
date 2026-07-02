using SiNet.Application.Identity;
using SiNetSQL.Models;
using SiNetSQL.Services;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Host adapter: maps legacy <see cref="CurrentUserContext"/> role checks to
/// <see cref="IAuthorizationQueryService"/>. WPF and NewShell must not reference
/// <see cref="CurrentUserContext"/> directly.
/// </summary>
internal sealed class LegacyAuthorizationQueryService : IAuthorizationQueryService
{
    /// <inheritdoc />
    public Task<bool> IsCurrentUserInRoleAsync(
        AppRole requiredRole,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var ctx = CurrentUserContext.Instance;
        if (!ctx.HasAccess)
        {
            return Task.FromResult(false);
        }

        var current = MapRole(ctx.Role);
        return Task.FromResult(AppFeatureAuthorization.SatisfiesRole(current, requiredRole));
    }

    /// <inheritdoc />
    public Task<bool> CanCurrentUserAccessFeatureAsync(
        string featureCode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var ctx = CurrentUserContext.Instance;
        if (!ctx.HasAccess)
        {
            return Task.FromResult(false);
        }

        var current = MapRole(ctx.Role);
        return Task.FromResult(AppFeatureAuthorization.CanAccessFeature(current, featureCode));
    }

    private static AppRole MapRole(AppUserRole role) => (AppRole)(int)role;
}
