using System.Globalization;
using System.Text;

namespace SiNet.Application.Diagnostics;

/// <summary>
/// Renders a <see cref="WorkstationCrashReport"/> to CSV + Markdown (DEV-010 + DEV-014 Ship 1).
/// </summary>
public static class WorkstationCrashReportFormatter
{
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";
    private const int MarkdownEventLimit = 200;

    private static readonly string[] CsvHeader =
    [
        "Time", "Log", "EventId", "Provider", "Severity", "IncidentId", "AppName", "AppVersion",
        "ModuleName", "ModuleVersion", "ExceptionCode", "FaultOffset", "AppPath",
        "ModulePath", "ReportId", "CorrelatedWith", "WheaBank", "WheaApicId", "WheaCorrected", "Message",
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
                Escape(e.IncidentId),
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
                Escape(e.Whea?.McaBank),
                Escape(e.Whea?.ApicId),
                Escape(e.Whea is null ? null : (e.Whea.IsCorrected ? "yes" : "no")),
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
        AppendIncidents(builder, report.Incidents);
        AppendBuckets(builder, summary);
        AppendEvents(builder, report.Events, report.Query.AppNameFilters);
        AppendWheaAppendix(builder, report.Events);
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
        AppendRow(builder, "System", FormatPair(machine.SystemManufacturer, machine.SystemModel));
        AppendRow(builder, "Baseboard", FormatBoard(machine));
        AppendRow(builder, "BIOS", FormatBios(machine));
        AppendRow(builder, "CPU", Invariant($"{machine.CpuName} — {machine.LogicalProcessorCount} logical cores"));
        if (!string.IsNullOrWhiteSpace(machine.CpuMicrocode))
            AppendRow(builder, "CPU microcode", machine.CpuMicrocode!);
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
            AppendRow(builder, "Autodesk products", string.Join("; ", machine.InstalledAutodeskProducts));

        builder.AppendLine();

        var modules = machine.MemoryModules ?? [];
        if (modules.Count > 0)
        {
            builder.AppendLine("### Memory modules");
            builder.AppendLine();
            builder.AppendLine("| Bank | Part | Size GB | Rated MHz | Configured MHz |");
            builder.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var m in modules.Take(MemoryModuleFacts.MaxModulesInReport))
            {
                builder.AppendLine(Invariant(
                    $"| {EscapeCell(m.BankLabel ?? m.DeviceLocator)} | {EscapeCell(FormatPair(m.Manufacturer, m.PartNumber))} | {m.CapacityGb:F1} | {m.RatedSpeedMhz?.ToString(CultureInfo.InvariantCulture) ?? ""} | {m.ConfiguredSpeedMhz?.ToString(CultureInfo.InvariantCulture) ?? ""} |"));
            }

            builder.AppendLine();
            builder.AppendLine(Invariant(
                $"- Mixed DIMM signal: {(machine.HasMixedDimms ? "yes (different part/speed across banks)" : "no")}"));
            builder.AppendLine();
        }

        builder.AppendLine("### Manual BIOS checks (not readable from Windows)");
        builder.AppendLine();
        builder.AppendLine("- Intel Default / Baseline Profile");
        builder.AppendLine("- MultiCore Enhancement (MCE)");
        builder.AppendLine("- Manual overclock / undervolt offsets");
        builder.AppendLine();

        if (machine.CollectionWarnings.Count > 0)
        {
            builder.AppendLine("Profile collection warnings:");
            builder.AppendLine();
            foreach (var warning in machine.CollectionWarnings)
                builder.AppendLine(Invariant($"- {warning}"));

            builder.AppendLine();
        }
    }

    private static string FormatBoard(MachineProfileDto machine)
    {
        var parts = new[]
        {
            machine.BaseBoardManufacturer,
            machine.BaseBoardProduct,
            machine.BaseBoardVersion,
            string.IsNullOrWhiteSpace(machine.BaseBoardSerial) ? null : $"S/N {machine.BaseBoardSerial}",
        }.Where(p => !string.IsNullOrWhiteSpace(p));
        var joined = string.Join(" · ", parts!);
        return string.IsNullOrWhiteSpace(joined) ? "unknown" : joined;
    }

    private static string FormatBios(MachineProfileDto machine)
    {
        var date = machine.BiosReleaseDate is { } d
            ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
        var parts = new[] { machine.BiosManufacturer, machine.BiosVersion, date }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        var joined = string.Join(" · ", parts!);
        return string.IsNullOrWhiteSpace(joined) ? "unknown" : joined;
    }

    private static string FormatPair(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right))
            return "unknown";
        if (string.IsNullOrWhiteSpace(left))
            return right!;
        if (string.IsNullOrWhiteSpace(right))
            return left!;
        return $"{left} {right}";
    }

    private static void AppendSummary(StringBuilder builder, CrashReportSummaryDto summary)
    {
        builder.AppendLine("## Facts");
        builder.AppendLine();
        builder.AppendLine("| Fact | Value |");
        builder.AppendLine("| --- | --- |");
        AppendRow(builder, "Records collected", summary.TotalRecords.ToString(CultureInfo.InvariantCulture));
        AppendRow(builder, "Incidents (grouped)", summary.IncidentCount.ToString(CultureInfo.InvariantCulture));
        AppendRow(builder, "Incidents per day", summary.IncidentsPerDay.ToString("F2", CultureInfo.InvariantCulture));
        AppendRow(builder, "Records per day", summary.RecordsPerDay.ToString("F2", CultureInfo.InvariantCulture));
        AppendRow(builder, "Civil / filtered app crashes", summary.CivilApplicationCrashIncidents.ToString(CultureInfo.InvariantCulture));
        AppendRow(builder, "Application hangs", summary.ApplicationHangIncidents.ToString(CultureInfo.InvariantCulture));
        AppendRow(builder, "Other application crashes", summary.OtherApplicationCrashIncidents.ToString(CultureInfo.InvariantCulture));
        AppendRow(builder, "Unexpected shutdowns", summary.UnexpectedShutdownIncidents.ToString(CultureInfo.InvariantCulture));
        AppendRow(builder, "Hardware error incidents", summary.HardwareErrorIncidents.ToString(CultureInfo.InvariantCulture));
        AppendRow(builder, "Application crash records", summary.ApplicationCrashCount.ToString(CultureInfo.InvariantCulture));
        AppendRow(builder, "Critical machine records", summary.CriticalCount.ToString(CultureInfo.InvariantCulture));
        AppendRow(builder, "Correlated app crashes", summary.CorrelatedCount.ToString(CultureInfo.InvariantCulture));
        AppendRow(builder, "Bugcheck present", summary.HasBugCheck ? "yes" : "no");
        AppendRow(builder, "Hardware events present", summary.HasHardwareEvents ? "yes" : "no");
        AppendRow(builder, "Unexpected shutdown present", summary.HasUnexpectedShutdown ? "yes" : "no");
        AppendRow(builder, "Repeat WHEA bank/APIC", summary.HasRepeatWheaBank ? "yes" : "no");
        builder.AppendLine();
    }

    private static void AppendIncidents(StringBuilder builder, IReadOnlyList<CrashIncidentDto> incidents)
    {
        builder.AppendLine("## Incidents");
        builder.AppendLine();

        if (incidents.Count == 0)
        {
            builder.AppendLine("No grouped incidents in the selected window.");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("| Id | Kind | Start | End | App | Records |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var incident in incidents.Take(MarkdownEventLimit))
        {
            builder.AppendLine(Invariant(
                $"| {EscapeCell(incident.IncidentId)} | {incident.Kind} | {incident.StartedAt.ToString(TimeFormat, CultureInfo.InvariantCulture)} | {incident.EndedAt.ToString(TimeFormat, CultureInfo.InvariantCulture)} | {EscapeCell(incident.AppName)} | {incident.RecordCount} |"));
        }

        builder.AppendLine();
    }

    private static void AppendBuckets(StringBuilder builder, CrashReportSummaryDto summary)
    {
        AppendBucketTable(builder, "Incidents per day", "Date", summary.IncidentsByDay);
        AppendBucketTable(builder, "Incidents per hour of day", "Hour", summary.IncidentsByHour);
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
            return;

        builder.AppendLine(Invariant($"## {title}"));
        builder.AppendLine();
        builder.AppendLine(Invariant($"| {keyHeader} | Count |"));
        builder.AppendLine("| --- | --- |");
        foreach (var bucket in buckets)
            builder.AppendLine(Invariant($"| {EscapeCell(bucket.Key)} | {bucket.Count} |"));

        builder.AppendLine();
    }

    private static void AppendEvents(
        StringBuilder builder,
        IReadOnlyList<WorkstationCrashEventDto> events,
        IReadOnlyList<string> appFilters)
    {
        builder.AppendLine("## Records");
        builder.AppendLine();

        // Other-app crashes stay in CSV only (PROD decision); Markdown focuses on filtered apps + machine.
        var display = events
            .Where(e =>
                !string.Equals(e.LogName, "Application", StringComparison.OrdinalIgnoreCase)
                || WorkstationCrashReportBuilder.MatchesAppFilter(e, appFilters)
                || e.Severity != CrashSeverity.AppCrash)
            .ToList();

        if (display.Count == 0)
        {
            builder.AppendLine("No matching events in the selected window.");
            builder.AppendLine();
            return;
        }

        if (display.Count > MarkdownEventLimit)
        {
            builder.AppendLine(Invariant(
                $"Showing the {MarkdownEventLimit} most recent of {display.Count} records; the CSV holds all of them."));
            builder.AppendLine();
        }

        builder.AppendLine("| Time | Log | Id | Provider | Severity | Incident | App | Module | Exception | Correlated with |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");

        foreach (var e in display.Take(MarkdownEventLimit))
        {
            var cells = new[]
            {
                e.TimeCreated.ToString(TimeFormat, CultureInfo.InvariantCulture),
                EscapeCell(e.LogName),
                e.EventId.ToString(CultureInfo.InvariantCulture),
                EscapeCell(e.ProviderName),
                e.Severity.ToString(),
                EscapeCell(e.IncidentId),
                EscapeCell(e.AppName),
                EscapeCell(e.ModuleName),
                EscapeCell(e.ExceptionCode),
                EscapeCell(e.CorrelatedWith),
            };

            builder.AppendLine(Invariant($"| {string.Join(" | ", cells)} |"));
        }

        builder.AppendLine();

        var withMessages = display
            .Where(e => e.Severity != CrashSeverity.Supporting && !string.IsNullOrWhiteSpace(e.Message))
            .Take(MarkdownEventLimit)
            .ToList();

        if (withMessages.Count == 0)
            return;

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

    private static void AppendWheaAppendix(StringBuilder builder, IReadOnlyList<WorkstationCrashEventDto> events)
    {
        var uncorrected = events
            .Where(e => e.Whea is { IsCorrected: false, RawXml: not null })
            .OrderByDescending(e => e.TimeCreated)
            .Take(WheaEventParser.UncorrectedXmlAppendixCap)
            .ToList();

        if (uncorrected.Count == 0)
            return;

        builder.AppendLine("## WHEA raw XML appendix (uncorrected only)");
        builder.AppendLine();

        foreach (var e in uncorrected)
        {
            builder.AppendLine(Invariant(
                $"### {e.TimeCreated.ToString(TimeFormat, CultureInfo.InvariantCulture)} · Event {e.EventId} · bank {e.Whea!.McaBank ?? "?"} · APIC {e.Whea.ApicId ?? "?"}"));
            builder.AppendLine();
            builder.AppendLine("```xml");
            builder.AppendLine(e.Whea.RawXml!.Trim());
            builder.AppendLine("```");
            builder.AppendLine();
        }
    }

    private static void AppendAnalysisRequest(StringBuilder builder)
    {
        builder.AppendLine("## What to determine");
        builder.AppendLine();
        builder.AppendLine("1. Is the dominant pattern an application fault or a machine/hardware fault?");
        builder.AppendLine("2. Which faulting module, exception code, or WHEA bank repeats?");
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
            return string.Empty;

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
