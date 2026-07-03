using System.Text.Json;
using System.Text.Json.Nodes;
using SiNet.Application.Settings;

namespace SiNet.Infrastructure.Logging;

/// <summary>
/// Reads/writes per-user logging fields in <c>%LOCALAPPDATA%\SiNetProjectManagerV2\settings.json</c>
/// without referencing legacy <c>AppSettings</c> / <c>SettingsManager</c>.
/// </summary>
public sealed class JsonUserLoggingSettingsService : IAppSettingsService
{
    private const string AppFolderName = "SiNetProjectManagerV2";
    private const string SettingsFileName = "settings.json";
    private const string LoggingEnabledProperty = "loggingEnabled";
    private const string LogDirectoryProperty = "logDirectory";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public string UserSettingsFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName,
        SettingsFileName);

    public Task<UserLoggingSettingsDto> GetUserLoggingSettingsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (loggingEnabled, logDirectory) = ReadLoggingFields(UserSettingsFilePath);
        return Task.FromResult(CreateDto(loggingEnabled, logDirectory));
    }

    public Task SaveUserLoggingSettingsAsync(
        UserLoggingSettingsDto settings,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(settings);

        WriteLoggingFields(
            UserSettingsFilePath,
            settings.LoggingEnabled,
            settings.LogDirectory);

        return Task.CompletedTask;
    }

    internal static UserLoggingSettingsDto CreateDto(bool loggingEnabled, string? logDirectory)
        => new(
            loggingEnabled,
            string.IsNullOrWhiteSpace(logDirectory) ? null : logDirectory.Trim(),
            LoggingSettingsPaths.BootstrapDefaultLocalLogDirectory,
            LoggingSettingsPaths.AppLoggerDefaultLocalLogDirectory);

    internal static (bool LoggingEnabled, string? LogDirectory) ReadLoggingFields(string settingsFilePath)
    {
        if (!File.Exists(settingsFilePath))
        {
            return (false, null);
        }

        try
        {
            var json = File.ReadAllText(settingsFilePath);
            var node = JsonNode.Parse(json);
            if (node is not JsonObject obj)
            {
                return (false, null);
            }

            var enabled = obj[LoggingEnabledProperty]?.GetValue<bool>() ?? false;
            var directory = obj[LogDirectoryProperty]?.GetValue<string>();
            return (enabled, directory);
        }
        catch
        {
            return (false, null);
        }
    }

    internal static void WriteLoggingFields(string settingsFilePath, bool loggingEnabled, string? logDirectory)
    {
        var directory = Path.GetDirectoryName(settingsFilePath)
            ?? throw new InvalidOperationException("Invalid settings file path.");
        Directory.CreateDirectory(directory);

        JsonObject root;
        if (File.Exists(settingsFilePath))
        {
            var existing = File.ReadAllText(settingsFilePath);
            root = JsonNode.Parse(existing) as JsonObject ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        root[LoggingEnabledProperty] = loggingEnabled;
        root[LogDirectoryProperty] = string.IsNullOrWhiteSpace(logDirectory) ? string.Empty : logDirectory.Trim();

        File.WriteAllText(settingsFilePath, root.ToJsonString(JsonOptions));
    }
}

/// <summary>Canonical local log directory paths documented in <c>docs/SETTINGS.md</c>.</summary>
public static class LoggingSettingsPaths
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
