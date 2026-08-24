using System.IO;
using System.Text;
using System.Text.Json;

namespace SiNet.App.Wpf.Tests.Live;

/// <summary>
/// Append-only step log for the L4W P0 Pilot write smoke. Writes a markdown report the operator can
/// paste into <c>docs/PILOT_CONTROLS.md</c> plus a JSON sibling for machine reading.
/// <para>
/// Flushed after every step so a crash or a hard process kill still leaves the created ids and the
/// restore state on disk — that is the only trail for the artifacts the harness cannot delete
/// itself (see <c>docs/TEST_STRATEGY.md</c> §4W.4).
/// </para>
/// </summary>
internal sealed class PilotSmokeEvidence
{
    private readonly List<Step> _steps = [];
    private readonly List<string> _manualCleanup = [];
    private readonly Dictionary<string, string> _facts = [];
    private readonly string _markdownPath;
    private readonly string _jsonPath;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;

    private PilotSmokeEvidence(string markdownPath, string jsonPath)
    {
        _markdownPath = markdownPath;
        _jsonPath = jsonPath;
    }

    public string MarkdownPath => _markdownPath;

    public static PilotSmokeEvidence Create()
    {
        var directory = Environment.GetEnvironmentVariable("SINET_PILOT_SMOKE_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SiNet",
                "pilot-smoke");
        }

        Directory.CreateDirectory(directory);
        var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        return new PilotSmokeEvidence(
            Path.Combine(directory, $"p0-pilot-smoke-{stamp}.md"),
            Path.Combine(directory, $"p0-pilot-smoke-{stamp}.json"));
    }

    /// <summary>Records an environment or configuration fact (target database, resolved ids, settings).</summary>
    public void Fact(string name, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _facts[name] = value ?? "<null>";
        Flush();
    }

    public void Pass(string step, string detail) => Add(step, "Pass", detail);

    public void Fail(string step, string detail) => Add(step, "Fail", detail);

    public void Skipped(string step, string detail) => Add(step, "Skipped", detail);

    public void NotRun(string step, string detail) => Add(step, "Not Run", detail);

    /// <summary>
    /// Records an artifact the harness cannot remove — uploaded ACC items/versions (the application
    /// only soft-deletes via <c>HideAsync</c>) and the disposable ACC projects.
    /// </summary>
    public void RequiresManualCleanup(string what)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(what);
        _manualCleanup.Add(what);
        Flush();
    }

    private void Add(string step, string result, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(step);
        _steps.Add(new Step(step, result, detail, DateTimeOffset.Now));
        Flush();
    }

    private void Flush()
    {
        File.WriteAllText(_markdownPath, BuildMarkdown(), Encoding.UTF8);
        File.WriteAllText(
            _jsonPath,
            JsonSerializer.Serialize(
                new { StartedAt = _startedAt, Facts = _facts, Steps = _steps, ManualCleanup = _manualCleanup },
                new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);
    }

    private string BuildMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# P0 Pilot Live Smoke — automated evidence");
        sb.AppendLine();
        sb.AppendLine($"> Started: {_startedAt:yyyy-MM-dd HH:mm:ss zzz}  ");
        sb.AppendLine("> Tier: L4W `Category=PilotSmoke` (docs/TEST_STRATEGY.md §4W)");
        sb.AppendLine();

        sb.AppendLine("## Environment");
        sb.AppendLine();
        sb.AppendLine("| Fact | Value |");
        sb.AppendLine("| --- | --- |");
        foreach (var (name, value) in _facts)
        {
            sb.AppendLine($"| {name} | {Escape(value)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Steps");
        sb.AppendLine();
        sb.AppendLine("| Time | Step | Result | Detail |");
        sb.AppendLine("| --- | --- | --- | --- |");
        foreach (var step in _steps)
        {
            sb.AppendLine(
                $"| {step.At:HH:mm:ss} | {Escape(step.Name)} | **{step.Result}** | {Escape(step.Detail)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Manual cleanup required");
        sb.AppendLine();
        if (_manualCleanup.Count == 0)
        {
            sb.AppendLine("None recorded.");
        }
        else
        {
            sb.AppendLine("The harness cannot remove these. ACC items and versions are only soft-deleted");
            sb.AppendLine("by the application (`HideAsync`), so removal needs the ACC Admin Console.");
            sb.AppendLine();
            foreach (var item in _manualCleanup)
            {
                sb.AppendLine($"- {item}");
            }
        }

        return sb.ToString();
    }

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);

    private sealed record Step(string Name, string Result, string Detail, DateTimeOffset At);
}
