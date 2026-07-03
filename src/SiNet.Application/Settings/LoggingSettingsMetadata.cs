namespace SiNet.Application.Settings;

/// <summary>Canonical local log paths (see <c>docs/SETTINGS.md</c>).</summary>
public static class LoggingSettingsMetadata
{
    public static string BootstrapDefaultLocalLogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SiNet",
        "SiNetProjectManagerV2",
        "Logs");

    public static string AppLoggerDefaultLocalLogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SiNetProjectManager",
        "Logs");
}
