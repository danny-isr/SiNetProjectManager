using System.Globalization;

namespace SiNet.Application.Diagnostics;

/// <summary>
/// Pure transformation from raw event rows to the finished report: scope filtering, severity,
/// correlation, incident grouping and aggregation (DEV-010 + DEV-014 Ship 1).
/// </summary>
public static class WorkstationCrashReportBuilder
{
    /// <summary>An app crash this close to a critical machine event is treated as related.</summary>
    public static readonly TimeSpan CorrelationWindow = TimeSpan.FromMinutes(5);

    /// <summary>WER / sibling records for the same application crash or hang.</summary>
    public static readonly TimeSpan ApplicationSiblingWindow = TimeSpan.FromSeconds(60);

    /// <summary>Kernel-Power / EventLog / BugCheck / hardware cluster window.</summary>
    public static readonly TimeSpan MachineClusterWindow = TimeSpan.FromMinutes(5);

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
        var (eventsWithIncidents, incidents) = GroupIncidents(correlated, query.AppNameFilters);
        var summary = Summarize(eventsWithIncidents, incidents, query, generatedAt, machine);

        return new WorkstationCrashReport(
            generatedAt, context, machine, query, eventsWithIncidents, summary, incidents);
    }

    /// <summary>Severity from provider + event id. Event id 1001 means different things per provider.</summary>
    public static CrashSeverity ClassifySeverity(string providerName, int eventId)
    {
        var provider = providerName ?? string.Empty;

        if (eventId == 1001 && Has(provider, "BugCheck"))
            return CrashSeverity.Critical;

        if (eventId == 41 && Has(provider, "Kernel-Power"))
            return CrashSeverity.Critical;

        if (eventId == 6008 && Has(provider, "EventLog"))
            return CrashSeverity.Critical;

        if (IsHardwareEvent(provider, eventId))
            return CrashSeverity.Critical;

        if (eventId == 1000 && Has(provider, "Application Error"))
            return CrashSeverity.AppCrash;

        if (eventId == 1002 && Has(provider, "Application Hang"))
            return CrashSeverity.AppCrash;

        if (eventId == 1026 && Has(provider, ".NET Runtime"))
            return CrashSeverity.AppCrash;

        return CrashSeverity.Supporting;
    }

    /// <summary>WHEA / disk / NTFS / volume-manager faults — hardware reporting a real error.</summary>
    public static bool IsHardwareEvent(string providerName, int eventId)
    {
        var provider = providerName ?? string.Empty;

        if (Has(provider, "WHEA-Logger") && eventId is 17 or 18 or 19)
            return true;

        if (Has(provider, "Ntfs") && eventId == 55)
            return true;

        if (Has(provider, "volmgr"))
            return true;

        return IsDiskProvider(provider) && eventId is 7 or 11 or 153;
    }

    public static bool MatchesAppFilter(WorkstationCrashEventDto crashEvent, IReadOnlyList<string> filters)
    {
        if (filters.Count == 0)
            return true;

        var haystack = string.Join(
            ' ',
            new[] { crashEvent.AppName, crashEvent.AppPath, crashEvent.Message }
                .Where(v => !string.IsNullOrWhiteSpace(v)));

        return filters.Any(f =>
            !string.IsNullOrWhiteSpace(f)
            && haystack.Contains(f.Trim(), StringComparison.OrdinalIgnoreCase));
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
            return events;

        return events
            .Select(e =>
            {
                if (e.Severity != CrashSeverity.AppCrash)
                    return e;

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

    private static (IReadOnlyList<WorkstationCrashEventDto> Events, IReadOnlyList<CrashIncidentDto> Incidents)
        GroupIncidents(IReadOnlyList<WorkstationCrashEventDto> events, IReadOnlyList<string> appFilters)
    {
        var working = events.ToList();
        var assigned = new bool[working.Count];
        var incidents = new List<CrashIncidentDto>();
        var nextId = 1;

        string AllocateId() => string.Create(CultureInfo.InvariantCulture, $"I{nextId++:D3}");

        // 1) Same ReportId clusters first.
        foreach (var group in working
                     .Select((e, i) => (e, i))
                     .Where(x => !string.IsNullOrWhiteSpace(x.e.ReportId))
                     .GroupBy(x => x.e.ReportId!, StringComparer.OrdinalIgnoreCase))
        {
            var members = group.Where(x => !assigned[x.i]).ToList();
            if (members.Count == 0)
                continue;

            var kind = InferApplicationKind(members.Select(m => m.e).ToList(), appFilters);
            if (kind is null)
                continue;

            CommitIncident(AllocateId(), kind.Value, members, incidents, assigned, working);
        }

        // 2) Application Error / Hang seeds ±60s.
        for (var i = 0; i < working.Count; i++)
        {
            if (assigned[i])
                continue;

            var seed = working[i];
            CrashIncidentKind? kind = null;
            if (seed.EventId == 1000 && Has(seed.ProviderName, "Application Error"))
            {
                kind = MatchesAppFilter(seed, appFilters)
                    ? CrashIncidentKind.ApplicationCrash
                    : CrashIncidentKind.OtherApplicationCrash;
            }
            else if (seed.EventId == 1002 && Has(seed.ProviderName, "Application Hang"))
            {
                kind = MatchesAppFilter(seed, appFilters)
                    ? CrashIncidentKind.ApplicationHang
                    : CrashIncidentKind.OtherApplicationCrash;
            }

            if (kind is null)
                continue;

            var members = new List<(WorkstationCrashEventDto e, int i)> { (seed, i) };
            for (var j = 0; j < working.Count; j++)
            {
                if (assigned[j] || j == i)
                    continue;

                var candidate = working[j];
                if (Distance(candidate.TimeCreated, seed.TimeCreated) > ApplicationSiblingWindow)
                    continue;

                var sameApp = string.Equals(
                    NormalizeApp(candidate.AppName) ?? NormalizeApp(seed.AppName),
                    NormalizeApp(seed.AppName) ?? NormalizeApp(candidate.AppName),
                    StringComparison.OrdinalIgnoreCase);

                var isWer = candidate.EventId == 1001 && Has(candidate.ProviderName, "Windows Error Reporting");
                var isNet = candidate.EventId == 1026 && Has(candidate.ProviderName, ".NET Runtime");
                if ((isWer || isNet) && (sameApp || string.IsNullOrWhiteSpace(candidate.AppName)))
                    members.Add((candidate, j));
            }

            CommitIncident(AllocateId(), kind.Value, members, incidents, assigned, working);
        }

        // 3) Unexpected shutdown cluster.
        for (var i = 0; i < working.Count; i++)
        {
            if (assigned[i])
                continue;

            var seed = working[i];
            if (!(seed.EventId == 41 && Has(seed.ProviderName, "Kernel-Power"))
                && !(seed.EventId == 6008 && Has(seed.ProviderName, "EventLog"))
                && !(seed.EventId == 1001 && Has(seed.ProviderName, "BugCheck")))
            {
                continue;
            }

            var members = new List<(WorkstationCrashEventDto e, int i)> { (seed, i) };
            for (var j = 0; j < working.Count; j++)
            {
                if (assigned[j] || j == i)
                    continue;

                var candidate = working[j];
                if (Distance(candidate.TimeCreated, seed.TimeCreated) > MachineClusterWindow)
                    continue;

                if ((candidate.EventId == 41 && Has(candidate.ProviderName, "Kernel-Power"))
                    || (candidate.EventId == 6008 && Has(candidate.ProviderName, "EventLog"))
                    || (candidate.EventId == 1001 && Has(candidate.ProviderName, "BugCheck")))
                {
                    members.Add((candidate, j));
                }
            }

            CommitIncident(AllocateId(), CrashIncidentKind.UnexpectedShutdown, members, incidents, assigned, working);
        }

        // 4) Hardware clusters by provider + bank/device.
        for (var i = 0; i < working.Count; i++)
        {
            if (assigned[i])
                continue;

            var seed = working[i];
            if (!IsHardwareEvent(seed.ProviderName, seed.EventId))
                continue;

            var key = HardwareClusterKey(seed);
            var members = new List<(WorkstationCrashEventDto e, int i)> { (seed, i) };
            for (var j = 0; j < working.Count; j++)
            {
                if (assigned[j] || j == i)
                    continue;

                var candidate = working[j];
                if (!IsHardwareEvent(candidate.ProviderName, candidate.EventId))
                    continue;
                if (Distance(candidate.TimeCreated, seed.TimeCreated) > MachineClusterWindow)
                    continue;
                if (!string.Equals(HardwareClusterKey(candidate), key, StringComparison.OrdinalIgnoreCase))
                    continue;

                members.Add((candidate, j));
            }

            CommitIncident(AllocateId(), CrashIncidentKind.HardwareError, members, incidents, assigned, working);
        }

        return (working, incidents.OrderByDescending(i => i.StartedAt).ToList());
    }

    private static CrashIncidentKind? InferApplicationKind(
        IReadOnlyList<WorkstationCrashEventDto> members,
        IReadOnlyList<string> appFilters)
    {
        // DEV-015: WER-only ReportId clusters are Supporting detail, not incidents.
        var primary = members.FirstOrDefault(e => e.EventId is 1000 or 1002);
        if (primary is null)
            return null;

        if (primary.EventId == 1002)
        {
            return MatchesAppFilter(primary, appFilters)
                ? CrashIncidentKind.ApplicationHang
                : CrashIncidentKind.OtherApplicationCrash;
        }

        return MatchesAppFilter(primary, appFilters)
            ? CrashIncidentKind.ApplicationCrash
            : CrashIncidentKind.OtherApplicationCrash;
    }

    private static void CommitIncident(
        string incidentId,
        CrashIncidentKind kind,
        List<(WorkstationCrashEventDto e, int i)> members,
        List<CrashIncidentDto> incidents,
        bool[] assigned,
        List<WorkstationCrashEventDto> working)
    {
        foreach (var (_, index) in members)
            assigned[index] = true;

        foreach (var (e, index) in members)
            working[index] = e with { IncidentId = incidentId };

        var times = members.Select(m => m.e.TimeCreated).OrderBy(t => t).ToList();
        var appName = members
            .Select(m => m.e.AppName)
            .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));

        incidents.Add(new CrashIncidentDto(
            incidentId,
            kind,
            times[0],
            times[^1],
            appName,
            members.Count,
            members.Select(m => m.e.ReportId)
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToList()));
    }

    private static string HardwareClusterKey(WorkstationCrashEventDto e)
    {
        if (e.Whea is { } whea)
            return $"{e.ProviderName}|{whea.McaBank ?? "?"}|{whea.ApicId ?? "?"}";

        return $"{e.ProviderName}|{e.EventId}";
    }

    private static string? NormalizeApp(string? appName)
        => string.IsNullOrWhiteSpace(appName) ? null : Path.GetFileName(appName.Trim());

    private static CrashReportSummaryDto Summarize(
        IReadOnlyList<WorkstationCrashEventDto> events,
        IReadOnlyList<CrashIncidentDto> incidents,
        WorkstationCrashQuery query,
        DateTimeOffset generatedAt,
        MachineProfileDto machine)
    {
        var appCrashes = events.Where(e => e.Severity == CrashSeverity.AppCrash).ToList();
        var criticals = events.Where(e => e.Severity == CrashSeverity.Critical).ToList();
        var days = query.LookbackDays(generatedAt);
        var incidentCount = incidents.Count;
        var hasBugCheckEvent = events.Any(e => e.EventId == 1001 && Has(e.ProviderName, "BugCheck"));
        var hasBugCheck = hasBugCheckEvent || machine.KernelMinidumpCount > 0;

        return new CrashReportSummaryDto(
            events.Count,
            appCrashes.Count,
            criticals.Count,
            events.Count(e => !string.IsNullOrEmpty(e.CorrelatedWith)),
            hasBugCheck,
            events.Any(e => IsHardwareEvent(e.ProviderName, e.EventId)),
            events.Any(e => (e.EventId == 41 && Has(e.ProviderName, "Kernel-Power"))
                            || (e.EventId == 6008 && Has(e.ProviderName, "EventLog"))),
            Math.Round(incidentCount / (double)days, 2),
            Math.Round(events.Count / (double)days, 2),
            incidentCount,
            incidents.Count(i => i.Kind == CrashIncidentKind.ApplicationCrash),
            incidents.Count(i => i.Kind == CrashIncidentKind.ApplicationHang),
            incidents.Count(i => i.Kind == CrashIncidentKind.OtherApplicationCrash),
            incidents.Count(i => i.Kind == CrashIncidentKind.UnexpectedShutdown),
            incidents.Count(i => i.Kind == CrashIncidentKind.HardwareError),
            WheaEventParser.HasRepeatBank(events),
            Bucket(
                incidents,
                i => i.StartedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                orderByKey: true),
            Bucket(
                incidents,
                i => i.StartedAt.ToString("HH", CultureInfo.InvariantCulture),
                orderByKey: true),
            BucketEvents(
                appCrashes.Where(e => !string.IsNullOrWhiteSpace(e.ModuleName)),
                e => e.ModuleName!,
                orderByKey: false),
            BucketEvents(
                appCrashes.Where(e => !string.IsNullOrWhiteSpace(e.ExceptionCode)),
                e => e.ExceptionCode!,
                orderByKey: false));
    }

    private static IReadOnlyList<CrashCountDto> Bucket(
        IEnumerable<CrashIncidentDto> source,
        Func<CrashIncidentDto, string> keySelector,
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

    private static IReadOnlyList<CrashCountDto> BucketEvents(
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
