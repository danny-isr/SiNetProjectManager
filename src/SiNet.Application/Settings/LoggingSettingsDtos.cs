namespace SiNet.Application.Settings;

/// <summary>Serilog-compatible log levels for settings DTOs (no Serilog dependency).</summary>
public enum LogLevelDto
{
    Verbose,
    Debug,
    Information,
    Warning,
    Error,
    Fatal,
}

/// <summary>Per-user logging settings stored in <c>settings.json</c>.</summary>
/// <param name="LoggingEnabled">When true, local file sink allows Debug+ (via host FileLevelSwitch).</param>
/// <param name="LogDirectory">Custom local log folder; null/empty = use bootstrap default.</param>
/// <param name="BootstrapDefaultLocalLogDirectory">Path used by host Serilog bootstrap (read-only hint).</param>
/// <param name="AppLoggerDefaultLocalLogDirectory">Legacy AppLogger default path (read-only hint).</param>
public sealed record UserLoggingSettingsDto(
    bool LoggingEnabled,
    string? LogDirectory,
    string BootstrapDefaultLocalLogDirectory,
    string AppLoggerDefaultLocalLogDirectory)
{
    /// <summary>Resolved directory for display (custom path or bootstrap default).</summary>
    public string EffectiveLocalLogDirectory =>
        string.IsNullOrWhiteSpace(LogDirectory) ? BootstrapDefaultLocalLogDirectory : LogDirectory.Trim();
}

/// <summary>File + central minimum levels for one app in the centralized logging layout.</summary>
public sealed record AppLogLevelsDto(
    LogLevelDto FileLevel,
    LogLevelDto CentralLevel);

/// <summary>
/// Global centralized logging settings from <c>dbo.SystemSettings</c> (<c>Logging.*</c> keys).
/// Changes require application restart (except WPF local verbosity via user toggle).
/// </summary>
public sealed record CentralLoggingSettingsDto(
    string? CentralLogPath,
    int LocalRetentionDays,
    int CentralRetentionDays,
    AppLogLevelsDto Client,
    AppLogLevelsDto AccService,
    AppLogLevelsDto SyncEngine,
    bool CentralLoggingEnabled)
{
    public const string RequiresRestartMessage =
        "שינויי הגדרות לוג מרכזי/שרת נכנסים לתוקף בהפעלה מחדש של האפליקציה.";
}
