using System.IO;
using System.Text.Json;
using Serilog;

namespace SiNetProjectManagerV2;

/// <summary>
/// Manages persistence of management-level settings.
/// These settings are stored separately from regular user settings
/// and should only be modified by administrators.
/// </summary>
public static class ManagementSettingsManager
{
    private static readonly string SettingsPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "management_settings.json");

    /// <summary>
    /// Loads management settings from file. Returns defaults if file doesn't exist.
    /// </summary>
    public static ManagementSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<ManagementSettings>(json) ?? new ManagementSettings();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Operation {Operation} failed. Context={@Context}",
                "LoadManagementSettings", new { FilePath = SettingsPath });
        }
        return new ManagementSettings();
    }

    /// <summary>
    /// Saves management settings to file.
    /// </summary>
    public static void SaveSettings(ManagementSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Operation {Operation} failed. Context={@Context}",
                "SaveManagementSettings", new { FilePath = SettingsPath });
            throw;
        }
    }
}
