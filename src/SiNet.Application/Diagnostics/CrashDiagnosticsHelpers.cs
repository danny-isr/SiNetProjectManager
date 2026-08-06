using System.Globalization;
using System.Xml.Linq;

namespace SiNet.Application.Diagnostics;

/// <summary>Pure helpers for WHEA XML payloads (DEV-014 / DEV-015).</summary>
public static class WheaEventParser
{
    public const int RawXmlCapChars = 4000;
    public const int UncorrectedXmlAppendixCap = 5;

    public static WheaDetailsDto? TryParse(string? eventXml, int eventId, string? message = null)
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

            var isCorrected = ClassifyCorrected(eventId, named, message);
            string? raw = null;
            if (!isCorrected)
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

    /// <summary>
    /// Event id alone is insufficient (Event 19 is often a corrected MCE). Prefer explicit
    /// severity / wording from the payload and message (DEV-015).
    /// </summary>
    public static bool ClassifyCorrected(
        int eventId,
        IReadOnlyDictionary<string, string> named,
        string? message)
    {
        if (eventId == 17)
            return true;

        var severity = Get(named, "ErrorSeverity")
                       ?? Get(named, "Severity")
                       ?? Get(named, "ErrorType");
        var blob = string.Join(' ', named.Values);
        if (!string.IsNullOrWhiteSpace(message))
            blob = blob + ' ' + message;
        if (!string.IsNullOrWhiteSpace(severity))
            blob = blob + ' ' + severity;

        if (ContainsWord(blob, "uncorrected") || ContainsWord(blob, "fatal"))
            return false;

        if (ContainsWord(blob, "corrected"))
            return true;

        // Fallbacks when the payload is silent.
        return eventId switch
        {
            18 => false,
            19 => false, // treat as uncorrected only when wording is absent — prefer not to hide
            _ => eventId == 17,
        };
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

    private static bool ContainsWord(string blob, string token)
        => blob.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static string? Get(IReadOnlyDictionary<string, string> named, string key)
        => named.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}

/// <summary>Pure helpers for DIMM / microcode facts (DEV-014 / DEV-015).</summary>
public static class MemoryModuleFacts
{
    public const int MaxModulesInReport = 8;
    public const int MaxMinidumpNamesInReport = 20;

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

    /// <summary>
    /// <c>Update Revision</c> is little-endian. Bytes <c>20 01 00 00</c> become <c>0x120</c>, not
    /// the raw hex dump <c>20010000</c> (DEV-015).
    /// </summary>
    public static string FormatMicrocode(byte[]? updateRevision)
    {
        if (updateRevision is null || updateRevision.Length == 0)
            return string.Empty;

        if (updateRevision.Length >= 4)
        {
            var value = BitConverter.ToUInt32(updateRevision, 0);
            return string.Create(CultureInfo.InvariantCulture, $"0x{value:x}");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"0x{Convert.ToHexString(updateRevision).ToLowerInvariant()}");
    }
}
