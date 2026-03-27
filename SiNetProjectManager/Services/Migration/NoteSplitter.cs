using System.Globalization;
using System.Text.RegularExpressions;

namespace SiNetProjectManager.Services.Migration;

/// <summary>
/// Result of splitting a single cell's text into one or more note segments.
/// </summary>
public sealed class SplitSegment
{
    /// <summary>1-based index within the split result.</summary>
    public int Index { get; init; }

    /// <summary>Cleaned text content of this segment.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Computed NoteSubIndex (e.g. "1.1.1", "1.1.2").</summary>
    public string? DetectedSubIndex { get; init; }
}

/// <summary>
/// Splits merged/combined note text from historical reports into individual note segments.
/// <para>
/// Historical reports often group multiple comments into a single large cell.
/// This class recognizes three numbering patterns:
/// <list type="bullet">
///   <item><b>Hebrew letters</b>: <c>א.</c>, <c>ב.</c>, <c>ג.</c> (with dot or closing paren)</item>
///   <item><b>Numeric prefixes</b>: <c>1.</c>, <c>2.</c>, or <c>1.1.1</c> style</item>
///   <item><b>Bullet/dash</b>: <c>-</c>, <c>•</c>, <c>●</c> line prefixes</item>
/// </list>
/// </para>
/// </summary>
public static partial class NoteSplitter
{
    /// <summary>
    /// Splits the given note text into individual segments.
    /// Returns a single-element list if no splitting pattern is detected.
    /// </summary>
    /// <param name="text">Raw text from the note cell (already BiDi-stripped).</param>
    /// <param name="sectionCode">Section code (e.g. "1.1") used to build NoteSubIndex values.</param>
    public static List<SplitSegment> Split(ReadOnlySpan<char> text, string sectionCode)
    {
        var textStr = text.Trim().ToString();
        if (string.IsNullOrWhiteSpace(textStr))
            return [new SplitSegment { Index = 1, Text = "", DetectedSubIndex = $"{sectionCode}.1" }];

        // ── Try Hebrew letter splitting first (א. ב. ג.) ──
        var hebrewMatches = HebrewLetterPattern().Matches(textStr);
        if (hebrewMatches.Count >= 2)
            return SplitByMatches(textStr, hebrewMatches, sectionCode);

        // ── Try numeric splitting (1. 2. or 1.1.1 style) ──
        var numericMatches = NumericPattern().Matches(textStr);
        if (numericMatches.Count >= 2)
            return SplitByMatches(textStr, numericMatches, sectionCode);

        // ── Try bullet/dash splitting ──
        var bulletMatches = BulletPattern().Matches(textStr);
        if (bulletMatches.Count >= 2)
            return SplitByMatches(textStr, bulletMatches, sectionCode);

        // ── No splitting pattern detected — return as single segment ──
        return [new SplitSegment { Index = 1, Text = textStr, DetectedSubIndex = $"{sectionCode}.1" }];
    }

    /// <summary>
    /// Extracts a closure/resolution date from note text.
    /// Recognizes patterns like <c>(25.12.2024)</c>, <c>בוצע 01/01/25</c>, <c>תוקן 25.12.2024</c>.
    /// </summary>
    public static DateTime? ExtractClosureDate(ReadOnlySpan<char> text)
    {
        if (text.IsWhiteSpace()) return null;
        var textStr = text.ToString();

        Regex[] patterns =
        [
            ParenDatePattern(),     // (25.12.2024) or (25/12/2024)
            ExecutedDatePattern(),  // בוצע 01/01/25
            FixedDatePattern(),     // תוקן 01/01/2025
            TrailingDatePattern(),  // bare date at end of text
        ];

        ReadOnlySpan<string> formats =
        [
            "d/M/yyyy", "d/M/yy", "dd/MM/yyyy", "dd/MM/yy",
            "d.M.yyyy", "d.M.yy", "dd.MM.yyyy", "dd.MM.yy"
        ];

        foreach (var pattern in patterns)
        {
            var match = pattern.Match(textStr);
            if (!match.Success) continue;

            var dateStr = match.Groups[1].Value;

            foreach (var fmt in formats)
            {
                if (DateTime.TryParseExact(dateStr, fmt,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var result))
                {
                    return result;
                }
            }
        }

        return null;
    }

    #region Private Helpers

    private static List<SplitSegment> SplitByMatches(
        string text, MatchCollection matches, string sectionCode)
    {
        var segments = new List<SplitSegment>();

        // Capture any text before the first match
        if (matches[0].Index > 0)
        {
            var preText = text[..matches[0].Index].Trim();
            if (!string.IsNullOrWhiteSpace(preText))
            {
                segments.Add(new SplitSegment
                {
                    Index = segments.Count + 1,
                    Text = preText,
                    DetectedSubIndex = $"{sectionCode}.{segments.Count + 1}"
                });
            }
        }

        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            int startOfContent = match.Index + match.Length;
            int endOfContent = i + 1 < matches.Count
                ? matches[i + 1].Index
                : text.Length;

            var segmentText = text[startOfContent..endOfContent].Trim();
            if (string.IsNullOrWhiteSpace(segmentText)) continue;

            segments.Add(new SplitSegment
            {
                Index = segments.Count + 1,
                Text = segmentText,
                DetectedSubIndex = $"{sectionCode}.{segments.Count + 1}"
            });
        }

        if (segments.Count == 0)
        {
            return [new SplitSegment { Index = 1, Text = text.Trim(), DetectedSubIndex = $"{sectionCode}.1" }];
        }

        return segments;
    }

    #endregion

    #region Compiled Regex Patterns

    // Hebrew letter followed by dot or closing paren and optional space.
    // Matches: "א. ", "ב) ", "ג. "
    [GeneratedRegex(@"(?:^|\n)\s*([א-ת])\s*[.)]\s*", RegexOptions.Multiline)]
    private static partial Regex HebrewLetterPattern();

    // Numeric prefix: "1. ", "2. ", "1.1. ", "1.1.1 "
    [GeneratedRegex(@"(?:^|\n)\s*(\d+(?:\.\d+)*)\.\s+", RegexOptions.Multiline)]
    private static partial Regex NumericPattern();

    // Bullet/dash/circle prefix
    [GeneratedRegex(@"(?:^|\n)\s*[-•●◦]\s+", RegexOptions.Multiline)]
    private static partial Regex BulletPattern();

    // Date in parentheses: (25.12.2024) or (25/12/2024)
    [GeneratedRegex(@"\((\d{1,2}[./]\d{1,2}[./]\d{2,4})\)")]
    private static partial Regex ParenDatePattern();

    // "בוצע" (executed) prefix + optional "ב" + date
    [GeneratedRegex(@"בוצע\s+(?:ב[\s\u00A0]*)?(\d{1,2}[./]\d{1,2}[./]\d{2,4})")]
    private static partial Regex ExecutedDatePattern();

    // "תוקן" (fixed) prefix + optional "ב" + date
    [GeneratedRegex(@"תוקן\s+(?:ב[\s\u00A0]*)?(\d{1,2}[./]\d{1,2}[./]\d{2,4})")]
    private static partial Regex FixedDatePattern();

    // Bare date at end of text
    [GeneratedRegex(@"(\d{1,2}[./]\d{1,2}[./]\d{2,4})\s*$")]
    private static partial Regex TrailingDatePattern();

    #endregion

    /// <summary>
    /// Detects execution/resolution status from note text.
    /// Returns "PartiallyResolved" for "בוצע חלקית", "Resolved" for "בוצע"/"תוקן", or <c>null</c>.
    /// </summary>
    public static string? DetectExecutionStatus(ReadOnlySpan<char> text)
    {
        if (text.IsWhiteSpace()) return null;
        var t = text.ToString();

        if (t.Contains("בוצע חלקית", StringComparison.OrdinalIgnoreCase))
            return "PartiallyResolved";

        if (t.Contains("בוצע", StringComparison.OrdinalIgnoreCase)
            || t.Contains("תוקן", StringComparison.OrdinalIgnoreCase))
            return "Resolved";

        return null;
    }
}
