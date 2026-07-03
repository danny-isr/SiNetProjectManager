namespace SiNet.Application.Settings;

/// <summary>
/// Per-user application settings port. Stage 5 exposes the <b>logging slice</b> first; theme/layout
/// fields will extend this interface in a later slice (see <c>docs/SETTINGS.md</c>).
/// </summary>
public interface IAppSettingsService
{
    /// <summary>Path to the user settings file (<c>%LOCALAPPDATA%\SiNetProjectManagerV2\settings.json</c>).</summary>
    string UserSettingsFilePath { get; }

    Task<UserLoggingSettingsDto> GetUserLoggingSettingsAsync(CancellationToken cancellationToken = default);

    Task SaveUserLoggingSettingsAsync(
        UserLoggingSettingsDto settings,
        CancellationToken cancellationToken = default);
}
