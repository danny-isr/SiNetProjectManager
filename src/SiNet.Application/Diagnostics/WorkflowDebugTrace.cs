// TEMP WF-DEBUG — temporary manual-test instrumentation. Remove after the manual test pass
// (grep the whole solution for "TEMP WF-DEBUG"), or silence at runtime with SINET_WF_DEBUG=0.
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace SiNet.Application.Diagnostics;

/// <summary>
/// TEMP WF-DEBUG. Lightweight, self-contained step logger for the manual workflow test phase.
/// Writes marked <c>[WF-STEP]</c> lines to a DEDICATED file (<c>workflow-manual-debug.log</c>) next to
/// the app's normal Logs folder, and mirrors each line to <see cref="Trace"/> for the VS Output window.
/// It is intentionally independent of the app's Serilog pipeline and its <c>LoggingEnabled</c> toggle so
/// it can be added/removed without touching production logging.
/// <para>
/// Gated by <see cref="Enabled"/>: defaults to the <c>SINET_WF_DEBUG</c> environment variable when set
/// (<c>0</c>/<c>false</c> = off, anything else = on), otherwise on in DEBUG builds and off in RELEASE.
/// </para>
/// </summary>
public static class WorkflowDebugTrace
{
    private static readonly object Gate = new();
    private static readonly Lazy<string> LogFilePath = new(ResolveLogFilePath);
    private const string AgentDebugSessionId = "cbfc8f";
    private const string AgentDebugLogPath = @"D:\repos2026\debug-cbfc8f.log";
    private static bool? _enabledOverride;

    /// <summary>Master on/off switch. See class remarks for the default resolution.</summary>
    public static bool Enabled
    {
        get => _enabledOverride ?? ResolveDefaultEnabled();
        set => _enabledOverride = value;
    }

    /// <summary>Absolute path of the dedicated debug log file (for the runbook / diagnostics).</summary>
    public static string FilePath => LogFilePath.Value;

    /// <summary>
    /// Writes one <c>[WF-STEP]</c> line: <c>[WF-STEP] {utcNow:O} {area} | {message}</c>. Never throws.
    /// </summary>
    /// <param name="area">Short subsystem tag, e.g. <c>Engine.Start</c>, <c>Orchestrator.AutoAdvance</c>.</param>
    /// <param name="message">Correlating detail (ids, stage codes, outcomes).</param>
    public static void Step(string area, string message)
    {
        if (!Enabled)
            return;

        var line = $"[WF-STEP] {DateTime.UtcNow:O} T{Environment.CurrentManagedThreadId} {area} | {message}";

        try
        {
            Trace.TraceInformation(line);
        }
        catch
        {
            // Diagnostics must never destabilize the app.
        }

        try
        {
            lock (Gate)
            {
                File.AppendAllText(LogFilePath.Value, line + Environment.NewLine, Encoding.UTF8);

                // #region agent log
                var hypothesisId = ResolveAgentHypothesis(area);
                var payload = JsonSerializer.Serialize(new
                {
                    sessionId = AgentDebugSessionId,
                    runId = "workflow-manual-pre-fix",
                    hypothesisId,
                    location = $"WorkflowDebugTrace.Step/{area}",
                    message = area,
                    data = new { detail = message },
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                });
                File.AppendAllText(AgentDebugLogPath, payload + Environment.NewLine, Encoding.UTF8);
                // #endregion
            }
        }
        catch
        {
            // Best-effort file sink; ignore IO failures (locked file, missing dir, etc.).
        }
    }

    // #region agent log
    private static string ResolveAgentHypothesis(string area)
    {
        if (area.StartsWith("ProjectWork.AccOpen", StringComparison.Ordinal)
            || area.StartsWith("ProjectWork.AccUrl", StringComparison.Ordinal))
            return "H-A";

        if (area.StartsWith("ProjectWork.LoadTree", StringComparison.Ordinal))
            return "H-B";

        if (area.StartsWith("Tasks.FloatWindow", StringComparison.Ordinal))
            return "H-C";

        if (area.StartsWith("ProjectWork.Results", StringComparison.Ordinal))
            return "H-D";

        if (area.StartsWith("ProjectWork.AccTabUi", StringComparison.Ordinal)
            || area.StartsWith("ProjectWork.AccPopOut", StringComparison.Ordinal))
            return "H-E";

        if (area.StartsWith("Acc.Provision", StringComparison.Ordinal)
            || area.StartsWith("Review.", StringComparison.Ordinal)
            || area.StartsWith("Email.File", StringComparison.Ordinal)
            || area.StartsWith("Email.Move", StringComparison.Ordinal))
            return "H3";

        if (area.StartsWith("Email.", StringComparison.Ordinal)
            || area.StartsWith("Engine.Start", StringComparison.Ordinal)
            || area.StartsWith("Provisioning.", StringComparison.Ordinal))
            return "H1";

        if (area.StartsWith("TaskCompletion.", StringComparison.Ordinal)
            || area.StartsWith("Evaluator.", StringComparison.Ordinal)
            || area.StartsWith("Orchestrator.", StringComparison.Ordinal)
            || area.StartsWith("Engine.Advance", StringComparison.Ordinal)
            || area.StartsWith("WorkflowSeed.", StringComparison.Ordinal)
            || area.StartsWith("Watchdog.", StringComparison.Ordinal))
            return "H2";

        if (area.StartsWith("Workbench.", StringComparison.Ordinal)
            || area.StartsWith("Engine.Pause", StringComparison.Ordinal)
            || area.StartsWith("Engine.Resume", StringComparison.Ordinal))
            return "H4";

        if (area.StartsWith("Watchdog.", StringComparison.Ordinal))
            return "H5";

        return "H2";
    }
    // #endregion

    private static bool ResolveDefaultEnabled()
    {
        var raw = Environment.GetEnvironmentVariable("SINET_WF_DEBUG");
        if (!string.IsNullOrWhiteSpace(raw))
        {
            return !(raw.Equals("0", StringComparison.OrdinalIgnoreCase)
                     || raw.Equals("false", StringComparison.OrdinalIgnoreCase)
                     || raw.Equals("off", StringComparison.OrdinalIgnoreCase));
        }

#if DEBUG
        return true;
#else
        return false;
#endif
    }

    private static string ResolveLogFilePath()
    {
        const string fileName = "workflow-manual-debug.log";
        try
        {
            var directory = ResolveLogDirectory();
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, fileName);
        }
        catch
        {
            // Fall back to the temp folder if the LocalAppData path is unavailable.
            return Path.Combine(Path.GetTempPath(), fileName);
        }
    }

    private static string ResolveLogDirectory()
    {
        // Mirror SiNetProjectManagerV2.App.GetLogDirectory so the file sits next to the app logs.
        try
        {
            var entry = Assembly.GetEntryAssembly();
            var company = entry?.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;
            if (string.IsNullOrWhiteSpace(company))
                company = "SiNet";
            var product = entry?.GetName().Name ?? "SiNetProjectManagerV2";
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, company, product, "Logs");
        }
        catch
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "SiNet", "SiNetProjectManagerV2", "Logs");
        }
    }
}
