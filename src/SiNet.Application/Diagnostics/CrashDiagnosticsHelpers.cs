using System.Globalization;
using System.Xml.Linq;

namespace SiNet.Application.Diagnostics;

/// <summary>Pure helpers for WHEA XML payloads (DEV-014 slice B).</summary>
public static class WheaEventParser
{
    public const int RawXmlCapChars = 4000;
    public const int UncorrectedXmlAppendixCap = 5;

    public static WheaDetailsDto? TryParse(string? eventXml, int eventId)
    {
        if (string.IsNullOrWhiteSpace(eventXml))
            return null;

        try
        {
            var document = XDocument.Parse(eventXml);
            var ns = document.Root?.Name.Namespace ?? XNamespace.None;
            var named = document.Descendants(ns + "Data")
                .Where(e => !string.IsNullOrWhiteSpace((string?)e.Attribute("Name")))
                .ToDictionary(
                    e => (string)e.Attribute("Name")!,
                    e => e.Value?.Trim() ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase);

            var isCorrected = eventId == 17;
            string? raw = null;
            if (!isCorrected && eventId is 18 or 19)
            {
                raw = eventXml.Length <= RawXmlCapChars
                    ? eventXml
                    : eventXml[..RawXmlCapChars] + "…";
            }

            return new WheaDetailsDto(
                Get(named, "ErrorSource"),
                Get(named, "ApicId") ?? Get(named, "APICID"),
                Get(named, "MCABank") ?? Get(named, "McaBank"),
                Get(named, "Address"),
                Get(named, "MciStat") ?? Get(named, "MCI_STATUS"),
                Get(named, "ProcessorId") ?? Get(named, "ProcId"),
                isCorrected,
                raw);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    public static bool HasRepeatBank(IEnumerable<WorkstationCrashEventDto> events)
    {
        return events
            .Where(e => e.Whea is { McaBank: not null } or { ApicId: not null })
            .GroupBy(
                e => $"{e.Whea!.McaBank ?? "?"}|{e.Whea.ApicId ?? "?"}",
                StringComparer.OrdinalIgnoreCase)
            .Any(g => g.Count() >= 2);
    }

    private static string? Get(IReadOnlyDictionary<string, string> named, string key)
        => named.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}

/// <summary>Pure helpers for DIMM inventory facts (DEV-014 slice A).</summary>
public static class MemoryModuleFacts
{
    public const int MaxModulesInReport = 8;

    public static bool HasMixedDimms(IReadOnlyList<MemoryModuleDto> modules)
    {
        if (modules.Count < 2)
            return false;

        var parts = modules
            .Select(m => $"{m.PartNumber ?? "?"}|{m.RatedSpeedMhz?.ToString(CultureInfo.InvariantCulture) ?? "?"}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        return parts > 1;
    }

    public static string FormatMicrocode(byte[]? updateRevision)
    {
        if (updateRevision is null || updateRevision.Length == 0)
            return string.Empty;

        // REG_BINARY is little-endian on Windows; present as lowercase hex without separators.
        return Convert.ToHexString(updateRevision).ToLowerInvariant();
    }
}
