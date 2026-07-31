using System.Text;
using System.Text.Json;

namespace SiNet.Application.Diagnostics;

/// <summary>TEMP debug-mode NDJSON writer for session 65884a (body PDF soak). Remove after fix verified.</summary>
public static class AgentDebugNdjson
{
    private const string SessionId = "65884a";
    private const string LogPath = @"D:\repos2026\SiNetProjectManager_GitHub\debug-65884a.log";
    private static readonly object Gate = new();

    public static void Write(
        string hypothesisId,
        string location,
        string message,
        object? data = null,
        string runId = "pre-fix")
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                sessionId = SessionId,
                runId,
                hypothesisId,
                location,
                message,
                data,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });

            lock (Gate)
            {
                File.AppendAllText(LogPath, payload + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnostics must never destabilize ingest.
        }
    }
}
