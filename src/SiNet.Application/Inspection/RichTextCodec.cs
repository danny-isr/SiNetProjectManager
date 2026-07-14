using System.Text;
using System.Text.RegularExpressions;

namespace SiNet.Application.Inspection;

/// <summary>
/// Encodes / decodes the internal WYSIWYG markup used for inspection notes.
/// Codes: <c>1</c>=Red, <c>2</c>=Blue, <c>3</c>=Gray, <c>4</c>=Green, <c>!</c>=Bold.
/// Any 1–2 character combination is valid (e.g. <c>{1! text}</c>, <c>{!2 text}</c>, <c>{! text}</c>).
/// Plain text outside braces is rendered unstyled.
/// </summary>
public static class RichTextCodec
{
    #region Types

    /// <summary>Known rich-text status values for inspection notes.</summary>
    public static class NoteStatuses
    {
        public const string OK = "OK";
        public const string Issue = "Issue";
        public const string Recurring = "Recurring";
    }

    /// <summary>A styled run within a decoded rich-text string.</summary>
    public sealed class RichTextRun
    {
        public required int StartIndex { get; init; }
        public required int Length { get; init; }
        public bool Bold { get; init; }
        public RichTextColor Color { get; init; }
    }

    /// <summary>Supported rich-text colors.</summary>
    public enum RichTextColor
    {
        Default,
        Red,
        Blue,
        Green,
        Gray
    }

    #endregion

    /// <summary>
    /// Decodes a formatting code string (e.g. <c>"1!"</c>, <c>"!2"</c>, <c>"3"</c>, <c>"!"</c>)
    /// into a bold flag and a <see cref="RichTextColor"/>.
    /// Supports any combination/order of a colour digit (<c>1-4</c>) and bold modifier (<c>!</c>).
    /// </summary>
    public static (bool Bold, RichTextColor Color) ParseCode(ReadOnlySpan<char> code)
    {
        bool bold = false;
        var color = RichTextColor.Default;

        foreach (var c in code)
        {
            switch (c)
            {
                case '!': bold = true; break;
                case '1': color = RichTextColor.Red; break;
                case '2': color = RichTextColor.Blue; break;
                case '3': color = RichTextColor.Gray; break;
                case '4': color = RichTextColor.Green; break;
            }
        }

        return (bold, color);
    }

    // Matches {code text} where code is 1–2 chars from [1234!] — captures code + inner text
    private static readonly Regex TagRegex = new(
        @"\{([1234!]{1,2})\s+([^}]+)\}",
        RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// Parses the internal markup into plain text and a list of styled runs.
    /// </summary>
    /// <param name="encoded">
    /// Raw markup string, e.g. <c>"Normal {1! critical issue} and {2 blue note}"</c>.
    /// </param>
    /// <returns>Tuple of the stripped plain text and the style runs.</returns>
    public static (string PlainText, List<RichTextRun> Runs) Parse(string? encoded)
    {
        if (string.IsNullOrEmpty(encoded))
            return (string.Empty, []);

        var runs = new List<RichTextRun>();
        var plain = new StringBuilder(encoded.Length);
        int lastIndex = 0;

        foreach (Match match in TagRegex.Matches(encoded))
        {
            // Append plain text before this tag
            if (match.Index > lastIndex)
            {
                plain.Append(encoded, lastIndex, match.Index - lastIndex);
            }

            var code = match.Groups[1].Value;
            var innerText = match.Groups[2].Value;

            int startIndex = plain.Length;
            plain.Append(innerText);

            var (bold, color) = ParseCode(code);

            runs.Add(new RichTextRun
            {
                StartIndex = startIndex,
                Length = innerText.Length,
                Bold = bold,
                Color = color
            });

            lastIndex = match.Index + match.Length;
        }

        // Append trailing plain text
        if (lastIndex < encoded.Length)
        {
            plain.Append(encoded, lastIndex, encoded.Length - lastIndex);
        }

        return (plain.ToString(), runs);
    }

    /// <summary>
    /// Appends a resolved-date annotation to the encoded markup (idempotent — skips if already present).
    /// Pattern: <c>{2 [טופל בתאריך: yyyy-MM-dd]}</c>
    /// </summary>
    public static string AppendResolvedDate(string encoded, DateTime resolvedDate)
    {
        const string marker = "טופל בתאריך:";
        if (encoded.Contains(marker, StringComparison.Ordinal))
            return encoded; // already annotated

        return encoded + $" {{2 [{marker} {resolvedDate:yyyy-MM-dd}]}}";
    }

    /// <summary>
    /// Computes the aggregate section status from its child note statuses.
    /// Priority: Recurring &gt; Issue &gt; OK. Returns <c>null</c> if no notes have a status.
    /// </summary>
    public static string? RollUpSectionStatus(IEnumerable<string?> noteStatuses)
    {
        bool hasIssue = false;
        bool hasAny = false;

        foreach (var status in noteStatuses)
        {
            if (string.IsNullOrWhiteSpace(status))
                continue;

            hasAny = true;

            if (status.Equals(NoteStatuses.Recurring, StringComparison.OrdinalIgnoreCase))
                return NoteStatuses.Recurring;

            if (status.Equals(NoteStatuses.Issue, StringComparison.OrdinalIgnoreCase))
                hasIssue = true;
        }

        if (!hasAny)
            return null;

        return hasIssue ? NoteStatuses.Issue : NoteStatuses.OK;
    }

    #region Encoding (runs → markup)

    /// <summary>
    /// Encodes plain text and styled runs back into the internal markup format.
    /// Runs must reference valid ranges within <paramref name="plainText"/>.
    /// </summary>
    public static string Encode(string plainText, List<RichTextRun> runs)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;
        if (runs.Count == 0)
            return plainText;

        var sorted = runs.OrderBy(r => r.StartIndex).ToList();
        var sb = new StringBuilder(plainText.Length + runs.Count * 6);
        int cursor = 0;

        foreach (var run in sorted)
        {
            if (run.StartIndex > cursor)
                sb.Append(plainText.AsSpan(cursor, run.StartIndex - cursor));

            var code = GetTagCode(run.Color, run.Bold);
            var inner = plainText.AsSpan(run.StartIndex, Math.Min(run.Length, plainText.Length - run.StartIndex));

            if (code != null)
                sb.Append('{').Append(code).Append(' ').Append(inner).Append('}');
            else
                sb.Append(inner);

            cursor = run.StartIndex + run.Length;
        }

        if (cursor < plainText.Length)
            sb.Append(plainText.AsSpan(cursor));

        return sb.ToString();
    }

    /// <summary>Returns the internal tag code for the given style, or <c>null</c> for unstyled text.</summary>
    private static string? GetTagCode(RichTextColor color, bool bold)
    {
        char? colorChar = color switch
        {
            RichTextColor.Red => '1',
            RichTextColor.Blue => '2',
            RichTextColor.Gray => '3',
            RichTextColor.Green => '4',
            _ => null
        };

        return (colorChar, bold) switch
        {
            (char c, true) => $"{c}!",
            (char c, false) => c.ToString(),
            (null, true) => "!",
            _ => null
        };
    }

    #endregion

    #region Conflict-aware color application

    /// <summary>
    /// Applies a new color to the range
    /// [<paramref name="newStart"/>, <paramref name="newStart"/> + <paramref name="newLength"/>)
    /// within existing styled runs, resolving all range conflicts:
    /// <list type="bullet">
    ///   <item><b>Full coverage</b> — old run fully inside new range → removed.</item>
    ///   <item><b>Internal split</b> — new range inside old → old splits into two.</item>
    ///   <item><b>Partial overlap start</b> — old starts before new → trim old end.</item>
    ///   <item><b>Partial overlap end</b> — old ends after new → trim old start.</item>
    /// </list>
    /// Pass <see cref="RichTextColor.Default"/> with <paramref name="newBold"/>=<c>false</c> to strip formatting.
    /// </summary>
    public static List<RichTextRun> ApplyColor(
        List<RichTextRun> existingRuns,
        int newStart, int newLength,
        RichTextColor newColor, bool newBold)
    {
        int newEnd = newStart + newLength;
        var result = new List<RichTextRun>(existingRuns.Count + 2);

        foreach (var run in existingRuns)
        {
            int oldStart = run.StartIndex;
            int oldEnd = oldStart + run.Length;

            // No overlap — keep as-is
            if (oldEnd <= newStart || oldStart >= newEnd)
            {
                result.Add(run);
                continue;
            }

            // Full coverage: new completely covers old → drop
            if (newStart <= oldStart && newEnd >= oldEnd)
                continue;

            // Internal split: new is strictly inside old → two remnants
            if (oldStart < newStart && oldEnd > newEnd)
            {
                result.Add(new RichTextRun
                {
                    StartIndex = oldStart,
                    Length = newStart - oldStart,
                    Bold = run.Bold,
                    Color = run.Color
                });
                result.Add(new RichTextRun
                {
                    StartIndex = newEnd,
                    Length = oldEnd - newEnd,
                    Bold = run.Bold,
                    Color = run.Color
                });
                continue;
            }

            // Partial overlap — old starts before new → trim old end
            if (oldStart < newStart)
            {
                result.Add(new RichTextRun
                {
                    StartIndex = oldStart,
                    Length = newStart - oldStart,
                    Bold = run.Bold,
                    Color = run.Color
                });
                continue;
            }

            // Partial overlap — old ends after new → trim old start
            if (oldEnd > newEnd)
            {
                result.Add(new RichTextRun
                {
                    StartIndex = newEnd,
                    Length = oldEnd - newEnd,
                    Bold = run.Bold,
                    Color = run.Color
                });
            }
        }

        // Add the new run (unless just stripping formatting)
        if (newColor != RichTextColor.Default || newBold)
        {
            result.Add(new RichTextRun
            {
                StartIndex = newStart,
                Length = newLength,
                Bold = newBold,
                Color = newColor
            });
        }

        result.Sort((a, b) => a.StartIndex.CompareTo(b.StartIndex));
        return result;
    }

    #endregion

    #region Position mapping (encoded ↔ plain text)

    /// <summary>
    /// Parses the internal markup and builds a position map from encoded string indices
    /// to plain text indices. Map values: plain text index for text characters, <c>-1</c>
    /// for tag delimiter characters (<c>{ code  }</c>).
    /// </summary>
    public static (string PlainText, List<RichTextRun> Runs, int[] EncodedToPlainMap) ParseWithMap(
        string? encoded)
    {
        if (string.IsNullOrEmpty(encoded))
            return (string.Empty, [], []);

        var map = new int[encoded.Length];
        Array.Fill(map, -1);
        var runs = new List<RichTextRun>();
        var plain = new StringBuilder(encoded.Length);
        int lastIndex = 0;

        foreach (Match match in TagRegex.Matches(encoded))
        {
            // Plain text before this tag
            for (int i = lastIndex; i < match.Index; i++)
            {
                map[i] = plain.Length;
                plain.Append(encoded[i]);
            }

            var code = match.Groups[1].Value;
            var innerGroup = match.Groups[2];

            // Inner text maps to plain text positions
            int runStart = plain.Length;
            for (int i = 0; i < innerGroup.Length; i++)
            {
                map[innerGroup.Index + i] = plain.Length;
                plain.Append(innerGroup.Value[i]);
            }

            var (bold, color) = ParseCode(code);

            runs.Add(new RichTextRun
            {
                StartIndex = runStart,
                Length = innerGroup.Length,
                Bold = bold,
                Color = color
            });

            lastIndex = match.Index + match.Length;
        }

        // Trailing plain text
        for (int i = lastIndex; i < encoded.Length; i++)
        {
            map[i] = plain.Length;
            plain.Append(encoded[i]);
        }

        return (plain.ToString(), runs, map);
    }

    /// <summary>
    /// Translates an encoded string position to the nearest valid plain text position.
    /// When the encoded position falls on a tag delimiter, scans forward (or backward)
    /// to find the closest text character.
    /// </summary>
    public static int MapEncodedToPlain(int[] map, int encodedIndex, bool searchForward = true)
    {
        if (map.Length == 0) return 0;

        // Beyond the end → return total plain text length
        if (encodedIndex >= map.Length)
        {
            for (int i = map.Length - 1; i >= 0; i--)
            {
                if (map[i] >= 0) return map[i] + 1;
            }
            return 0;
        }

        encodedIndex = Math.Max(0, encodedIndex);

        if (map[encodedIndex] >= 0)
            return map[encodedIndex];

        if (searchForward)
        {
            for (int i = encodedIndex + 1; i < map.Length; i++)
            {
                if (map[i] >= 0) return map[i];
            }
            // Fell off end
            for (int i = map.Length - 1; i >= 0; i--)
            {
                if (map[i] >= 0) return map[i] + 1;
            }
        }
        else
        {
            for (int i = encodedIndex - 1; i >= 0; i--)
            {
                if (map[i] >= 0) return map[i] + 1;
            }
        }

        return 0;
    }

    #endregion
}
