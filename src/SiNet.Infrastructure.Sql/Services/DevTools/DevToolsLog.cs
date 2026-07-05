namespace SiNet.Infrastructure.Sql.Services.DevTools;

/// <summary>Lightweight logging for DEBUG dev-tools operations (no SiNetSQL AppLogger dependency).</summary>
internal static class DevToolsLog
{
    public static void Info(string message) => System.Diagnostics.Debug.WriteLine(message);

    public static void Warn(string message) => System.Diagnostics.Debug.WriteLine("[WARN] " + message);

    public static void Error(Exception ex, string message) =>
        System.Diagnostics.Debug.WriteLine($"[ERROR] {message}: {ex.Message}");
}
