using System.Globalization;

namespace SiNet.Application.Diagnostics;

/// <summary>
/// Pure transformation from raw event rows to the finished report: scope filtering, severity,
/// correlation and aggregation. Holds no I/O so it is fully unit-testable without an Event Log.
/// </summary>
public static class WorkstationCrashReportBuilder
{
    /// <summary>An app crash this close to a critical machine event is treated as related.</summary>
    public static readonly TimeSpan CorrelationWindow = TimeSpan.FromMinutes(5);

    private const int TopBucketCount = 10;

    public static WorkstationCrashReport Build(
        WorkstationCrashQuery query,
        CrashReportContextDto context,
        MachineProfileDto machine,
        IReadOnlyList<WorkstationCrashEventDto> events,
        DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(events);

        var classified = events
            .Select(e => e with { Severity = ClassifySeverity(e.ProviderName, e.EventId) })
            .Where(e => MatchesScope(e, query.Scope))
            .OrderByDescending(e => e.TimeCreated)
            .Take(Math.Max(1, query.MaxEvents))
            .ToList();

        var correlated = ApplyCorrelation(classified);
        var summary = Summarize(correlated, query, generatedAt);

        return new WorkstationCrashReport(generatedAt, context, machine, query, correlated, summary);
    }

    /// <summary>Severity from provider + event id. Event id 1001 means different things per provider.</summary>
    public static CrashSeverity ClassifySeverity(string providerName, int eventId)
    {
        var provider = providerName ?? string.Empty;

        if (eventId == 1001 && Has(provider, "BugCheck"))
        {
            return CrashSeverity.Critical;
        }

        if (eventId == 41 && Has(provider, "Kernel-Power"))
        {
            return CrashSeverity.Critical;
        }

        if (eventId == 6008 && Has(provider, "EventLog"))
        {
            return CrashSeverity.Critical;
        }

        if (IsHardwareEvent(provider, eventId))
        {
            return CrashSeverity.Critical;
        }

        if (eventId == 1000 && Has(provider, "Application Error"))
        {
            return CrashSeverity.AppCrash;
        }

        if (eventId == 1002 && Has(provider, "Application Hang"))
        {
            return CrashSeverity.AppCrash;
        }

        if (eventId == 1026 && Has(provider, ".NET Runtime"))
        {
            return CrashSeverity.AppCrash;
        }

        return CrashSeverity.Supporting;
    }

    /// <summary>WHEA / disk / NTFS / volume-manager faults — hardware reporting a real error.</summary>
    public static bool IsHardwareEvent(string providerName, int eventId)
    {
        var provider = providerName ?? string.Empty;

        if (Has(provider, "WHEA-Logger") && eventId is 17 or 18 or 19)
        {
            return true;
        }

        if (Has(provider, "Ntfs") && eventId == 55)
        {
            return true;
        }

        if (Has(provider, "volmgr"))
        {
            return true;
        }

        return IsDiskProvider(provider) && eventId is 7 or 11 or 153;
    }

    private static bool IsDiskProvider(string provider)
        => string.Equals(provider, "disk", StringComparison.OrdinalIgnoreCase)
           || Has(provider, "Disk");

    private static bool MatchesScope(WorkstationCrashEventDto crashEvent, CrashReportScope scope) => scope switch
    {
        CrashReportScope.ApplicationOnly =>
            string.Equals(crashEvent.LogName, "Application", StringComparison.OrdinalIgnoreCase),
        CrashReportScope.MachineOnly =>
            string.Equals(crashEvent.LogName, "System", StringComparison.OrdinalIgnoreCase),
        _ => true,
    };

    private static IReadOnlyList<WorkstationCrashEventDto> ApplyCorrelation(
        IReadOnlyList<WorkstationCrashEventDto> events)
    {
        var criticals = events.Where(e => e.Severity == CrashSeverity.Critical).ToList();
        if (criticals.Count == 0)
        {
            return events;
        }

        return events
            .Select(e =>
            {
                if (e.Severity != CrashSeverity.AppCrash)
                {
                    return e;
                }

                var match = criticals
                    .Where(c => Distance(c.TimeCreated, e.TimeCreated) <= CorrelationWindow)
                    .OrderBy(c => Distance(c.TimeCreated, e.TimeCreated))
                    .FirstOrDefault();

                return match is null ? e : e with { CorrelatedWith = DescribeCorrelation(match) };
            })
            .ToList();
    }

    private static TimeSpan Distance(DateTimeOffset left, DateTimeOffset right)
    {
        var delta = left - right;
        return delta < TimeSpan.Zero ? -delta : delta;
    }

    private static string DescribeCorrelation(WorkstationCrashEventDto criticalEvent)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{criticalEvent.ProviderName} {criticalEvent.EventId} @ {criticalEvent.TimeCreated:yyyy-MM-dd HH:mm:ss}");

    private static CrashReportSummaryDto Summarize(
        IReadOnlyList<WorkstationCrashEventDto> events,
        WorkstationCrashQuery query,
        DateTimeOffset generatedAt)
    {
        var appCrashes = events.Where(e => e.Severity == CrashSeverity.AppCrash).ToList();
        var criticals = events.Where(e => e.Severity == CrashSeverity.Critical).ToList();
        var incidents = appCrashes.Count + criticals.Count;
        var days = query.LookbackDays(generatedAt);

        return new CrashReportSummaryDto(
            events.Count,
            appCrashes.Count,
            criticals.Count,
            events.Count(e => !string.IsNullOrEmpty(e.CorrelatedWith)),
            events.Any(e => e.EventId == 1001 && Has(e.ProviderName, "BugCheck")),
            events.Any(e => IsHardwareEvent(e.ProviderName, e.EventId)),
            events.Any(e => (e.EventId == 41 && Has(e.ProviderName, "Kernel-Power"))
                            || (e.EventId == 6008 && Has(e.ProviderName, "EventLog"))),
            Math.Round(incidents / (double)days, 2),
            Bucket(
                events.Where(e => e.Severity != CrashSeverity.Supporting),
                e => e.TimeCreated.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                orderByKey: true),
            Bucket(
                events.Where(e => e.Severity != CrashSeverity.Supporting),
                e => e.TimeCreated.ToString("HH", CultureInfo.InvariantCulture),
                orderByKey: true),
            Bucket(
                appCrashes.Where(e => !string.IsNullOrWhiteSpace(e.ModuleName)),
                e => e.ModuleName!,
                orderByKey: false),
            Bucket(
                appCrashes.Where(e => !string.IsNullOrWhiteSpace(e.ExceptionCode)),
                e => e.ExceptionCode!,
                orderByKey: false));
    }

    private static IReadOnlyList<CrashCountDto> Bucket(
        IEnumerable<WorkstationCrashEventDto> source,
        Func<WorkstationCrashEventDto, string> keySelector,
        bool orderByKey)
    {
        var grouped = source
            .GroupBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CrashCountDto(g.Key, g.Count()));

        return orderByKey
            ? grouped.OrderBy(b => b.Key, StringComparer.Ordinal).ToList()
            : grouped
                .OrderByDescending(b => b.Count)
                .ThenBy(b => b.Key, StringComparer.OrdinalIgnoreCase)
                .Take(TopBucketCount)
                .ToList();
    }

    private static bool Has(string value, string token)
        => value.Contains(token, StringComparison.OrdinalIgnoreCase);
}
