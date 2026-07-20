using System.Text.Json;

namespace SiNet.Application.Diagnostics;

/// <summary>TEMP debug-session NDJSON writer for agent runtime evidence. Session cbfc8f.</summary>
public static class AgentDebugNdjson
{
    private const string SessionId = "cbfc8f";
    private static readonly string LogPath = ResolveLogPath();
    private static readonly object Gate = new();

    private static string ResolveLogPath()
    {
        // Session log must land at workspace root (d:\repos2026\debug-cbfc8f.log).
        var forced = Path.Combine(@"D:\repos2026", "debug-cbfc8f.log");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(forced)!);
            return forced;
        }
        catch
        {
            return Path.Combine(AppContext.BaseDirectory, "debug-cbfc8f.log");
        }
    }

    public static void Write(string hypothesisId, string location, string message, object? data = null)
    {
        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["sessionId"] = SessionId,
                ["runId"] = Environment.GetEnvironmentVariable("SINET_DEBUG_RUN") ?? "post-fix",
                ["hypothesisId"] = hypothesisId,
                ["location"] = location,
                ["message"] = message,
                ["data"] = data,
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
            var line = JsonSerializer.Serialize(payload);
            lock (Gate)
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // never break product path
        }
    }
}
