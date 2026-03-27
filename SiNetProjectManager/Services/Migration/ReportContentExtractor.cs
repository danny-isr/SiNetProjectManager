using System.Text.RegularExpressions;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using SiNetSQL.Services.InspectionSync;
using SiOffice.GoogleConnector.Reports;
using SheetsColor = Google.Apis.Sheets.v4.Data.Color;

namespace SiNetProjectManager.Services.Migration;

/// <summary>
/// Smart extractor that reads content from FINAL (filled) inspection reports.
/// <para>
/// Strategy — two-phase extraction with deep analysis:
/// <list type="number">
///   <item><b>Template scan</b>: Reads the original template to discover which cells held tags
///     (section code → row/col positions for status and note cells).</item>
///   <item><b>Report read</b>: Reads the final report with <c>includeGridData</c> for formatting.
///     For each template-mapped position, walks forward through the report (handling row shifts
///     from multi-note insertions) and extracts the status background color + note text.</item>
/// </list>
/// </para>
/// <para>
/// Smart capabilities:
/// <list type="bullet">
///   <item><b>Header-First Validation</b>: Verifies adjacent cells contain expected section numbers.</item>
///   <item><b>Sub-Section Decomposition</b>: Splits merged cells with Hebrew/numeric numbering patterns.</item>
///   <item><b>Visual Status Recovery</b>: Detects gray backgrounds as Resolved/Closed, extracts dates.</item>
///   <item><b>Hebrew Text Sanitization</b>: Strips BiDi control characters from all extracted text.</item>
/// </list>
/// </para>
/// </summary>
public sealed partial class ReportContentExtractor
{
    private readonly GoogleAuthService _authService;

    // ── Known export background colors (from GoogleReportExportService.GetStatusBackgroundColor) ──
    // Tolerance of ±0.08 per channel accounts for rounding in the Sheets API.
    private static readonly (string Key, float R, float G, float B)[] KnownStatusColors =
    [
        ("Passed",            0.85f, 0.95f, 0.85f),  // light green
        ("Failed",            0.95f, 0.85f, 0.85f),  // light red/pink
        ("RecurringFailed",   1.00f, 0.93f, 0.80f),  // light orange
        ("NotApplicable",     0.93f, 0.93f, 0.93f),  // light gray
        ("PartiallyResolved", 0.79f, 0.85f, 0.97f),  // light cornflower blue (#C9DAF8)
        ("PartiallyResolved", 0.64f, 0.76f, 0.96f),  // medium cornflower blue (#A4C2F4)
        ("PartiallyResolved", 0.81f, 0.89f, 0.95f),  // light blue (#CFE2F3)
    ];

    private const float ColorTolerance = 0.08f;

    /// <summary>Maximum channel delta for achromatic (gray) detection.</summary>
    private const float GrayChannelDelta = 0.06f;

    public ReportContentExtractor(GoogleAuthService authService)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
    }

    /// <summary>
    /// Extracts section data from a FINAL (filled) report using anchor-based title matching.
    /// <para>
    /// Algorithm:
    /// <list type="number">
    ///   <item><b>Template scan</b>: Finds tags, then reads the adjacent cell (one column to the RIGHT)
    ///     to get the section <b>title text</b> — the anchor for matching.</item>
    ///   <item><b>Report search</b>: Searches for each anchor title in the report by text match.</item>
    ///   <item><b>Adjacent read</b>: From the found title, reads status (one col forward),
    ///     designer response (two cols forward), and notes (one row below, one col forward).</item>
    /// </list>
    /// </para>
    /// </summary>
    public async Task<ReportExtractionResult> ExtractAsync(
        string templateSpreadsheetId,
        string reportSpreadsheetId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateSpreadsheetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportSpreadsheetId);

        var warnings = new List<string>();

        try
        {
            await _authService.EnsureAuthenticatedAsync(cancellationToken);
            var sheetsService = _authService.SheetsService
                ?? throw new InvalidOperationException("Sheets service not available after authentication.");

            // ════════════════════════════════════════════════════════
            // PHASE 1: Scan template for tags and build section map
            // ════════════════════════════════════════════════════════
            var templateRows = await ReadSheetValuesAsync(sheetsService, templateSpreadsheetId, cancellationToken);
            if (templateRows == null || templateRows.Count == 0)
            {
                return new ReportExtractionResult
                {
                    TemplateSpreadsheetId = templateSpreadsheetId,
                    ReportSpreadsheetId = reportSpreadsheetId,
                    IsSuccess = false,
                    ErrorMessage = "Template sheet is empty — no rows returned."
                };
            }

            var templateTags = GoogleReportExportService.ScanAllTemplateTags(templateRows);
            var sectionMap = BuildSectionMapFromTags(templateTags, warnings);

            if (sectionMap.Count == 0)
            {
                return new ReportExtractionResult
                {
                    TemplateSpreadsheetId = templateSpreadsheetId,
                    ReportSpreadsheetId = reportSpreadsheetId,
                    IsSuccess = false,
                    ErrorMessage = "No section tags found in template.",
                    Warnings = warnings
                };
            }

            // ════════════════════════════════════════════════════════
            // PHASE 2: Build anchor titles from template
            // For each tag → one column to the RIGHT → anchor title text
            // ════════════════════════════════════════════════════════
            var anchors = new List<SectionAnchor>();
            foreach (var (code, mapping) in sectionMap)
            {
                int titleCol = mapping.StatusCol - 1;
                if (titleCol < 0)
                {
                    warnings.Add($"Section {code}: status tag at col 0, no room for title column.");
                    continue;
                }

                var titleText = StripBidiMarks(GetTemplateValue(templateRows, mapping.StatusRow, titleCol)).Trim();
                if (string.IsNullOrWhiteSpace(titleText))
                {
                    warnings.Add($"Section {code}: empty title at R{mapping.StatusRow}C{titleCol}.");
                    continue;
                }

                int noteRowOffset = mapping.NoteRow - mapping.StatusRow;

                anchors.Add(new SectionAnchor(
                    Code: code,
                    ChapterTitle: mapping.ChapterTitle,
                    SectionTitle: mapping.SectionTitle,
                    AnchorText: titleText,
                    NoteRowOffset: noteRowOffset,
                    StatusTagText: mapping.StatusTagText,
                    NoteTagText: mapping.NoteTagText));
            }

            warnings.Add($"Template scan: {sectionMap.Count} tag pairs → {anchors.Count} anchors with titles.");

            // ════════════════════════════════════════════════════════
            // PHASE 3: Build general-field anchors from template
            // ════════════════════════════════════════════════════════
            var generalTags = templateTags.Where(t => t.IsGeneralTag).ToList();
            var generalAnchors = new List<(string Label, int TagCol)>();
            foreach (var gt in generalTags)
            {
                int labelCol = gt.Col - 1;
                if (labelCol < 0) continue;

                var label = StripBidiMarks(GetTemplateValue(templateRows, gt.Row, labelCol)).Trim();
                if (string.IsNullOrWhiteSpace(label)) continue;

                generalAnchors.Add((label, gt.Col));
            }

            // ════════════════════════════════════════════════════════
            // PHASE 4: Read the FINAL report with formatting data
            // ════════════════════════════════════════════════════════
            var (reportGridData, _, sheetTitle) = await ReadSheetWithFormattingAsync(
                sheetsService, reportSpreadsheetId, cancellationToken);

            if (reportGridData == null)
            {
                return new ReportExtractionResult
                {
                    TemplateSpreadsheetId = templateSpreadsheetId,
                    ReportSpreadsheetId = reportSpreadsheetId,
                    IsSuccess = false,
                    ErrorMessage = "Final report sheet returned no grid data.",
                    Warnings = warnings
                };
            }

            // ════════════════════════════════════════════════════════
            // PHASE 5: Build text → position index for the report
            // ════════════════════════════════════════════════════════
            var textIndex = BuildTextPositionIndex(reportGridData);

            // ════════════════════════════════════════════════════════
            // PHASE 6: Extract general fields
            // ════════════════════════════════════════════════════════
            var generalFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (label, tagCol) in generalAnchors)
            {
                if (!textIndex.TryGetValue(label, out var positions)) continue;

                foreach (var (r, c) in positions)
                {
                    var value = StripBidiMarks(GetCellText(reportGridData, r, c + 1));
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        generalFields[label] = value;
                        break;
                    }
                }
            }

            // ════════════════════════════════════════════════════════
            // PHASE 7: Extract section data (anchor-based matching)
            // ════════════════════════════════════════════════════════
            var sections = new List<ExtractedSectionData>();

            foreach (var anchor in anchors.OrderBy(a => a.Code, StringComparer.Ordinal))
            {
                // ── Find the anchor title in the report ──
                var found = FindAnchorInReport(textIndex, reportGridData, anchor.AnchorText);
                if (found == null)
                {
                    warnings.Add($"Section {anchor.Code}: anchor not found in report — \"{anchor.AnchorText}\"");
                    continue;
                }

                var (reportRow, titleCol) = found.Value;
                int valueCol = titleCol + 1;
                int designerCol = titleCol + 2;
                int noteRow = reportRow + anchor.NoteRowOffset;

                // ── Read status (one col forward from title) ──
                var statusColor = GetCellBackgroundColor(reportGridData, reportRow, valueCol);
                var statusKey = ClassifyColor(statusColor);
                var statusText = StripBidiMarks(GetCellText(reportGridData, reportRow, valueCol));

                if (statusKey == null)
                    statusKey = InferStatusFromText(statusText);

                // ── Visual status recovery: gray background ──
                bool isResolved = IsGrayBackground(statusColor);

                // ── Blue background → partially resolved ──
                if (statusKey == null && !isResolved)
                {
                    var noteColorForBlue = GetCellBackgroundColor(reportGridData, noteRow, valueCol);
                    if (ClassifyColor(statusColor) == "PartiallyResolved"
                        || ClassifyColor(noteColorForBlue) == "PartiallyResolved")
                    {
                        statusKey = "PartiallyResolved";
                    }
                }

                // ── Read note text (one row below, same value column — full cell, no splitting) ──
                var noteText = StripBidiMarks(GetCellText(reportGridData, noteRow, valueCol));

                var noteColor = GetCellBackgroundColor(reportGridData, noteRow, valueCol);
                if (IsGrayBackground(noteColor))
                    isResolved = true;

                // ── Read designer response (next column after status) ──
                var designerResponse = StripBidiMarks(GetCellText(reportGridData, reportRow, designerCol));
                var noteDesignerResponse = StripBidiMarks(GetCellText(reportGridData, noteRow, designerCol));

                // Combine: if notes row has a different designer response, append it
                if (!string.IsNullOrWhiteSpace(noteDesignerResponse) && noteDesignerResponse != designerResponse)
                {
                    designerResponse = string.IsNullOrWhiteSpace(designerResponse)
                        ? noteDesignerResponse
                        : $"{designerResponse}\n---\n{noteDesignerResponse}";
                }

                // ── Closure date from note text ──
                DateTime? closedDate = NoteSplitter.ExtractClosureDate(noteText);
                if (closedDate != null)
                    isResolved = true;

                // ── Text-based status: "בוצע" / "בוצע חלקית" / "תוקן" ──
                if (statusKey == null || statusKey == "Unknown"
                    || (statusKey == "Failed" && !string.IsNullOrWhiteSpace(noteText)))
                {
                    var execStatus = NoteSplitter.DetectExecutionStatus(noteText);
                    if (execStatus == "Resolved")
                    {
                        isResolved = true;
                        statusKey ??= "Resolved";
                    }
                    else if (execStatus == "PartiallyResolved")
                    {
                        statusKey ??= "PartiallyResolved";
                    }
                }

                sections.Add(new ExtractedSectionData
                {
                    SectionCode = anchor.Code,
                    ChapterTitle = anchor.ChapterTitle,
                    SectionTitle = anchor.SectionTitle,
                    StatusText = statusText,
                    StatusKey = statusKey ?? (isResolved ? "Resolved" : "Unknown"),
                    StatusColorHex = ColorToHex(statusColor),
                    NoteText = noteText,
                    DesignerResponse = designerResponse,
                    NoteSubIndex = "",
                    ReportRow = reportRow,
                    DetectionMethod = "anchor-title",
                    OriginalCellRef = ColumnToRef(valueCol, reportRow),
                    WasSplit = false,
                    SplitIndex = 0,
                    SplitSourceText = "",
                    ClosedDate = closedDate,
                    IsResolved = isResolved,
                    HeaderValidation = anchor.AnchorText,
                    TemplateStatusTag = anchor.StatusTagText,
                    TemplateNoteTag = anchor.NoteTagText
                });
            }

            return new ReportExtractionResult
            {
                TemplateSpreadsheetId = templateSpreadsheetId,
                ReportSpreadsheetId = reportSpreadsheetId,
                Sections = sections,
                GeneralFields = generalFields,
                Warnings = warnings,
                IsSuccess = true
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ReportExtractionResult
            {
                TemplateSpreadsheetId = templateSpreadsheetId,
                ReportSpreadsheetId = reportSpreadsheetId,
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Warnings = warnings
            };
        }
    }

    #region Anchor Model & Matching

    /// <summary>
    /// Internal record linking a section code to its anchor title text extracted from the template.
    /// </summary>
    private sealed record SectionAnchor(
        string Code,
        string ChapterTitle,
        string SectionTitle,
        string AnchorText,
        int NoteRowOffset,
        string StatusTagText,
        string NoteTagText);

    /// <summary>
    /// Reads a cell value from the template (text-only data, no formatting).
    /// </summary>
    private static string GetTemplateValue(IList<IList<object>> rows, int row, int col)
    {
        if (row < 0 || row >= rows.Count) return "";
        var r = rows[row];
        if (r == null || col < 0 || col >= r.Count) return "";
        return r[col]?.ToString() ?? "";
    }

    /// <summary>
    /// Builds an index mapping stripped cell text to all positions where it appears in the report.
    /// Used for fast anchor-title lookups.
    /// </summary>
    private static Dictionary<string, List<(int Row, int Col)>> BuildTextPositionIndex(IList<RowData> gridRows)
    {
        var index = new Dictionary<string, List<(int, int)>>(StringComparer.OrdinalIgnoreCase);

        for (int r = 0; r < gridRows.Count; r++)
        {
            var rowData = gridRows[r];
            if (rowData?.Values == null) continue;

            for (int c = 0; c < rowData.Values.Count; c++)
            {
                var text = GetCellText(gridRows, r, c);
                if (string.IsNullOrWhiteSpace(text)) continue;

                var stripped = StripBidiMarks(text).Trim();
                if (string.IsNullOrWhiteSpace(stripped)) continue;

                if (!index.TryGetValue(stripped, out var list))
                {
                    list = [];
                    index[stripped] = list;
                }

                list.Add((r, c));
            }
        }

        return index;
    }

    /// <summary>
    /// Finds an anchor title in the report using multi-strategy matching:
    /// <list type="number">
    ///   <item>Exact match (fastest, dictionary lookup).</item>
    ///   <item>Normalized match — collapses NBSP / newlines / extra spaces before comparing.</item>
    ///   <item>Best partial match — picks the <b>longest</b> overlap; requires ≥10 char key
    ///     for reverse containment to prevent false positives on short cells like "1".</item>
    ///   <item>Section-code prefix match (e.g. "1.6 " at start of cell text).</item>
    /// </list>
    /// </summary>
    private static (int Row, int Col)? FindAnchorInReport(
        Dictionary<string, List<(int Row, int Col)>> textIndex,
        IList<RowData> gridRows,
        string anchorText)
    {
        // Strategy 1: Exact match (after BiDi stripping + trim)
        if (textIndex.TryGetValue(anchorText, out var exactMatches) && exactMatches.Count > 0)
            return exactMatches[0];

        // Strategy 2: Normalized match — collapse NBSP / newlines / multi-spaces
        var normalizedAnchor = NormalizeForMatching(anchorText);
        foreach (var (key, positions) in textIndex)
        {
            if (NormalizeForMatching(key).Equals(normalizedAnchor, StringComparison.OrdinalIgnoreCase))
                return positions[0];
        }

        // Strategy 3: Best partial match — collect ALL candidates, pick the longest overlap.
        //   • Cell text contains the full anchor → high confidence (score = anchor length × 2)
        //   • Anchor text contains the cell text → only if key ≥ 10 chars
        //     (prevents matching short values like "1", "תב\"ע", etc.)
        (int Row, int Col)? bestPartial = null;
        int bestScore = 0;

        foreach (var (key, positions) in textIndex)
        {
            var normKey = NormalizeForMatching(key);

            // Cell fully contains the anchor (raw or normalized)
            if (key.Contains(anchorText, StringComparison.OrdinalIgnoreCase)
                || normKey.Contains(normalizedAnchor, StringComparison.OrdinalIgnoreCase))
            {
                int score = anchorText.Length * 2;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestPartial = positions[0];
                }
            }
            // Anchor contains the cell text — require substantial length
            else if (key.Length >= 10
                && (anchorText.Contains(key, StringComparison.OrdinalIgnoreCase)
                    || normalizedAnchor.Contains(normKey, StringComparison.OrdinalIgnoreCase)))
            {
                if (key.Length > bestScore)
                {
                    bestScore = key.Length;
                    bestPartial = positions[0];
                }
            }
        }

        if (bestPartial != null)
            return bestPartial;

        // Strategy 4: Match by section code prefix (e.g. "1.6 " at start of cell text)
        var codePrefix = anchorText.Split(' ', 2)[0];
        if (codePrefix.Contains('.'))
        {
            var prefixWithSpace = codePrefix + " ";
            foreach (var (key, positions) in textIndex)
            {
                if (key.StartsWith(prefixWithSpace, StringComparison.OrdinalIgnoreCase))
                    return positions[0];
            }
        }

        return null;
    }

    #endregion

    #region Google Sheets I/O

    /// <summary>
    /// Reads all cell values from the first sheet of a spreadsheet (text only, no formatting).
    /// </summary>
    private static async Task<IList<IList<object>>?> ReadSheetValuesAsync(
        SheetsService sheetsService, string spreadsheetId, CancellationToken ct)
    {
        // First get sheet metadata for the title
        var meta = await sheetsService.Spreadsheets.Get(spreadsheetId).ExecuteAsync(ct);
        var title = meta.Sheets[0].Properties.Title ?? "Sheet1";
        var totalRows = meta.Sheets[0].Properties.GridProperties?.RowCount ?? 1200;

        var response = await sheetsService.Spreadsheets.Values
            .Get(spreadsheetId, $"'{title}'!A1:Z{totalRows}")
            .ExecuteAsync(ct);

        return response.Values;
    }

    /// <summary>
    /// Reads the first sheet of a spreadsheet WITH cell formatting data (background colors).
    /// Returns the grid data, value rows, and sheet title.
    /// </summary>
    private static async Task<(IList<RowData>? GridRows, IList<IList<object>>? Values, string Title)>
        ReadSheetWithFormattingAsync(SheetsService sheetsService, string spreadsheetId, CancellationToken ct)
    {
        // Get full spreadsheet with grid data — request only the fields we need
        var getRequest = sheetsService.Spreadsheets.Get(spreadsheetId);
        getRequest.IncludeGridData = true;
        getRequest.Fields = "sheets.properties.title,sheets.data.rowData.values(effectiveFormat.backgroundColor,effectiveValue,formattedValue)";

        var spreadsheet = await getRequest.ExecuteAsync(ct);
        var sheet = spreadsheet.Sheets[0];
        var title = sheet.Properties.Title ?? "Sheet1";

        var gridData = sheet.Data?.FirstOrDefault();
        return (gridData?.RowData, null, title);
    }

    #endregion

    #region Cell Access Helpers

    private static string GetCellText(IList<RowData>? gridRows, int row, int col)
    {
        if (gridRows == null || row < 0 || row >= gridRows.Count)
            return "";

        var rowData = gridRows[row];
        if (rowData?.Values == null || col < 0 || col >= rowData.Values.Count)
            return "";

        var cell = rowData.Values[col];
        // Prefer formattedValue (what the user sees), then effectiveValue.stringValue
        return cell?.FormattedValue
            ?? cell?.EffectiveValue?.StringValue
            ?? cell?.EffectiveValue?.NumberValue?.ToString()
            ?? "";
    }

    private static SheetsColor? GetCellBackgroundColor(IList<RowData>? gridRows, int row, int col)
    {
        if (gridRows == null || row < 0 || row >= gridRows.Count)
            return null;

        var rowData = gridRows[row];
        if (rowData?.Values == null || col < 0 || col >= rowData.Values.Count)
            return null;

        return rowData.Values[col]?.EffectiveFormat?.BackgroundColor;
    }

    private static int GetRowCount(IList<RowData>? gridRows) => gridRows?.Count ?? 0;

    #endregion

    #region Color Classification

    /// <summary>
    /// Classifies a Google Sheets background color into a known status key.
    /// Returns <c>null</c> if the color doesn't match any known status (e.g., white/default).
    /// </summary>
    private static string? ClassifyColor(SheetsColor? color)
    {
        if (color == null) return null;

        float r = color.Red ?? 1f;
        float g = color.Green ?? 1f;
        float b = color.Blue ?? 1f;

        // Skip white/near-white (default background)
        if (r > 0.97f && g > 0.97f && b > 0.97f)
            return null;

        foreach (var (key, kr, kg, kb) in KnownStatusColors)
        {
            if (Math.Abs(r - kr) < ColorTolerance &&
                Math.Abs(g - kg) < ColorTolerance &&
                Math.Abs(b - kb) < ColorTolerance)
            {
                return key;
            }
        }

        return null;
    }

    /// <summary>
    /// Falls back to text-based status inference when color detection fails.
    /// </summary>
    private static string? InferStatusFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var t = text.Trim();
        return t switch
        {
            "מקובל" or "V" or "Passed" or "תקין" => "Passed",
            "הערה" or "X" or "Failed" => "Failed",
            "הערה חוזרת" or "!" or "RecurringFailed" => "RecurringFailed",
            "לא רלוונטי" or "—" or "NotApplicable" or "N/A" => "NotApplicable",
            "בוצע" or "תוקן" or "Resolved" => "Resolved",
            "בוצע חלקית" or "PartiallyResolved" => "PartiallyResolved",
            _ => null
        };
    }

    /// <summary>
    /// Scans a column range for the first cell with a known status background color.
    /// </summary>
    private static (int Row, string? Key) ScanForStatusColor(
        IList<RowData>? gridRows, int col, int fromRow, int toRow)
    {
        if (gridRows == null) return (-1, null);

        int start = Math.Max(0, fromRow);
        int end = Math.Min(gridRows.Count - 1, toRow);

        for (int row = start; row <= end; row++)
        {
            var color = GetCellBackgroundColor(gridRows, row, col);
            var key = ClassifyColor(color);
            if (key != null)
                return (row, key);
        }

        return (-1, null);
    }

    private static string ColorToHex(SheetsColor? color)
    {
        if (color == null) return "#FFFFFF";

        int r = (int)((color.Red ?? 1f) * 255);
        int g = (int)((color.Green ?? 1f) * 255);
        int b = (int)((color.Blue ?? 1f) * 255);

        return $"#{r:X2}{g:X2}{b:X2}";
    }

    /// <summary>
    /// Detects if a color is gray (achromatic): all channels roughly equal,
    /// not white and not too dark. Used to identify resolved/closed items in historical reports.
    /// </summary>
    private static bool IsGrayBackground(SheetsColor? color)
    {
        if (color == null) return false;

        float r = color.Red ?? 1f;
        float g = color.Green ?? 1f;
        float b = color.Blue ?? 1f;

        // Skip white / near-white
        if (r > 0.96f && g > 0.96f && b > 0.96f)
            return false;

        // Skip very dark (black-ish)
        float avg = (r + g + b) / 3f;
        if (avg < 0.35f)
            return false;

        // Gray = all channels within a small delta of each other
        return Math.Abs(r - g) < GrayChannelDelta
            && Math.Abs(g - b) < GrayChannelDelta
            && Math.Abs(r - b) < GrayChannelDelta;
    }

    #endregion

    #region Header-First Validation

    /// <summary>
    /// Checks columns A (0) and B (1) at the given row for a section number pattern (e.g. "1.1", "2.3").
    /// Returns the found text or <c>null</c> if no section-like content was detected.
    /// </summary>
    private static string? ValidateHeaderAtRow(IList<RowData>? gridRows, int row, string expectedCode)
    {
        for (int col = 0; col <= 1; col++)
        {
            var text = GetCellText(gridRows, row, col).Trim();
            if (string.IsNullOrEmpty(text)) continue;

            // Check if it looks like a section number (e.g. "1.1", "2.3", "3.1")
            if (SectionNumberPattern().IsMatch(text))
                return text;

            // Also return known Hebrew chapter/section titles as validation
            if (text.Length >= 2 && text.Length <= 50)
                return text;
        }

        return null;
    }

    [GeneratedRegex(@"^\d+\.\d+", RegexOptions.Compiled)]
    private static partial Regex SectionNumberPattern();

    #endregion

    #region Cell Reference Utility

    /// <summary>
    /// Converts a zero-based column index and row index to a spreadsheet-style cell reference (e.g. "C15").
    /// </summary>
    private static string ColumnToRef(int col, int row)
    {
        var colRef = new System.Text.StringBuilder();
        int c = col;
        do
        {
            colRef.Insert(0, (char)('A' + c % 26));
            c = c / 26 - 1;
        }
        while (c >= 0);

        return $"{colRef}{row + 1}"; // Sheets uses 1-based rows
    }

    #endregion

    #region Template Tag Processing

    /// <summary>
    /// Internal mapping record for a single section's tag positions in the template.
    /// </summary>
    private sealed record SectionMapping(
        int StatusRow, int StatusCol,
        int NoteRow, int NoteCol,
        string ChapterTitle, string SectionTitle,
        string StatusTagText, string NoteTagText);

    /// <summary>
    /// Builds a section map from scanned template tags.
    /// Groups status tags and note-input tags by section code to create paired mappings.
    /// </summary>
    private static Dictionary<string, SectionMapping> BuildSectionMapFromTags(
        List<TemplateScanTag> tags, List<string> warnings)
    {
        var statusTags = new Dictionary<string, TemplateScanTag>(StringComparer.Ordinal);
        var noteTags = new Dictionary<string, TemplateScanTag>(StringComparer.Ordinal);

        foreach (var tag in tags)
        {
            if (tag.IsGeneralTag) continue;
            if (string.IsNullOrEmpty(tag.SectionCode)) continue;

            if (tag.IsStatusTag)
            {
                statusTags.TryAdd(tag.SectionCode, tag);
            }
            else if (tag.IsNoteInputTag)
            {
                noteTags.TryAdd(tag.SectionCode, tag);
            }
            else // Legacy note tag <<X.Y Title>>
            {
                noteTags.TryAdd(tag.SectionCode, tag);
            }
        }

        var map = new Dictionary<string, SectionMapping>(StringComparer.Ordinal);

        foreach (var (code, statusTag) in statusTags)
        {
            if (!noteTags.TryGetValue(code, out var noteTag))
            {
                warnings.Add($"Section {code}: has status tag at R{statusTag.Row}C{statusTag.Col} but no note tag — skipping.");
                continue;
            }

            map[code] = new SectionMapping(
                StatusRow: statusTag.Row,
                StatusCol: statusTag.Col,
                NoteRow: noteTag.Row,
                NoteCol: noteTag.Col,
                ChapterTitle: statusTag.Title ?? "",
                SectionTitle: statusTag.DefaultText ?? "",
                StatusTagText: ReconstructTagText(statusTag),
                NoteTagText: ReconstructTagText(noteTag)
            );
        }

        // Report orphaned note tags
        foreach (var (code, noteTag) in noteTags)
        {
            if (!statusTags.ContainsKey(code))
                warnings.Add($"Section {code}: has note tag at R{noteTag.Row}C{noteTag.Col} but no status tag — skipping.");
        }

        return map;
    }

    #endregion

    #region Utility

    /// <summary>
    /// Reconstructs the original template tag text from a <see cref="TemplateScanTag"/>.
    /// Example outputs: <c>&lt;&lt;Status_3.6 חניה [גישה לחניות]&gt;&gt;</c>, <c>&lt;&lt;3.6 $&gt;&gt;</c>.
    /// </summary>
    private static string ReconstructTagText(TemplateScanTag tag)
    {
        if (tag.IsGeneralTag)
            return $"<<{tag.GeneralTagLabel}>>";

        if (tag.IsNoteInputTag)
            return $"<<{tag.SectionCode} $>>";

        // Status tag: <<X.Y Title [DefaultText]>>
        var title = tag.Title ?? "";
        var defaultText = tag.DefaultText ?? "";
        return string.IsNullOrEmpty(defaultText)
            ? $"<<{tag.SectionCode} {title}>>"
            : $"<<{tag.SectionCode} {title} [{defaultText}]>>";
    }

    /// <summary>
    /// Strips Unicode BiDi control characters (same as GoogleReportExportService.StripBidiMarks).
    /// </summary>
    private static string StripBidiMarks(string text) =>
        string.IsNullOrEmpty(text) ? "" : BidiMarksPattern().Replace(text, "");

    [GeneratedRegex(@"[\u200E\u200F\u202A-\u202E\u2066-\u2069]")]
    private static partial Regex BidiMarksPattern();

    /// <summary>
    /// Normalizes text for anchor matching: strips BiDi marks, replaces all whitespace
    /// variants (NBSP U+00A0, newlines, tabs) with regular spaces, collapses runs, trims.
    /// </summary>
    private static string NormalizeForMatching(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var stripped = StripBidiMarks(text);
        return WhitespaceCollapser.Replace(stripped, " ").Trim();
    }

    private static readonly Regex WhitespaceCollapser = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Extracts a Google Sheets spreadsheet ID from various URL formats.
    /// Supports: full URL, /d/ID/ pattern, or raw ID.
    /// </summary>
    public static string ExtractSpreadsheetId(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";

        input = input.Trim();

        // Pattern: https://docs.google.com/spreadsheets/d/SPREADSHEET_ID/...
        var match = SpreadsheetIdPattern().Match(input);
        if (match.Success) return match.Groups[1].Value;

        // Already a raw ID (alphanumeric, hyphens, underscores, 20+ chars)
        if (RawSpreadsheetIdPattern().IsMatch(input))
            return input;

        return input;
    }

    [GeneratedRegex(@"/spreadsheets/d/([a-zA-Z0-9_-]+)")]
    private static partial Regex SpreadsheetIdPattern();

    [GeneratedRegex(@"^[a-zA-Z0-9_-]{20,}$")]
    private static partial Regex RawSpreadsheetIdPattern();

    #endregion
}
