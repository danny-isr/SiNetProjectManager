using System.Text.RegularExpressions;

namespace SiNetSQL.Services.InspectionSync;

/// <summary>
/// Canonical Google-Sheet <c>&lt;&lt;...&gt;&gt;</c> tag grammar for inspection templates.
/// Shared by New System create/hydrate and V2 export/scan paths — do not reimplement elsewhere.
/// </summary>
public static partial class InspectionTemplateTagGrammar
{
    // <<X.Y Title [Subtitle]>> — header/definition tag (numbered, with brackets)
    private static readonly Regex StatusTagRegex = StatusTagRegexGen();
    // <<X.Y $>> or <<$ X.Y>> — note-input tag
    private static readonly Regex NoteInputTagRegex = NoteInputTagRegexGen();
    // <<X.Y Title>> — legacy note tag (numbered, no brackets, no $)
    private static readonly Regex NoteTagRegex = NoteTagRegexGen();
    // <<text>> — general data tag (non-numbered)
    private static readonly Regex GeneralTagRegex = GeneralTagRegexGen();

    /// <summary>
    /// Scans all cells for status / note-input / legacy-note / general tags (RTL column order).
    /// </summary>
    public static List<TemplateScanTag> ScanAllTemplateTags(IList<IList<object>> rows)
    {
        var tags = new List<TemplateScanTag>();

        int maxCols = 0;
        for (int r = 0; r < rows.Count; r++)
        {
            if (rows[r] != null && rows[r].Count > maxCols)
                maxCols = rows[r].Count;
        }

        var matched = new HashSet<(int Row, int Col, int Start)>();

        for (int colIdx = maxCols - 1; colIdx >= 0; colIdx--)
        {
            for (int rowIdx = 0; rowIdx < rows.Count; rowIdx++)
            {
                var row = rows[rowIdx];
                if (row == null || colIdx >= row.Count) continue;

                var rawText = row[colIdx]?.ToString();
                if (string.IsNullOrEmpty(rawText)) continue;

                var text = StripBidiMarks(rawText);

                foreach (Match m in StatusTagRegex.Matches(text))
                {
                    matched.Add((rowIdx, colIdx, m.Index));
                    tags.Add(new TemplateScanTag
                    {
                        SectionCode = m.Groups[1].Value,
                        Title = m.Groups[2].Value.Trim(),
                        DefaultText = m.Groups[3].Value.Trim(),
                        IsStatusTag = true,
                        Row = rowIdx,
                        Col = colIdx
                    });
                }

                foreach (Match m in NoteInputTagRegex.Matches(text))
                {
                    if (matched.Contains((rowIdx, colIdx, m.Index))) continue;
                    matched.Add((rowIdx, colIdx, m.Index));
                    tags.Add(new TemplateScanTag
                    {
                        SectionCode = m.Groups["code"].Value,
                        IsNoteInputTag = true,
                        Row = rowIdx,
                        Col = colIdx
                    });
                }

                foreach (Match m in NoteTagRegex.Matches(text))
                {
                    if (matched.Contains((rowIdx, colIdx, m.Index))) continue;
                    matched.Add((rowIdx, colIdx, m.Index));
                    tags.Add(new TemplateScanTag
                    {
                        SectionCode = m.Groups[1].Value,
                        Title = m.Groups[2].Value.Trim(),
                        IsStatusTag = false,
                        Row = rowIdx,
                        Col = colIdx
                    });
                }

                foreach (Match m in GeneralTagRegex.Matches(text))
                {
                    if (matched.Contains((rowIdx, colIdx, m.Index))) continue;
                    var label = m.Groups[1].Value.Trim();
                    var isPlannerResponseTag = string.Equals(
                        label,
                        TemplateTagValidator.PlannerResponseTagLabel,
                        StringComparison.Ordinal);
                    tags.Add(new TemplateScanTag
                    {
                        SectionCode = string.Empty,
                        GeneralTagLabel = label,
                        IsGeneralTag = true,
                        IsPlannerResponseColumnTag = isPlannerResponseTag,
                        Row = rowIdx,
                        Col = colIdx
                    });
                }
            }
        }

        return tags;
    }

    /// <summary>
    /// Converts header / fallback numbered tags and general tags into <see cref="TemplateSyncRow"/> values.
    /// </summary>
    public static List<TemplateSyncRow> BuildSyncRowsFromTags(IReadOnlyList<TemplateScanTag> tags)
    {
        var rows = new List<TemplateSyncRow>();
        var seenCodes = new HashSet<string>(StringComparer.Ordinal);
        int rowNumber = 0;

        var headerTags = tags
            .Where(t => t.IsStatusTag && !t.IsGeneralTag)
            .OrderBy(t => t.Col)
            .ThenBy(t => t.Row);

        foreach (var tag in headerTags)
        {
            if (!seenCodes.Add(tag.SectionCode))
                continue;

            var dotIdx = tag.SectionCode.IndexOf('.');
            var chapterPart = dotIdx > 0 ? tag.SectionCode[..dotIdx] : tag.SectionCode;
            if (!int.TryParse(chapterPart, out var chapterNumber))
                continue;

            rowNumber++;
            rows.Add(new TemplateSyncRow
            {
                RowNumber = rowNumber,
                ChapterNumber = chapterNumber,
                ChapterTitle = tag.Title?.Trim(),
                SectionCode = tag.SectionCode,
                SectionTitle = $"<<{tag.SectionCode} {tag.Title} [{tag.DefaultText}]>>"
            });
        }

        var fallbackTags = tags
            .Where(t => !t.IsGeneralTag && !t.IsStatusTag
                        && !string.IsNullOrEmpty(t.SectionCode)
                        && !seenCodes.Contains(t.SectionCode))
            .OrderBy(t => t.Col)
            .ThenBy(t => t.Row);

        foreach (var tag in fallbackTags)
        {
            if (!seenCodes.Add(tag.SectionCode))
                continue;

            var dotIdx = tag.SectionCode.IndexOf('.');
            var chapterPart = dotIdx > 0 ? tag.SectionCode[..dotIdx] : tag.SectionCode;
            if (!int.TryParse(chapterPart, out var chapterNumber))
                continue;

            var title = tag.Title?.Trim() ?? tag.SectionCode;
            rowNumber++;
            rows.Add(new TemplateSyncRow
            {
                RowNumber = rowNumber,
                ChapterNumber = chapterNumber,
                ChapterTitle = title,
                SectionCode = tag.SectionCode,
                SectionTitle = $"<<{tag.SectionCode} {title}>>"
            });
        }

        var generalTags = tags
            .Where(t => t.IsGeneralTag
                        && !t.IsPlannerResponseColumnTag
                        && !string.IsNullOrWhiteSpace(t.GeneralTagLabel))
            .OrderBy(t => t.Col)
            .ThenBy(t => t.Row);

        var seenLabels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in generalTags)
        {
            var label = tag.GeneralTagLabel!;
            if (!seenLabels.Add(label))
                continue;

            rowNumber++;
            rows.Add(new TemplateSyncRow
            {
                RowNumber = rowNumber,
                ChapterNumber = 0,
                ChapterTitle = "נתונים כלליים",
                SectionCode = label,
                SectionTitle = label
            });
        }

        return rows;
    }

    /// <summary>Scan → validate → build sync rows (caller still decides fail-closed policy).</summary>
    public static TemplateScanResult ScanAndBuild(IList<IList<object>> rows)
    {
        if (rows.Count == 0)
        {
            return new TemplateScanResult
            {
                SyncRows = [],
                AllTags = [],
                ValidationErrors = []
            };
        }

        var allTags = ScanAllTemplateTags(rows);
        var validationErrors = TemplateTagValidator.Validate(allTags);
        var syncRows = BuildSyncRowsFromTags(allTags);
        var plannerResponseTag = allTags.FirstOrDefault(t => t.IsPlannerResponseColumnTag);

        return new TemplateScanResult
        {
            SyncRows = syncRows,
            AllTags = allTags,
            ValidationErrors = validationErrors,
            PlannerResponseColumnIndex = plannerResponseTag?.Col ?? -1,
            PlannerResponseRowIndex = plannerResponseTag?.Row ?? -1
        };
    }

    private static string StripBidiMarks(string text) =>
        Regex.Replace(text, @"[\u200B-\u200F\u00AD\u2060\uFEFF\u202A-\u202E\u2066-\u2069]", "");

    [GeneratedRegex(@"<<\s*(\d+(?:\.\d+)+)\s+([^\[]*?)\[(.*?)\]\s*>>")]
    private static partial Regex StatusTagRegexGen();

    [GeneratedRegex(@"<<\s*(?:(?<code>\d+(?:\.\d+)+)\s+\$|\$\s+(?<code>\d+(?:\.\d+)+))\s*>>")]
    private static partial Regex NoteInputTagRegexGen();

    [GeneratedRegex(@"<<\s*(\d+(?:\.\d+)+)\s+([^>\[\$]+?)>>")]
    private static partial Regex NoteTagRegexGen();

    [GeneratedRegex(@"<<\s*([^\d>\s][^>]*?)\s*>>")]
    private static partial Regex GeneralTagRegexGen();
}
