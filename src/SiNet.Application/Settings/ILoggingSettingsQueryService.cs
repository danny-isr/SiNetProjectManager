namespace SiNet.Application.Settings;

/// <summary>Read global centralized logging settings from the database.</summary>
public interface ILoggingSettingsQueryService
{
    Task<CentralLoggingSettingsDto> GetCentralLoggingAsync(CancellationToken cancellationToken = default);
}
