using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace SiNet.Application.Diagnostics;

/// <summary>
/// Optional workflow step logger. Disabled by default in all builds; enable only with
/// <c>SINET_WF_DEBUG=1</c> for local diagnostics. Writes to
/// <c>%LOCALAPPDATA%\&lt;company&gt;\&lt;product&gt;\Logs\workflow-manual-debug.log</c>
/// and mirrors to <see cref="Trace"/>. Never throws.
/// </summary>
public static class WorkflowDebugTrace
{
    private static readonly object Gate = new();
    private static readonly Lazy<string> LogFilePath = new(ResolveLogFilePath);
    private static bool? _enabledOverride;

    /// <summary>Master on/off. Default: on only when <c>SINET_WF_DEBUG</c> is set to a truthy value.</summary>
    public static bool Enabled
    {
        get => _enabledOverride ?? ResolveDefaultEnabled();
        set => _enabledOverride = value;
    }

    /// <summary>Absolute path of the dedicated debug log file.</summary>
    public static string FilePath => LogFilePath.Value;

    /// <summary>
    /// Writes one <c>[WF-STEP]</c> line when <see cref="Enabled"/>. Never throws.
    /// </summary>
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
            }
        }
        catch
        {
            // Best-effort file sink.
        }
    }

    private static bool ResolveDefaultEnabled()
    {
        var raw = Environment.GetEnvironmentVariable("SINET_WF_DEBUG");
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        return !(raw.Equals("0", StringComparison.OrdinalIgnoreCase)
                 || raw.Equals("false", StringComparison.OrdinalIgnoreCase)
                 || raw.Equals("off", StringComparison.OrdinalIgnoreCase));
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
            return Path.Combine(Path.GetTempPath(), fileName);
        }
    }

    private static string ResolveLogDirectory()
    {
        try
        {
            var entry = Assembly.GetEntryAssembly();
            var company = entry?.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;
            if (string.IsNullOrWhiteSpace(company))
                company = "SiNet";
            var product = entry?.GetName().Name ?? "SiNet.App.Wpf";
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, company, product, "Logs");
        }
        catch
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "SiNet", "SiNet.App.Wpf", "Logs");
        }
    }
}
