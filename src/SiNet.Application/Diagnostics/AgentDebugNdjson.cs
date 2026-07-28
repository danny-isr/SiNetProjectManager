using System.Text.Json;

namespace SiNet.Application.Diagnostics;

/// <summary>
/// Temporary NDJSON debug ingest for Cursor debug sessions. Do not log secrets.
/// </summary>
public static class AgentDebugNdjson
{
    private const string SessionId = "65884a";
    private const string LogPath = @"D:\repos2026\SiNetProjectManager_GitHub\debug-65884a.log";

    public static void Write(
        string hypothesisId,
        string location,
        string message,
        IReadOnlyDictionary<string, object?>? data = null,
        string runId = "acc-ingest-debug")
    {
        // #region agent log
        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["sessionId"] = SessionId,
                ["runId"] = runId,
                ["hypothesisId"] = hypothesisId,
                ["location"] = location,
                ["message"] = message,
                ["data"] = data ?? new Dictionary<string, object?>(),
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
            File.AppendAllText(LogPath, JsonSerializer.Serialize(payload) + Environment.NewLine);
        }
        catch
        {
            // Debug ingest must never break production paths.
        }
        // #endregion
    }
}
