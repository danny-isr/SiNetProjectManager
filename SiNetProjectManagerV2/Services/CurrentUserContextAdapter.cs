using SiNet.Application.Identity;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Host adapter that implements the new <see cref="ICurrentUserContext"/> Application port by reading
/// the legacy authenticated <see cref="SiNetSQL.Services.CurrentUserContext"/> singleton.
/// <para>
/// This is the single place that bridges the new clean port to the legacy host's Windows-identity
/// based user context. It exposes only the application user id; it makes no authorization decisions
/// (those stay in <c>CurrentUserContext</c>) and never invents an id — when no user is authenticated
/// <see cref="UserId"/> is <see langword="null"/> so callers fall back to an explicit input instead of
/// guessing. Replace with a native infrastructure implementation once identity is fully migrated.
/// </para>
/// </summary>
internal sealed class CurrentUserContextAdapter : ICurrentUserContext
{
    public int? UserId => SiNetSQL.Services.CurrentUserContext.Instance.CurrentUserId;
}
