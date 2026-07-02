namespace SiNet.Application.Identity;

/// <summary>
/// Formats <see cref="CurrentUserProfileDto"/> for shell/header display (see
/// <c>docs/IDENTITY_AND_PERMISSIONS.md</c> P2).
/// </summary>
public static class CurrentUserProfileDisplay
{
    /// <summary>
    /// Friendly display text: display name, then login name, then <c>משתמש #{id}</c>.
    /// Returns <see langword="null"/> when <paramref name="profile"/> is null.
    /// </summary>
    public static string? Format(CurrentUserProfileDto? profile)
    {
        if (profile is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(profile.DisplayName))
        {
            return profile.DisplayName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(profile.LoginName))
        {
            return profile.LoginName.Trim();
        }

        return $"\u05DE\u05E9\u05EA\u05DE\u05E9 #{profile.UserId}";
    }
}
