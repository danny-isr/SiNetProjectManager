using System.IO;
using System.Text;
using System.Text.Json;

namespace SiNet.App.Wpf.Tests.Certification;

/// <summary>Outcome of a single certification step. There is deliberately no "Skipped".</summary>
internal enum CertificationResult
{
    /// <summary>Declared but not yet reached. The initial state of every required step.</summary>
    NotRun,

    /// <summary>Proven, including any external read-back the step required.</summary>
    Pass,

    /// <summary>The system did not do what the graph says it should. A real defect.</summary>
    Fail,

    /// <summary>
    /// Cannot be proven end-to-end because the product or a policy does not allow it. An honest gap —
    /// never a pass, and never disguised as one.
    /// </summary>
    Blocked,

    /// <summary>Does not apply to this configuration, with a written reason.</summary>
    NotApplicable,
}

/// <summary>Whether a step's outcome can be allowed to stand without proof.</summary>
internal enum CertificationRequirement
{
    Required,
    Optional,
}

/// <summary>
/// Evidence log for the certification tier, with the gate the L4W smoke writer lacks.
/// <para>
/// In <c>PilotSmokeEvidence</c> a <c>Fail</c> row only appends text, so a run could finish green with red
/// rows in the report (audit §2.5). Here every step is declared up front with a
/// <see cref="CertificationRequirement"/>, and <see cref="FinalizeCertification"/> refuses to let the run
/// pass unless every required step reached <see cref="CertificationResult.Pass"/>.
/// </para>
/// <para>
/// <see cref="CertificationResult.Blocked"/> does not throw, because a product gap identified in advance
/// is information rather than a regression — but it does force the overall verdict to
/// <c>NOT CERTIFIED</c>, so it can never be read as success.
/// </para>
/// </summary>
internal sealed class SystemCertificationEvidence
{
    private readonly Dictionary<string, Step> _steps = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];
    private readonly Dictionary<string, string> _facts = [];
    private readonly List<CreatedEntity> _created = [];
    private readonly List<SettingSnapshot> _settings = [];
    private readonly List<string> _manualCleanup = [];
    private readonly string _markdownPath;
    private readonly string _jsonPath;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;

    private SystemCertificationEvidence(string markdownPath, string jsonPath)
    {
        _markdownPath = markdownPath;
        _jsonPath = jsonPath;
    }

    public string MarkdownPath => _markdownPath;

    /// <summary>
    /// Creates an evidence log. <paramref name="directoryOverride"/> lets the gate's own unit tests write
    /// into a temp folder instead of the operator's evidence directory.
    /// </summary>
    public static SystemCertificationEvidence Create(string? directoryOverride = null)
    {
        var directory = directoryOverride
            ?? Environment.GetEnvironmentVariable("SINET_SYSTEM_CERT_EVIDENCE_DIR");

        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SiNet",
                "system-certification");
        }

        Directory.CreateDirectory(directory);

        // The timestamp alone collided in the L4W tier when two runs started in the same second, so a
        // short unique suffix is part of the name rather than a hoped-for property of the clock.
        var stamp = DateTimeOffset.Now.ToString(
            "yyyyMMdd-HHmmss",
            System.Globalization.CultureInfo.InvariantCulture);
        var name = $"system-certification-{stamp}-{Guid.NewGuid().ToString("N")[..6]}";

        return new SystemCertificationEvidence(
            Path.Combine(directory, $"{name}.md"),
            Path.Combine(directory, $"{name}.json"));
    }

    /// <summary>
    /// Declares a step before it runs, so an unreached step is visible as <see cref="CertificationResult.NotRun"/>
    /// rather than silently absent. Declaring the plan up front is what makes "did not run" detectable.
    /// </summary>
    public void Declare(string step, CertificationRequirement requirement, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(step);

        if (!_steps.ContainsKey(step))
        {
            _order.Add(step);
        }

        _steps[step] = new Step(step, requirement, CertificationResult.NotRun, description, DateTimeOffset.Now);
        Flush();
    }

    public void DeclareAll(
        CertificationRequirement requirement,
        params (string Step, string Description)[] steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        foreach (var (step, description) in steps)
        {
            Declare(step, requirement, description);
        }
    }

    public void Pass(string step, string detail) => Record(step, CertificationResult.Pass, detail);

    public void Fail(string step, string detail) => Record(step, CertificationResult.Fail, detail);

    /// <summary>Records a product or policy gap. Never counts as proof.</summary>
    public void Blocked(string step, string detail) => Record(step, CertificationResult.Blocked, detail);

    public void NotApplicable(string step, string reason) =>
        Record(step, CertificationResult.NotApplicable, reason);

    public void Fact(string name, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _facts[name] = value ?? "<null>";
        Flush();
    }

    /// <summary>Records an id the run created, so cleanup and forensics have an exact trail.</summary>
    public void Created(string entityKind, string id, string detail = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        _created.Add(new CreatedEntity(entityKind, id, detail, DateTimeOffset.Now));
        Flush();
    }

    /// <summary>
    /// Snapshots a system setting before it is changed. <paramref name="existedBefore"/> matters as much as
    /// the value: restoring an absent row by writing an empty string is not a restore.
    /// </summary>
    public void SettingChanged(string key, bool existedBefore, string? previousValue, string? newValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _settings.Add(new SettingSnapshot(key, existedBefore, previousValue, newValue, null, false));
        Flush();
    }

    /// <summary>Records the verified post-restore read of a setting changed earlier in the run.</summary>
    public void SettingRestoreVerified(string key, string? valueAfterRestore, bool matchesOriginal)
    {
        var index = _settings.FindLastIndex(s => string.Equals(s.Key, key, StringComparison.Ordinal));
        if (index < 0)
        {
            throw new InvalidOperationException(
                $"Setting '{key}' was never snapshotted, so its restore cannot be verified.");
        }

        _settings[index] = _settings[index] with
        {
            ValueAfterRestore = valueAfterRestore,
            RestoreVerified = matchesOriginal,
        };
        Flush();
    }

    public void RequiresManualCleanup(string what)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(what);
        _manualCleanup.Add(what);
        Flush();
    }

    /// <summary>
    /// Fails the run unless every required step passed. Throws on required Fail or NotRun; a required
    /// Blocked is reported and blocks the verdict without throwing, since it was analysed in advance.
    /// </summary>
    public void FinalizeCertification()
    {
        Flush();

        var failed = Required(CertificationResult.Fail);
        var notRun = Required(CertificationResult.NotRun);

        if (failed.Count == 0 && notRun.Count == 0)
        {
            return;
        }

        var message = new StringBuilder("Certification did not pass. ");
        if (failed.Count > 0)
        {
            message.Append($"Required steps FAILED: {string.Join("; ", failed)}. ");
        }

        if (notRun.Count > 0)
        {
            message.Append($"Required steps NOT RUN: {string.Join("; ", notRun)}. ");
        }

        message.Append($"Evidence: {_markdownPath}");
        throw new SystemCertificationFailedException(message.ToString());
    }

    /// <summary>Overall verdict. Certified only when nothing required is outstanding, blocked or failed.</summary>
    public string Verdict =>
        Required(CertificationResult.Fail).Count == 0
        && Required(CertificationResult.NotRun).Count == 0
        && Required(CertificationResult.Blocked).Count == 0
            ? "CERTIFIED"
            : "NOT CERTIFIED";

    private List<string> Required(CertificationResult result) =>
        _order
            .Select(name => _steps[name])
            .Where(s => s.Requirement == CertificationRequirement.Required && s.Result == result)
            .Select(s => s.Name)
            .ToList();

    private void Record(string step, CertificationResult result, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(step);

        if (!_steps.TryGetValue(step, out var existing))
        {
            // An undeclared step is a harness bug: the plan is what makes NotRun meaningful, so silently
            // accepting late additions would defeat the gate.
            throw new InvalidOperationException(
                $"Step '{step}' was recorded without being declared first. Declare every step up front so "
                + "that steps which never run are visible as NotRun.");
        }

        _steps[step] = existing with { Result = result, Detail = detail, At = DateTimeOffset.Now };
        Flush();
    }

    private void Flush()
    {
        File.WriteAllText(_markdownPath, BuildMarkdown(), Encoding.UTF8);
        File.WriteAllText(
            _jsonPath,
            JsonSerializer.Serialize(
                new
                {
                    StartedAt = _startedAt,
                    Verdict,
                    Facts = _facts,
                    Steps = _order.Select(n => _steps[n]),
                    Created = _created,
                    Settings = _settings,
                    ManualCleanup = _manualCleanup,
                },
                new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);
    }

    private string BuildMarkdown()
    {
        var steps = _order.Select(n => _steps[n]).ToList();
        var sb = new StringBuilder();

        sb.AppendLine("# Full System Workflow Certification — evidence");
        sb.AppendLine();
        sb.AppendLine($"> Started: {_startedAt:yyyy-MM-dd HH:mm:ss zzz}  ");
        sb.AppendLine($"> Verdict: **{Verdict}**");
        sb.AppendLine();

        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Result | Required | Optional |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var result in Enum.GetValues<CertificationResult>())
        {
            var required = steps.Count(s =>
                s.Requirement == CertificationRequirement.Required && s.Result == result);
            var optional = steps.Count(s =>
                s.Requirement == CertificationRequirement.Optional && s.Result == result);
            if (required > 0 || optional > 0)
            {
                sb.AppendLine($"| {Label(result)} | {required} | {optional} |");
            }
        }

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
        sb.AppendLine("| Time | Step | Req | Result | Detail |");
        sb.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var step in steps)
        {
            var req = step.Requirement == CertificationRequirement.Required ? "R" : "O";
            sb.AppendLine(
                $"| {step.At:HH:mm:ss} | {Escape(step.Name)} | {req} | **{Label(step.Result)}** | "
                + $"{Escape(step.Detail)} |");
        }

        if (_created.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Entities created");
            sb.AppendLine();
            sb.AppendLine("| Kind | Id | Detail |");
            sb.AppendLine("| --- | --- | --- |");
            foreach (var entity in _created)
            {
                sb.AppendLine($"| {Escape(entity.Kind)} | {Escape(entity.Id)} | {Escape(entity.Detail)} |");
            }
        }

        if (_settings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## System settings touched");
            sb.AppendLine();
            sb.AppendLine("| Key | Existed before | Original | Written | After restore | Restore verified |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- |");
            foreach (var setting in _settings)
            {
                sb.AppendLine(
                    $"| {Escape(setting.Key)} | {setting.ExistedBefore} | "
                    + $"{Escape(setting.PreviousValue ?? "<absent>")} | "
                    + $"{Escape(setting.NewValue ?? "<absent>")} | "
                    + $"{Escape(setting.ValueAfterRestore ?? "<not read>")} | "
                    + $"{(setting.RestoreVerified ? "yes" : "**NO**")} |");
            }
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
            foreach (var item in _manualCleanup)
            {
                sb.AppendLine($"- {item}");
            }
        }

        return sb.ToString();
    }

    private static string Label(CertificationResult result) => result switch
    {
        CertificationResult.Pass => "PASS",
        CertificationResult.Fail => "FAIL",
        CertificationResult.Blocked => "BLOCKED",
        CertificationResult.NotApplicable => "N/A",
        _ => "NOT RUN",
    };

    private static string Escape(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal)
             .Replace("\r", " ", StringComparison.Ordinal)
             .Replace("\n", " ", StringComparison.Ordinal);

    private sealed record Step(
        string Name,
        CertificationRequirement Requirement,
        CertificationResult Result,
        string Detail,
        DateTimeOffset At);

    private sealed record CreatedEntity(string Kind, string Id, string Detail, DateTimeOffset At);

    private sealed record SettingSnapshot(
        string Key,
        bool ExistedBefore,
        string? PreviousValue,
        string? NewValue,
        string? ValueAfterRestore,
        bool RestoreVerified);
}

/// <summary>
/// Thrown by <see cref="SystemCertificationEvidence.FinalizeCertification"/> when required proof is missing.
/// </summary>
internal sealed class SystemCertificationFailedException(string message) : Exception(message);
