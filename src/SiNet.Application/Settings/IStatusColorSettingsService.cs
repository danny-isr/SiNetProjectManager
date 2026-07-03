namespace SiNet.Application.Settings;

/// <summary>
/// Status color overrides (per-user) and global defaults (admin). Backed by
/// <c>UserStatusPreferences</c> and <c>ProjectAssignmentStatuses</c> — not SystemSettings.
/// </summary>
public interface IStatusColorSettingsService
{
    Task<IReadOnlyList<UserStatusColorEntryDto>> GetUserStatusColorsAsync(
        int userId,
        CancellationToken cancellationToken = default);

    Task SetUserOverrideAsync(
        int userId,
        int statusId,
        string colorHex,
        CancellationToken cancellationToken = default);

    Task RemoveUserOverrideAsync(
        int userId,
        int statusId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GlobalStatusColorEntryDto>> GetGlobalStatusColorsAsync(
        CancellationToken cancellationToken = default);

    Task SetGlobalDefaultColorAsync(
        int statusId,
        string? colorHex,
        CancellationToken cancellationToken = default);
}
