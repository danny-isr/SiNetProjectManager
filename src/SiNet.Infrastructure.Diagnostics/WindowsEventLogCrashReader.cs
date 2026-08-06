using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Xml.Linq;
using SiNet.Application.Diagnostics;

namespace SiNet.Infrastructure.Diagnostics;

/// <summary>
/// Reads crash-related records from the local <c>Application</c> and <c>System</c> event logs
/// (DEV-010 + DEV-014 WHEA payload retention).
/// </summary>
public sealed class WindowsEventLogCrashReader : IWorkstationEventLogReader
{
    private const string ApplicationLog = "Application";
    private const string SystemLog = "System";
    private const int MessageCap = 1500;

    private static readonly int[] ApplicationEventIds = [1000, 1001, 1002, 1026];
    private static readonly int[] SystemEventIds = [41, 55, 6008, 1001, 17, 18, 19, 7, 11, 153];

    private static readonly (string ProviderFragment, int[] EventIds)[] SystemAllowList =
    [
        ("Kernel-Power", [41]),
        ("EventLog", [6008]),
        ("BugCheck", [1001]),
        ("WHEA-Logger", [17, 18, 19]),
        ("Ntfs", [55]),
        ("volmgr", [7, 11, 153, 161, 162]),
        ("disk", [7, 11, 153]),
    ];

    public Task<IReadOnlyList<WorkstationCrashEventDto>> ReadAsync(
        WorkstationCrashQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Task.Run<IReadOnlyList<WorkstationCrashEventDto>>(
            () =>
            {
                var results = new List<WorkstationCrashEventDto>();

                if (query.Scope != CrashReportScope.MachineOnly)
                    results.AddRange(Read(ApplicationLog, ApplicationEventIds, query, cancellationToken));

                if (query.Scope != CrashReportScope.ApplicationOnly)
                    results.AddRange(Read(SystemLog, SystemEventIds, query, cancellationToken));

                return results.OrderByDescending(e => e.TimeCreated).ToList();
            },
            cancellationToken);
    }

    private static IEnumerable<WorkstationCrashEventDto> Read(
        string logName,
        IReadOnlyList<int> eventIds,
        WorkstationCrashQuery query,
        CancellationToken cancellationToken)
    {
        var results = new List<WorkstationCrashEventDto>();
        var cap = Math.Max(1, query.MaxEvents);
        var xpath = BuildXPath(eventIds, query.Since);
        var logQuery = new EventLogQuery(logName, PathType.LogName, xpath) { ReverseDirection = true };

        using var reader = new EventLogReader(logQuery);

        while (results.Count < cap)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var record = reader.ReadEvent();
            if (record is null)
                break;

            var dto = Map(record, logName);
            if (dto is null)
                continue;

            if (logName == SystemLog && !IsAllowedSystemEvent(dto.ProviderName, dto.EventId))
                continue;

            results.Add(dto);
        }

        return results;
    }

    private static string BuildXPath(IReadOnlyList<int> eventIds, DateTimeOffset since)
    {
        var ids = string.Join(
            " or ",
            eventIds.Select(id => string.Create(CultureInfo.InvariantCulture, $"EventID={id}")));

        var sinceUtc = since.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
        return $"*[System[({ids}) and TimeCreated[@SystemTime>='{sinceUtc}']]]";
    }

    private static bool IsAllowedSystemEvent(string providerName, int eventId)
        => SystemAllowList.Any(entry =>
            providerName.Contains(entry.ProviderFragment, StringComparison.OrdinalIgnoreCase)
            && entry.EventIds.Contains(eventId));

    private static WorkstationCrashEventDto? Map(EventRecord record, string logName)
    {
        if (record.TimeCreated is not { } timeCreated)
            return null;

        var xml = TryGet(record.ToXml);
        var data = ReadEventData(xml);
        var provider = record.ProviderName ?? string.Empty;
        var message = Truncate(TryGet(record.FormatDescription));
        WheaDetailsDto? whea = null;
        if (provider.Contains("WHEA-Logger", StringComparison.OrdinalIgnoreCase)
            && record.Id is 17 or 18 or 19)
        {
            whea = WheaEventParser.TryParse(xml, record.Id, message);
        }

        return new WorkstationCrashEventDto
        {
            TimeCreated = new DateTimeOffset(timeCreated),
            LogName = logName,
            EventId = record.Id,
            ProviderName = provider,
            LevelDisplayName = TryGet(() => record.LevelDisplayName),
            AppName = data.Get("AppName", 0),
            AppVersion = data.Get("AppVersion", 1),
            ModuleName = data.Get("ModuleName", 3),
            ModuleVersion = data.Get("ModuleVersion", 4),
            ExceptionCode = data.Get("ExceptionCode", 6),
            FaultOffset = data.Get("FaultingOffset", 7) ?? data.Get("FaultOffset", 7),
            AppPath = data.Get("AppPath", 10),
            ModulePath = data.Get("ModulePath", 11),
            ReportId = data.Get("IntegratorReportId", 12) ?? data.Get("ReportId", 12),
            Message = message,
            Whea = whea,
        };
    }

    private static EventDataBag ReadEventData(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return EventDataBag.Empty;

        try
        {
            var document = XDocument.Parse(xml);
            var ns = document.Root?.Name.Namespace ?? XNamespace.None;
            var dataElements = document.Descendants(ns + "Data").ToList();

            var named = dataElements
                .Where(e => !string.IsNullOrWhiteSpace((string?)e.Attribute("Name")))
                .ToDictionary(
                    e => (string)e.Attribute("Name")!,
                    e => e.Value,
                    StringComparer.OrdinalIgnoreCase);

            var positional = dataElements.Select(e => e.Value).ToList();
            return new EventDataBag(named, positional);
        }
        catch (System.Xml.XmlException)
        {
            return EventDataBag.Empty;
        }
    }

    private static string? Truncate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= MessageCap ? trimmed : trimmed[..MessageCap] + "…";
    }

    private static string? TryGet(Func<string?> accessor)
    {
        try
        {
            return accessor();
        }
        catch (EventLogException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed class EventDataBag(IReadOnlyDictionary<string, string> named, IReadOnlyList<string> positional)
    {
        public static EventDataBag Empty { get; } =
            new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), []);

        public string? Get(string name, int positionalIndex)
        {
            if (named.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();

            if (named.Count > 0 || positionalIndex >= positional.Count)
                return null;

            var fallback = positional[positionalIndex];
            return string.IsNullOrWhiteSpace(fallback) ? null : fallback.Trim();
        }
    }
}
