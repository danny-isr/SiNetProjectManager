namespace SiNet.Application.Settings;

/// <summary>Admin write path for global centralized logging settings (DB <c>Logging.*</c> keys).</summary>
public interface ILoggingSettingsCommandService
{
    Task SaveCentralLoggingAsync(
        CentralLoggingSettingsDto settings,
        CancellationToken cancellationToken = default);

    /// <summary>Best-effort write probe for a UNC/local central log path (admin diagnostics).</summary>
    Task<bool> ProbeCentralLogPathAsync(string path, CancellationToken cancellationToken = default);
}
