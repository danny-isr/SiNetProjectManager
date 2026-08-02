namespace SiNet.Application.Diagnostics;

/// <summary>
/// Retired temporary NDJSON debug sink. Kept as a no-op so existing call sites compile
/// without writing to disk. Call sites may be removed in a later cleanup pass.
/// Status: inactive / pending removal — do not reintroduce a hard-coded log path.
/// </summary>
public static class AgentDebugNdjson
{
    public static void Write(
        string hypothesisId,
        string location,
        string message,
        object? data = null,
        string runId = "pre-fix")
    {
        // Intentionally no-op — production builds must not write session debug files.
    }
}
