namespace SiNet.Application.Settings;

/// <summary>Read global/admin settings from <c>dbo.SystemSettings</c>.</summary>
public interface ISystemSettingsQueryService
{
    Task<SystemSettingsDto> GetSystemSettingsAsync(CancellationToken cancellationToken = default);
}
