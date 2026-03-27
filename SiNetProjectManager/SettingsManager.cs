using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace SiNetProjectManager
{
    public static class SettingsManager
    {
        private static readonly string settingsPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public static AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(settingsPath))
                {
                    var json = File.ReadAllText(settingsPath);
                    var options = new JsonSerializerOptions
                    {
                        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
                    };
                    return JsonSerializer.Deserialize<AppSettings>(json, options) ?? new AppSettings();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Operation {Operation} failed. Context={@Context}",
                    "LoadSettings", new { FilePath = settingsPath });
            }
            return new AppSettings();
        }

        public static void SaveSettings(AppSettings settings)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
                });
                File.WriteAllText(settingsPath, json);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Operation {Operation} failed. Context={@Context}",
                    "SaveSettings", new { FilePath = settingsPath });
                throw;
            }
        }
    }

}
