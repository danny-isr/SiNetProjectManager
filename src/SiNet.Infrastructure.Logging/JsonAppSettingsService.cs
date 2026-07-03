using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SiNet.Application.Settings;

namespace SiNet.Infrastructure.Logging;

/// <summary>
/// Reads/writes all per-user fields in <c>%LOCALAPPDATA%\SiNetProjectManagerV2\settings.json</c>.
/// Preserves unknown JSON properties (merge write).
/// </summary>
public sealed class JsonAppSettingsService : IAppSettingsService
{
    private const string AppFolderName = "SiNetProjectManagerV2";
    private const string SettingsFileName = "settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public string UserSettingsFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName,
        SettingsFileName);

    public Task<UserAppSettingsDto> GetUserAppSettingsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReadDto(UserSettingsFilePath));
    }

    public Task SaveUserAppSettingsAsync(
        UserAppSettingsDto settings,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(settings);
        WriteDto(UserSettingsFilePath, settings);
        return Task.CompletedTask;
    }

    public async Task<UserLoggingSettingsDto> GetUserLoggingSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        var all = await GetUserAppSettingsAsync(cancellationToken).ConfigureAwait(false);
        return all.Logging;
    }

    public async Task SaveUserLoggingSettingsAsync(
        UserLoggingSettingsDto settings,
        CancellationToken cancellationToken = default)
    {
        var all = await GetUserAppSettingsAsync(cancellationToken).ConfigureAwait(false);
        await SaveUserAppSettingsAsync(
            all with { Logging = settings with
            {
                BootstrapDefaultLocalLogDirectory = LoggingSettingsMetadata.BootstrapDefaultLocalLogDirectory,
                AppLoggerDefaultLocalLogDirectory = LoggingSettingsMetadata.AppLoggerDefaultLocalLogDirectory,
            }},
            cancellationToken).ConfigureAwait(false);
    }

    internal static UserAppSettingsDto ReadDto(string settingsFilePath)
    {
        if (!File.Exists(settingsFilePath))
        {
            return CreateDefaultDto();
        }

        try
        {
            var json = File.ReadAllText(settingsFilePath);
            var node = JsonNode.Parse(json);
            if (node is not JsonObject obj)
            {
                return CreateDefaultDto();
            }

            return MapFromJson(obj);
        }
        catch
        {
            return CreateDefaultDto();
        }
    }

    internal static void WriteDto(string settingsFilePath, UserAppSettingsDto settings)
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

        ApplyToJson(root, settings);
        File.WriteAllText(settingsFilePath, root.ToJsonString(JsonOptions));
    }

    internal static UserAppSettingsDto CreateDefaultDto()
    {
        var defaults = UserAppSettingsDefaults.Create();
        return defaults with
        {
            Logging = defaults.Logging with
            {
                BootstrapDefaultLocalLogDirectory = LoggingSettingsMetadata.BootstrapDefaultLocalLogDirectory,
                AppLoggerDefaultLocalLogDirectory = LoggingSettingsMetadata.AppLoggerDefaultLocalLogDirectory,
            },
        };
    }

    internal static UserAppSettingsDto MapFromJson(JsonObject obj)
    {
        var defaults = CreateDefaultDto();

        return defaults with
        {
            Appearance = defaults.Appearance with
            {
                FontFamily = GetString(obj, "FontFamily", defaults.Appearance.FontFamily),
                FontSize = GetDouble(obj, "FontSize", defaults.Appearance.FontSize),
                ForegroundColor = GetString(obj, "ForegroundColor", defaults.Appearance.ForegroundColor),
                BackgroundColor = GetString(obj, "BackgroundColor", defaults.Appearance.BackgroundColor),
            },
            Behavior = defaults.Behavior with
            {
                AllowMultipleInstances = GetBool(obj, "AllowMultipleInstances", defaults.Behavior.AllowMultipleInstances),
            },
            Logging = defaults.Logging with
            {
                LoggingEnabled = GetBool(obj, "LoggingEnabled", GetBool(obj, "loggingEnabled", defaults.Logging.LoggingEnabled)),
                LogDirectory = GetOptionalString(obj, "LogDirectory") ?? GetOptionalString(obj, "logDirectory"),
            },
            FloatingOpacity = defaults.FloatingOpacity with
            {
                ActiveOpacity = ClampOpacity(GetDouble(obj, "FloatingWindowActiveOpacity", defaults.FloatingOpacity.ActiveOpacity)),
                IdleOpacity = ClampOpacity(GetDouble(obj, "FloatingWindowIdleOpacity", defaults.FloatingOpacity.IdleOpacity)),
            },
            FloatingTasks = ReadGeometry(obj, "FloatingTasks", defaults.FloatingTasks),
            FloatingInspection = ReadGeometry(obj, "FloatingInspection", defaults.FloatingInspection),
            EnableAuthorizationTestMode = GetBool(obj, "EnableAuthorizationTestMode", defaults.EnableAuthorizationTestMode),
        };
    }

    internal static void ApplyToJson(JsonObject root, UserAppSettingsDto settings)
    {
        root["FontFamily"] = settings.Appearance.FontFamily;
        root["FontSize"] = settings.Appearance.FontSize;
        root["ForegroundColor"] = settings.Appearance.ForegroundColor;
        root["BackgroundColor"] = settings.Appearance.BackgroundColor;
        root["AllowMultipleInstances"] = settings.Behavior.AllowMultipleInstances;
        root["LoggingEnabled"] = settings.Logging.LoggingEnabled;
        root["logDirectory"] = settings.Logging.LogDirectory ?? string.Empty;
        root["LogDirectory"] = settings.Logging.LogDirectory ?? string.Empty;
        root["FloatingWindowActiveOpacity"] = settings.FloatingOpacity.ActiveOpacity;
        root["FloatingWindowIdleOpacity"] = settings.FloatingOpacity.IdleOpacity;
        WriteGeometry(root, "FloatingTasks", settings.FloatingTasks);
        WriteGeometry(root, "FloatingInspection", settings.FloatingInspection);
        root["EnableAuthorizationTestMode"] = settings.EnableAuthorizationTestMode;
    }

    private static FloatingWindowGeometryDto ReadGeometry(
        JsonObject obj,
        string prefix,
        FloatingWindowGeometryDto defaults)
        => new(
            GetDouble(obj, prefix + "Top", defaults.Top),
            GetDouble(obj, prefix + "Left", defaults.Left),
            GetDouble(obj, prefix + "Width", defaults.Width),
            GetDouble(obj, prefix + "Height", defaults.Height));

    private static void WriteGeometry(JsonObject root, string prefix, FloatingWindowGeometryDto geometry)
    {
        root[prefix + "Top"] = geometry.Top;
        root[prefix + "Left"] = geometry.Left;
        root[prefix + "Width"] = geometry.Width;
        root[prefix + "Height"] = geometry.Height;
    }

    private static string GetString(JsonObject obj, string key, string fallback)
        => obj[key]?.GetValue<string>() ?? fallback;

    private static string? GetOptionalString(JsonObject obj, string key)
    {
        var value = obj[key]?.GetValue<string>();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool GetBool(JsonObject obj, string key, bool fallback)
        => obj[key]?.GetValue<bool>() ?? fallback;

    private static double GetDouble(JsonObject obj, string key, double fallback)
    {
        var node = obj[key];
        if (node is null)
        {
            return fallback;
        }

        if (node.GetValueKind() == JsonValueKind.String
            && string.Equals(node.GetValue<string>(), "NaN", StringComparison.OrdinalIgnoreCase))
        {
            return double.NaN;
        }

        return node.GetValue<double>();
    }

    private static double ClampOpacity(double value) => Math.Clamp(value, 0.1, 1.0);
}
