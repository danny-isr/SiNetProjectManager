namespace SiNet.Application.Settings;

/// <summary>
/// Per-user application settings port (<c>%LOCALAPPDATA%\SiNetProjectManagerV2\settings.json</c>).
/// </summary>
public interface IAppSettingsService
{
    /// <summary>Path to the user settings file.</summary>
    string UserSettingsFilePath { get; }

    Task<UserAppSettingsDto> GetUserAppSettingsAsync(CancellationToken cancellationToken = default);

    Task SaveUserAppSettingsAsync(
        UserAppSettingsDto settings,
        CancellationToken cancellationToken = default);

    /// <summary>Logging slice (convenience over <see cref="GetUserAppSettingsAsync"/>).</summary>
    Task<UserLoggingSettingsDto> GetUserLoggingSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>Logging slice (convenience over <see cref="SaveUserAppSettingsAsync"/>).</summary>
    Task SaveUserLoggingSettingsAsync(
        UserLoggingSettingsDto settings,
        CancellationToken cancellationToken = default);
}
