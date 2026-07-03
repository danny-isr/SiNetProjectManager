namespace SiNet.Application.Settings;

/// <summary>
/// Well-known <c>SystemSettings.SettingKey</c> values for the centralized logging pipeline.
/// Mirrors legacy <c>SystemSettingKeys.Logging*</c> without referencing SiNetSQL.
/// </summary>
public static class LoggingSettingKeys
{
    public const string CentralLogPath = "Logging.CentralLogPath";
    public const string LocalRetentionDays = "Logging.LocalRetentionDays";
    public const string CentralRetentionDays = "Logging.CentralRetentionDays";
    public const string ClientFileLevel = "Logging.Client.FileLevel";
    public const string ClientCentralLevel = "Logging.Client.CentralLevel";
    public const string AccServiceFileLevel = "Logging.AccService.FileLevel";
    public const string AccServiceCentralLevel = "Logging.AccService.CentralLevel";
    public const string SyncEngineFileLevel = "Logging.SyncEngine.FileLevel";
    public const string SyncEngineCentralLevel = "Logging.SyncEngine.CentralLevel";

    public static IReadOnlyList<string> All { get; } =
    [
        CentralLogPath,
        LocalRetentionDays,
        CentralRetentionDays,
        ClientFileLevel,
        ClientCentralLevel,
        AccServiceFileLevel,
        AccServiceCentralLevel,
        SyncEngineFileLevel,
        SyncEngineCentralLevel,
    ];
}
