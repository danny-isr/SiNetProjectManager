namespace SiNet.Application.Settings;

/// <summary>Admin write path for global settings (requires <c>System.Settings.Write</c>).</summary>
public interface ISystemSettingsCommandService
{
    Task SaveSystemSettingsAsync(
        SystemSettingsDto settings,
        CancellationToken cancellationToken = default);
}
