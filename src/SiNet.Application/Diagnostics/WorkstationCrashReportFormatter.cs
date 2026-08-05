using System.Globalization;
using System.Text;

namespace SiNet.Application.Diagnostics;

/// <summary>
/// Renders a <see cref="WorkstationCrashReport"/> to the two shipped formats: a tabular CSV for
/// humans and spreadsheets, and a self-contained Markdown file meant to be handed to an AI.
/// The user-written context appears in the Markdown only — the CSV stays purely tabular.
/// </summary>
public static class WorkstationCrashReportFormatter
{
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";
    private const int MarkdownEventLimit = 200;

    private static readonly string[] CsvHeader =
    [
        "Time", "Log", "EventId", "Provider", "Severity", "AppName", "AppVersion",
        "ModuleName", "ModuleVersion", "ExceptionCode", "FaultOffset", "AppPath",
        "ModulePath", "ReportId", "CorrelatedWith", "Message",
    ];

    public static string ToCsv(WorkstationCrashReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', CsvHeader));

        foreach (var e in report.Events)
        {
            builder.AppendLine(string.Join(',',
            [
                Escape(e.TimeCreated.ToString(TimeFormat, CultureInfo.InvariantCulture)),
                Escape(e.LogName),
                Escape(e.EventId.ToString(CultureInfo.InvariantCulture)),
                Escape(e.ProviderName),
                Escape(e.Severity.ToString()),
                Escape(e.AppName),
                Escape(e.AppVersion),
                Escape(e.ModuleName),
                Escape(e.ModuleVersion),
                Escape(e.ExceptionCode),
                Escape(e.FaultOffset),
                Escape(e.AppPath),
                Escape(e.ModulePath),
                Escape(e.ReportId),
                Escape(e.CorrelatedWith),
                Escape(e.Message),
            ]));
        }

        return builder.ToString();
    }

    public static string ToMarkdown(WorkstationCrashReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var machine = report.Machine;
        var summary = report.Summary;
        var builder = new StringBuilder();

        builder.AppendLine(Invariant($"# Workstation crash report — {machine.MachineName}"));
        builder.AppendLine();
        builder.AppendLine(Invariant($"Generated: {report.GeneratedAt.ToString(TimeFormat, CultureInfo.InvariantCulture)}"));
        builder.AppendLine(Invariant(
            $"Window: last {report.Query.LookbackDays(report.GeneratedAt)} day(s), since {report.Query.Since.ToString(TimeFormat, CultureInfo.InvariantCulture)}"));
        builder.AppendLine(Invariant($"Scope: {report.Query.Scope}"));
        builder.AppendLine(Invariant($"App filters: {string.Join(", ", report.Query.AppNameFilters)}"));
        builder.AppendLine();

        AppendUserContext(builder, report.Context);
        AppendMachineProfile(builder, machine);
        AppendSummary(builder, summary);
        AppendBuckets(builder, summary);
        AppendEvents(builder, report.Events);
        AppendAnalysisRequest(builder);

        return builder.ToString();
    }

    private static void AppendUserContext(StringBuilder builder, CrashReportContextDto context)
    {
        builder.AppendLine("## Why this report was produced (written by the user)");
        builder.AppendLine();
        builder.AppendLine(Invariant($"- Category: {context.Category} ({CrashReasonCategoryDisplay.ToHebrew(context.Category)})"));

        if (context.LastOccurrence is { } occurrence)
        {
            builder.AppendLine(Invariant(
                $"- Last occurrence reported by the user: {occurrence.ToString(TimeFormat, CultureInfo.InvariantCulture)}"));
        }

        builder.AppendLine();
        builder.AppendLine("> " + context.Description.Replace("\r\n", "\n").Replace("\n", "\n> "));
        builder.AppendLine();
    }

    private static void AppendMachineProfile(StringBuilder builder, MachineProfileDto machine)
    {
        builder.AppendLine("## Machine profile");
        builder.AppendLine();
        builder.AppendLine("| Property | Value |");
        builder.AppendLine("| --- | --- |");
        AppendRow(builder, "Machine", machine.MachineName);
        AppendRow(builder, "User", machine.UserName);
        AppendRow(builder, "OS", Invariant($"{machine.OsCaption} ({machine.OsVersion})"));
        AppendRow(builder, "CPU", Invariant($"{machine.CpuName} — {machine.LogicalProcessorCount} logical cores"));
        AppendRow(builder, "RAM", Invariant($"{machine.TotalMemoryGb:F1} GB"));
        AppendRow(builder, "System drive", Invariant(
            $"{machine.SystemDriveFreeGb:F1} GB free of {machine.SystemDriveTotalGb:F1} GB"));
        AppendRow(builder, "Uptime", Invariant($"{machine.Uptime.TotalHours:F1} h"));
        AppendRow(
            builder,
            "Last Windows update",
            machine.LastWindowsUpdate is { } lastUpdate
                ? lastUpdate.ToString(TimeFormat, CultureInfo.InvariantCulture)
                : "unknown");

        foreach (var gpu in machine.GraphicsAdapters)
        {
            var driverDate = gpu.DriverDate is { } date
                ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : "unknown";
            AppendRow(builder, "GPU", Invariant($"{gpu.Name} — driver {gpu.DriverVersion ?? "unknown"} ({driverDate})"));
        }

        if (machine.InstalledAutodeskProducts.Count > 0)
        {
            AppendRow(builder, "Autodesk products", string.Join("; ", machine.InstalledAutodeskProducts));
        }

        builder.AppendLine();

        if (machine.CollectionWarnings.Count > 0)
        {
            builder.AppendLine("Profile collection warnings:");
            builder.AppendLine();
            foreach (var warning in machine.CollectionWarnings)
            {
                builder.AppendLine(Invariant($"- {warning}"));
            }

            builder.AppendLine();
        }
    }

    private static void AppendSummary(StringBuilder builder, CrashReportSummaryDto summary)
    {
        builder.AppendLine("## Facts");
        builder.AppendLine();
        builder.AppendLine("| Fact | Value |");
        builder.AppendLine("| --- | --- |");
        AppendRow(builder, "Events collected", summary.TotalEvents.ToString(CultureInfo.InvariantCulture));
        AppendRow(builder, "Application crashes", summary.ApplicationCrashCount.ToString(CultureInfo.InvariantCulture));
        AppendRow(builder, "Critical machine events", summary.CriticalCount.ToString(CultureInfo.InvariantCulture));
        AppendRow(builder, "Correlated app crashes", summary.CorrelatedCount.ToString(CultureInfo.InvariantCulture));
        AppendRow(builder, "Incidents per day", summary.CrashesPerDay.ToString("F2", CultureInfo.InvariantCulture));
        AppendRow(builder, "Bugcheck present", summary.HasBugCheck ? "yes" : "no");
        AppendRow(builder, "Hardware events present", summary.HasHardwareEvents ? "yes" : "no");
        AppendRow(builder, "Unexpected shutdown present", summary.HasUnexpectedShutdown ? "yes" : "no");
        builder.AppendLine();
    }

    private static void AppendBuckets(StringBuilder builder, CrashReportSummaryDto summary)
    {
        AppendBucketTable(builder, "Incidents per day", "Date", summary.CrashesByDay);
        AppendBucketTable(builder, "Incidents per hour of day", "Hour", summary.CrashesByHour);
        AppendBucketTable(builder, "Top faulting modules", "Module", summary.TopModules);
        AppendBucketTable(builder, "Top exception codes", "Code", summary.TopExceptionCodes);
    }

    private static void AppendBucketTable(
        StringBuilder builder,
        string title,
        string keyHeader,
        IReadOnlyList<CrashCountDto> buckets)
    {
        if (buckets.Count == 0)
        {
            return;
        }

        builder.AppendLine(Invariant($"## {title}"));
        builder.AppendLine();
        builder.AppendLine(Invariant($"| {keyHeader} | Count |"));
        builder.AppendLine("| --- | --- |");
        foreach (var bucket in buckets)
        {
            builder.AppendLine(Invariant($"| {EscapeCell(bucket.Key)} | {bucket.Count} |"));
        }

        builder.AppendLine();
    }

    private static void AppendEvents(StringBuilder builder, IReadOnlyList<WorkstationCrashEventDto> events)
    {
        builder.AppendLine("## Events");
        builder.AppendLine();

        if (events.Count == 0)
        {
            builder.AppendLine("No matching events in the selected window.");
            builder.AppendLine();
            return;
        }

        if (events.Count > MarkdownEventLimit)
        {
            builder.AppendLine(Invariant(
                $"Showing the {MarkdownEventLimit} most recent of {events.Count} events; the CSV holds all of them."));
            builder.AppendLine();
        }

        builder.AppendLine("| Time | Log | Id | Provider | Severity | App | Module | Exception | Correlated with |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");

        foreach (var e in events.Take(MarkdownEventLimit))
        {
            var cells = new[]
            {
                e.TimeCreated.ToString(TimeFormat, CultureInfo.InvariantCulture),
                EscapeCell(e.LogName),
                e.EventId.ToString(CultureInfo.InvariantCulture),
                EscapeCell(e.ProviderName),
                e.Severity.ToString(),
                EscapeCell(e.AppName),
                EscapeCell(e.ModuleName),
                EscapeCell(e.ExceptionCode),
                EscapeCell(e.CorrelatedWith),
            };

            builder.AppendLine(Invariant($"| {string.Join(" | ", cells)} |"));
        }

        builder.AppendLine();

        var withMessages = events
            .Where(e => e.Severity != CrashSeverity.Supporting && !string.IsNullOrWhiteSpace(e.Message))
            .Take(MarkdownEventLimit)
            .ToList();

        if (withMessages.Count == 0)
        {
            return;
        }

        builder.AppendLine("### Event messages");
        builder.AppendLine();

        foreach (var e in withMessages)
        {
            var stamp = e.TimeCreated.ToString(TimeFormat, CultureInfo.InvariantCulture);
            builder.AppendLine(Invariant($"**{stamp} · {e.ProviderName} {e.EventId} · {e.Severity}**"));
            builder.AppendLine();
            builder.AppendLine("```text");
            builder.AppendLine(e.Message!.Trim());
            builder.AppendLine("```");
            builder.AppendLine();
        }
    }

    private static void AppendAnalysisRequest(StringBuilder builder)
    {
        builder.AppendLine("## What to determine");
        builder.AppendLine();
        builder.AppendLine("1. Is the dominant pattern an application fault or a machine/hardware fault?");
        builder.AppendLine("2. Which faulting module or exception code repeats, and what usually causes it?");
        builder.AppendLine("3. Do the timestamps cluster (time of day, specific dates, near a driver or update change)?");
        builder.AppendLine("4. What concrete next step would confirm or rule out the leading hypothesis?");
        builder.AppendLine();
        builder.AppendLine("Answer only from the data above; state explicitly when the data is insufficient.");
    }

    private static void AppendRow(StringBuilder builder, string label, string value)
        => builder.AppendLine(Invariant($"| {label} | {EscapeCell(value)} |"));

    private static string EscapeCell(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ").Trim();

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var needsQuotes = value.Contains(',', StringComparison.Ordinal)
                          || value.Contains('"', StringComparison.Ordinal)
                          || value.Contains('\n', StringComparison.Ordinal)
                          || value.Contains('\r', StringComparison.Ordinal);

        return needsQuotes
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
    }

    private static string Invariant(FormattableString value)
        => value.ToString(CultureInfo.InvariantCulture);
}
