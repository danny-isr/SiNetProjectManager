using SiNet.Application.Identity;
using SiNetSQL.Models;
using SiNetSQL.Services;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Host adapter: maps the legacy authenticated <see cref="CurrentUserContext"/> singleton to the
/// clean <see cref="ICurrentUserProfileService"/> port. Read-only; no EF types leak to WPF.
/// </summary>
internal sealed class LegacyCurrentUserProfileService : ICurrentUserProfileService
{
    /// <inheritdoc />
    public Task<CurrentUserProfileDto?> GetCurrentUserAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var ctx = CurrentUserContext.Instance;
        if (!ctx.HasAccess || ctx.CurrentUserId is not int userId)
        {
            return Task.FromResult<CurrentUserProfileDto?>(null);
        }

        var displayName = ctx.DisplayName;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = ctx.DatabaseLoginName ?? userId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var profile = new CurrentUserProfileDto(
            UserId: userId,
            DisplayName: displayName,
            LoginName: ctx.DatabaseLoginName,
            Role: MapRole(ctx.Role),
            IsActive: true,
            MasterPlanEmployeeId: ctx.MasterPlanEmployeeId);

        return Task.FromResult<CurrentUserProfileDto?>(profile);
    }

    private static AppRole MapRole(AppUserRole role) => (AppRole)(int)role;
}
