using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace SiNetProjectManagerV2
{
    /// <summary>
    /// Manages application settings persistence.
    /// <para>
    /// Settings are stored in <c>%LOCALAPPDATA%\SiNetProjectManagerV2\settings.json</c>.
    /// This location is writeable even under MSIX/WindowsApps deployment where the
    /// application's BaseDirectory is read-only.
    /// </para>
    /// <para>
    /// For backward compatibility, if a <c>settings.json</c> exists next to the exe
    /// (legacy path) and no user-folder settings exist yet, the legacy file is read
    /// once and migrated. The legacy path is never written to.
    /// </para>
    /// </summary>
    public static class SettingsManager
    {
        private const string AppFolderName = "SiNetProjectManagerV2";
        private const string SettingsFileName = "settings.json";

        /// <summary>
        /// The user-writable settings path: %LOCALAPPDATA%\SiNetProjectManagerV2\settings.json
        /// </summary>
        private static readonly string _userSettingsPath;

        /// <summary>
        /// Legacy settings path next to the exe (read-only fallback for migration).
        /// </summary>
        private static readonly string _legacySettingsPath;

        /// <summary>
        /// True if the legacy path is under Program Files\WindowsApps (MSIX, read-only).
        /// </summary>
        private static readonly bool _legacyPathIsReadOnly;

        static SettingsManager()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(localAppData, AppFolderName);
            _userSettingsPath = Path.Combine(appFolder, SettingsFileName);

            _legacySettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SettingsFileName);

            // Detect if we're running under MSIX (WindowsApps folder is read-only)
            _legacyPathIsReadOnly = _legacySettingsPath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);

            Log.Information(
                "[SettingsManager] initialized — userPath={UserPath}, legacyPath={LegacyPath}, legacyIsReadOnly={LegacyIsReadOnly}.",
                _userSettingsPath, _legacySettingsPath, _legacyPathIsReadOnly);
        }

        /// <summary>
        /// Gets the actual settings file path being used.
        /// </summary>
        public static string SettingsFilePath => _userSettingsPath;

        public static AppSettings LoadSettings()
        {
            var options = new JsonSerializerOptions
            {
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };

            try
            {
                // 1. Try the user-folder path first (preferred location)
                if (File.Exists(_userSettingsPath))
                {
                    var json = File.ReadAllText(_userSettingsPath);
                    Log.Debug("[SettingsManager] Loaded settings from user folder: {Path}", _userSettingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json, options) ?? new AppSettings();
                }

                // 2. Fallback: read from legacy path (next to exe) for migration
                if (File.Exists(_legacySettingsPath))
                {
                    var json = File.ReadAllText(_legacySettingsPath);
                    Log.Information(
                        "[SettingsManager] Migrating settings from legacy path {LegacyPath} to {UserPath}.",
                        _legacySettingsPath, _userSettingsPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json, options) ?? new AppSettings();

                    // Migrate by saving to the new location
                    try
                    {
                        SaveSettings(settings);
                    }
                    catch (Exception saveEx)
                    {
                        Log.Warning(saveEx, "[SettingsManager] Migration save failed, will retry on next save.");
                    }

                    return settings;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Operation {Operation} failed. Context={@Context}",
                    "LoadSettings", new { UserPath = _userSettingsPath, LegacyPath = _legacySettingsPath });
            }

            return new AppSettings();
        }

        public static void SaveSettings(AppSettings settings)
        {
            try
            {
                // Ensure the user folder exists
                var folder = Path.GetDirectoryName(_userSettingsPath);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                    Log.Debug("[SettingsManager] Created settings folder: {Folder}", folder);
                }

                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
                });
                File.WriteAllText(_userSettingsPath, json);
                Log.Debug("[SettingsManager] Saved settings to: {Path}", _userSettingsPath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Operation {Operation} failed. Context={@Context}",
                    "SaveSettings", new { FilePath = _userSettingsPath });
                throw;
            }
        }
    }
}
