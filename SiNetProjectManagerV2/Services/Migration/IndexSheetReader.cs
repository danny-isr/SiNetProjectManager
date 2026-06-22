using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using SiOffice.GoogleConnector.Reports;

namespace SiNetProjectManagerV2.Services.Migration;

/// <summary>
/// Reads and parses a Google Sheets "Index" sheet that tracks inspection report visits.
/// <para>
/// The index sheet typically has a header row with Hebrew column names, followed by data rows.
/// This reader auto-detects the header row (scanning first 10 rows) and maps columns
/// using a flexible Hebrew alias dictionary.
/// </para>
/// </summary>
public sealed class IndexSheetReader
{
    private readonly GoogleAuthService _authService;

    // ── Canonical column keys ──
    private const string ColProject = "Project";
    private const string ColReportNumber = "ReportNumber";
    private const string ColDate = "Date";
    private const string ColInspector = "Inspector";
    private const string ColStatus = "Status";
    private const string ColLink = "Link";
    private const string ColLinkVersions = "LinkVersions";
    private const string ColNotes = "Notes";
    private const string ColEmail = "Email";

    /// <summary>
    /// Hebrew aliases for each canonical column key.
    /// The header cell text is trimmed and compared case-insensitively.
    /// </summary>
    private static readonly Dictionary<string, string[]> ColumnAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        [ColProject] = ["פרויקט", "שם פרויקט", "מספר פרויקט", "מס׳ פרויקט", "מס' פרויקט", "Project", "ProjectId", "פרויקט/מתחם"],
        [ColReportNumber] = ["מס׳ ביקורת", "מס' ביקורת", "מספר ביקורת", "ביקורת מס", "מס׳ דוח", "מס' דוח", "מספר דוח", "#"],
        [ColDate] = ["תאריך", "תאריך ביקורת", "תאריך בדיקה", "Date"],
        [ColInspector] = ["בודק", "שם בודק", "מבקר", "בודק/ת", "Inspector",
                          "בודק/ת:", "שם הבודק", "שם המבקר", "שם הבודקת",
                          "בודקת", "מבקר/ת", "ביצוע", "ביצוע ע\"י", "מבצע",
                          "עורך הביקורת", "עורך", "פקח", "שם פקח",
                          // ממלא דוח אחרון variants — used when no explicit בודק column exists
                          "ממלא דוח אחרון", "ממלא הדוח האחרון",
                          "שם ממלא דוח אחרון", "שם ממלא הדוח האחרון",
                          "עורך דוח אחרון", "עורך הדוח האחרון",
                          "אחראי דוח אחרון", "אחראי הדוח האחרון"],
        [ColEmail] = ["אימייל", "דוא\"ל", "מייל", "Email", "דואר אלקטרוני", "כתובת מייל", "mail",
                      // ממלא דוח אחרון email variants
                      "מייל ממלא הדוח האחרון", "מייל ממלא דוח אחרון",
                      "אימייל ממלא הדוח האחרון", "דוא\"ל ממלא הדוח האחרון"],
        [ColStatus] = ["סטטוס", "מצב", "Status", "סטאטוס"],
        [ColLink] = ["קישור", "קישור לדוח", "לינק", "Link", "URL"],
        [ColLinkVersions] = ["גרסאות הגליון", "קישורים לגרסאות הגליון"],
        [ColNotes] = ["הערות", "הערה", "Notes", "Comments", "תיאור"],
    };

    /// <summary>
    /// Status values that indicate a report is approved/completed.
    /// Comparison is done after trimming whitespace.
    /// </summary>
    private static readonly HashSet<string> ApprovedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "מאושר", "אושר", "הושלם", "V", "תקין",
        "approved", "completed", "done",
        "מאושר ללא הערות", "מאושר עם הערות",
    };

    /// <summary>Maximum header rows to scan before giving up.</summary>
    private const int MaxHeaderScanRows = 10;

    /// <summary>Minimum columns that must match to accept a header row.</summary>
    private const int MinHeaderMatchCount = 2;

    public IndexSheetReader(GoogleAuthService authService)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
    }

    /// <summary>
    /// Reads the index sheet from a Google Spreadsheet and returns parsed rows with unique statuses.
    /// </summary>
    public async Task<IndexSheetResult> ReadAsync(
        string spreadsheetId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spreadsheetId);

        var warnings = new List<string>();

        try
        {
            await _authService.EnsureAuthenticatedAsync(cancellationToken);
            var sheetsService = _authService.SheetsService
                ?? throw new InvalidOperationException("Sheets service not available after authentication.");

            var rows = await ReadSheetValuesAsync(sheetsService, spreadsheetId, cancellationToken);
            if (rows == null || rows.Count == 0)
            {
                return new IndexSheetResult
                {
                    IsSuccess = false,
                    ErrorMessage = "Index sheet is empty — no rows returned."
                };
            }

            // ── Detect header row ──
            var (headerRowIndex, columnMapping) = DetectHeaderRow(rows, warnings);
            if (headerRowIndex < 0)
            {
                return new IndexSheetResult
                {
                    IsSuccess = false,
                    ErrorMessage = $"Could not detect a header row in the first {MaxHeaderScanRows} rows. " +
                        "Expected at least 2 recognized Hebrew column names (e.g. סטטוס, תאריך, מס׳ ביקורת)."
                };
            }

            warnings.Add($"Header detected at row {headerRowIndex + 1} with {columnMapping.Count} mapped columns: " +
                string.Join(", ", columnMapping.Select(kv => $"{kv.Key}=col{kv.Value}")));

            // ── Parse data rows ──
            var parsedRows = new List<IndexSheetRow>();
            var uniqueStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int r = headerRowIndex + 1; r < rows.Count; r++)
            {
                var row = rows[r];
                if (IsEmptyRow(row))
                    continue;

                var status = GetMappedValue(row, columnMapping, ColStatus);
                var reportNumber = GetMappedValue(row, columnMapping, ColReportNumber);
                var date = GetMappedValue(row, columnMapping, ColDate);
                var inspector = GetMappedValue(row, columnMapping, ColInspector);
                var email = GetMappedValue(row, columnMapping, ColEmail);
                var link = GetMappedValue(row, columnMapping, ColLink);
                var notes = GetMappedValue(row, columnMapping, ColNotes);
                var project = GetMappedValue(row, columnMapping, ColProject);

                // Skip rows with no meaningful data
                if (string.IsNullOrWhiteSpace(status) && string.IsNullOrWhiteSpace(reportNumber) && string.IsNullOrWhiteSpace(date))
                    continue;

                var isApproved = !string.IsNullOrWhiteSpace(status) && ApprovedStatuses.Contains(status.Trim());

                parsedRows.Add(new IndexSheetRow
                {
                    RowIndex = r,
                    ProjectIdOrName = project,
                    ReportNumber = reportNumber,
                    InspectionDate = date,
                    InspectorName = inspector,
                    InspectorEmail = email,
                    Status = status,
                    ReportLink = link,
                    Notes = notes,
                    IsApproved = isApproved,
                });

                if (!string.IsNullOrWhiteSpace(status))
                    uniqueStatuses.Add(status.Trim());
            }

            if (parsedRows.Count == 0)
            {
                warnings.Add("No data rows found after the header row.");
            }

            return new IndexSheetResult
            {
                Rows = parsedRows,
                UniqueStatuses = [.. uniqueStatuses.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)],
                Warnings = warnings,
                IsSuccess = true,
                HeaderRow = headerRowIndex,
                ColumnMapping = new Dictionary<string, int>(columnMapping, StringComparer.OrdinalIgnoreCase),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new IndexSheetResult
            {
                IsSuccess = false,
                ErrorMessage = $"Failed to read index sheet: {ex.Message}",
                Warnings = warnings,
            };
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Header Detection
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scans the first <see cref="MaxHeaderScanRows"/> rows to find the header.
    /// Returns the row index and a mapping of canonical column key → column index.
    /// </summary>
    private static (int RowIndex, Dictionary<string, int> Mapping) DetectHeaderRow(
        IList<IList<object>> rows, List<string> warnings)
    {
        int bestRow = -1;
        Dictionary<string, int> bestMapping = new();

        int scanLimit = Math.Min(rows.Count, MaxHeaderScanRows);
        for (int r = 0; r < scanLimit; r++)
        {
            var mapping = TryMapHeaderColumns(rows[r]);
            if (mapping.Count >= MinHeaderMatchCount && mapping.Count > bestMapping.Count)
            {
                bestRow = r;
                bestMapping = mapping;
            }
        }

        if (bestRow < 0)
            warnings.Add($"Header detection failed in first {scanLimit} rows.");

        return (bestRow, bestMapping);
    }

    /// <summary>
    /// Tries to match each cell in a row to a canonical column key using the alias dictionary.
    /// </summary>
    private static Dictionary<string, int> TryMapHeaderColumns(IList<object> row)
    {
        var mapping = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int c = 0; c < row.Count; c++)
        {
            var cellText = row[c]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(cellText))
                continue;

            foreach (var (canonicalKey, aliases) in ColumnAliases)
            {
                if (mapping.ContainsKey(canonicalKey))
                    continue;

                foreach (var alias in aliases)
                {
                    if (cellText.Equals(alias, StringComparison.OrdinalIgnoreCase) ||
                        cellText.Contains(alias, StringComparison.OrdinalIgnoreCase))
                    {
                        mapping[canonicalKey] = c;
                        break;
                    }
                }
            }
        }

        return mapping;
    }

    // ════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════

    private static string GetMappedValue(IList<object> row, Dictionary<string, int> mapping, string key)
    {
        if (!mapping.TryGetValue(key, out var col) || col >= row.Count)
            return string.Empty;

        return row[col]?.ToString()?.Trim() ?? string.Empty;
    }

    private static bool IsEmptyRow(IList<object> row)
    {
        if (row.Count == 0) return true;
        return row.All(cell => string.IsNullOrWhiteSpace(cell?.ToString()));
    }

    /// <summary>
    /// Reads all cell values from the first sheet of a spreadsheet (text only).
    /// </summary>
    private static async Task<IList<IList<object>>?> ReadSheetValuesAsync(
        SheetsService sheetsService, string spreadsheetId, CancellationToken ct)
    {
        var meta = await sheetsService.Spreadsheets.Get(spreadsheetId).ExecuteAsync(ct);
        var title = meta.Sheets[0].Properties.Title ?? "Sheet1";
        var totalRows = meta.Sheets[0].Properties.GridProperties?.RowCount ?? 1200;

        var response = await sheetsService.Spreadsheets.Values
            .Get(spreadsheetId, $"'{title}'!A1:Z{totalRows}")
            .ExecuteAsync(ct);

        return response.Values;
    }

    // ════════════════════════════════════════════════════════════════
    //  Hyperlink Reading (for resolving actual report spreadsheet IDs)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reads the index sheet with grid data to extract report hyperlinks from the links column.
    /// Each cell in the links column may contain one or more hyperlinks (e.g. "1, 2, 3, 4"
    /// where each number is a clickable link to a report version).
    /// </summary>
    /// <param name="log">Optional logging callback for diagnostics.</param>
    /// <param name="includeRowsWithoutLinks">
    /// When <c>true</c>, rows that have valid project/status/reviewer data but no report
    /// hyperlinks are still included with empty <see cref="IndexSheetReportLink.ReportSpreadsheetIds"/>.
    /// Default is <c>false</c> to preserve legacy extraction behavior.
    /// </param>
    public async Task<List<IndexSheetReportLink>> ReadReportHyperlinksAsync(
        string spreadsheetId,
        Action<string>? log = null,
        CancellationToken cancellationToken = default,
        bool includeRowsWithoutLinks = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spreadsheetId);

        await _authService.EnsureAuthenticatedAsync(cancellationToken);
        var sheetsService = _authService.SheetsService
            ?? throw new InvalidOperationException("Sheets service not available.");

        // Read with grid data — request all hyperlink-related fields
        var getRequest = sheetsService.Spreadsheets.Get(spreadsheetId);
        getRequest.IncludeGridData = true;
        getRequest.Fields = "sheets(properties.title,data.rowData.values(" +
            "formattedValue,hyperlink,textFormatRuns,effectiveFormat.textFormat.link,userEnteredValue))";

        var spreadsheet = await getRequest.ExecuteAsync(cancellationToken);
        var sheet = spreadsheet.Sheets[0];
        log?.Invoke($"Sheet title: '{sheet.Properties?.Title}'");

        var gridData = sheet.Data?.FirstOrDefault()?.RowData;
        if (gridData == null || gridData.Count == 0)
        {
            log?.Invoke("Grid data is empty — no rows returned.");
            return [];
        }

        log?.Invoke($"Grid data: {gridData.Count} rows.");

        // Convert grid data to text rows for header detection (reusing existing logic)
        var textRows = new List<IList<object>>();
        for (int r = 0; r < gridData.Count; r++)
        {
            var rowData = gridData[r];
            var row = new List<object>();
            if (rowData?.Values != null)
            {
                for (int c = 0; c < rowData.Values.Count; c++)
                    row.Add((object?)rowData.Values[c]?.FormattedValue ?? "");
            }
            textRows.Add(row);
        }

        // Detect header row using existing alias matching
        var warnings = new List<string>();
        var (headerRowIndex, columnMapping) = DetectHeaderRow(textRows, warnings);
        if (headerRowIndex < 0)
        {
            log?.Invoke($"Header detection failed. First row cells: [{string.Join(" | ", textRows.FirstOrDefault()?.Select(c => c?.ToString() ?? "") ?? [])}]");
            return [];
        }

        log?.Invoke($"Header at row {headerRowIndex + 1}. Mapped columns: {string.Join(", ", columnMapping.Select(kv => $"{kv.Key}=col{kv.Value}"))}");

        // Must have the project column
        if (!columnMapping.TryGetValue(ColProject, out var projectCol))
        {
            log?.Invoke("❌ 'Project' column not detected in header.");
            return [];
        }

        // Use the specific versions column (קישורים לגרסאות הגליון), fall back to generic Link
        bool hasLinkCol;
        int linkCol = -1;
        if (columnMapping.TryGetValue(ColLinkVersions, out var linkColVersions))
        {
            linkCol = linkColVersions;
            hasLinkCol = true;
        }
        else if (columnMapping.TryGetValue(ColLink, out var linkColGeneric))
        {
            linkCol = linkColGeneric;
            hasLinkCol = true;
            log?.Invoke("⚠ 'LinkVersions' column not found, falling back to 'Link' column.");
        }
        else
        {
            hasLinkCol = false;
            if (!includeRowsWithoutLinks)
            {
                log?.Invoke("❌ Neither 'LinkVersions' nor 'Link' column detected in header.");
                return [];
            }
            log?.Invoke("⚠ Neither 'LinkVersions' nor 'Link' column detected — continuing without link extraction (includeRowsWithoutLinks=true).");
        }

        columnMapping.TryGetValue(ColReportNumber, out var reportNumCol);
        var hasReportNumCol = columnMapping.ContainsKey(ColReportNumber);

        columnMapping.TryGetValue(ColInspector, out var reviewerCol);
        var hasReviewerCol = columnMapping.ContainsKey(ColInspector);

        // Resolve the actual header name used for the reviewer column so the log is actionable
        string? reviewerHeaderName = null;
        if (hasReviewerCol && headerRowIndex >= 0)
        {
            var headerRow = textRows[headerRowIndex];
            if (reviewerCol < headerRow.Count)
                reviewerHeaderName = headerRow[reviewerCol]?.ToString()?.Trim();
        }

        columnMapping.TryGetValue(ColStatus, out var statusCol);
        var hasStatusCol = columnMapping.ContainsKey(ColStatus);

        if (hasReviewerCol)
            log?.Invoke($"✅ Reviewer column detected: '{reviewerHeaderName}' (col{reviewerCol}).");
        else
            log?.Invoke("⚠ Reviewer (Inspector) column NOT detected — reviewer field will be empty for all rows.");

        int dataRowCount = gridData.Count - (headerRowIndex + 1);
        log?.Invoke($"Scanning {dataRowCount} data rows (rows {headerRowIndex + 2}–{gridData.Count}).");

        var results = new List<IndexSheetReportLink>();
        int skippedNoHyperlinks = 0;

        for (int r = headerRowIndex + 1; r < gridData.Count; r++)
        {
            var rowData = gridData[r];
            if (rowData?.Values == null) continue;

            var projectText = projectCol < rowData.Values.Count
                ? rowData.Values[projectCol]?.FormattedValue?.Trim() ?? ""
                : "";
            if (string.IsNullOrWhiteSpace(projectText)) continue;

            var reportNum = hasReportNumCol && reportNumCol < rowData.Values.Count
                ? rowData.Values[reportNumCol]?.FormattedValue?.Trim() ?? ""
                : "";
                
            var reviewerText = hasReviewerCol && reviewerCol < rowData.Values.Count
                ? rowData.Values[reviewerCol]?.FormattedValue?.Trim() ?? ""
                : "";
                
            var statusText = hasStatusCol && statusCol < rowData.Values.Count
                ? rowData.Values[statusCol]?.FormattedValue?.Trim() ?? ""
                : "";

            // Extract hyperlink URLs from the link cell (only if a link column was detected)
            var linkCell = hasLinkCol && linkCol < rowData.Values.Count ? rowData.Values[linkCol] : null;
            var hyperlinks = ExtractHyperlinksFromCell(linkCell);

            if (hyperlinks.Count == 0)
            {
                if (includeRowsWithoutLinks && !string.IsNullOrWhiteSpace(projectText))
                {
                    // Row has project/status data but no report link — include for Phase 1 Preview
                    results.Add(new IndexSheetReportLink
                    {
                        RowIndex = r,
                        ProjectRef = projectText,
                        ReportNumber = reportNum,
                        ReportSpreadsheetIds = [],
                        Reviewer = reviewerText,
                        Status = statusText,
                    });
                }
                else
                {
                    skippedNoHyperlinks++;
                    // Log the first few skipped rows for diagnostics
                    if (skippedNoHyperlinks <= 3)
                    {
                        var cellText = linkCell?.FormattedValue ?? "(null)";
                        var hasRuns = linkCell?.TextFormatRuns?.Count ?? 0;
                        var hasHyperlink = linkCell?.Hyperlink ?? "(null)";
                        var hasEffectiveLink = linkCell?.EffectiveFormat?.TextFormat?.Link?.Uri ?? "(null)";
                        log?.Invoke($"  Row {r + 1} '{projectText}': no hyperlinks extracted. " +
                            $"Cell text='{cellText}', textFormatRuns={hasRuns}, hyperlink='{hasHyperlink}', effectiveLink='{hasEffectiveLink}'");
                    }
                }
                continue;
            }

            // Parse spreadsheet IDs from URLs
            var spreadsheetIds = hyperlinks
                .Select(ReportContentExtractor.ExtractSpreadsheetId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();

            if (spreadsheetIds.Count > 0)
            {
                results.Add(new IndexSheetReportLink
                {
                    RowIndex = r,
                    ProjectRef = projectText,
                    ReportNumber = reportNum,
                    ReportSpreadsheetIds = spreadsheetIds,
                    Reviewer = reviewerText,
                    Status = statusText,
                });
            }
            else if (includeRowsWithoutLinks && !string.IsNullOrWhiteSpace(projectText))
            {
                // Had hyperlinks but none parsed to valid spreadsheet IDs — include with empty IDs
                results.Add(new IndexSheetReportLink
                {
                    RowIndex = r,
                    ProjectRef = projectText,
                    ReportNumber = reportNum,
                    ReportSpreadsheetIds = [],
                    Reviewer = reviewerText,
                    Status = statusText,
                });
            }
        }

        if (skippedNoHyperlinks > 0)
            log?.Invoke($"Skipped {skippedNoHyperlinks} rows with no extractable hyperlinks in link column.");

        int withReviewer = results.Count(r => !string.IsNullOrWhiteSpace(r.Reviewer));
        log?.Invoke($"Result: {results.Count} rows returned ({withReviewer} have reviewer text). " +
            $"Projects: [{string.Join(", ", results.Select(r => r.ProjectRef).Distinct().Take(10))}]");

        return results;
    }

    /// <summary>
    /// Extracts all hyperlink URLs from a single cell.
    /// Checks (in priority order):
    /// <list type="number">
    ///   <item>Rich-text per-run links (<see cref="CellData.TextFormatRuns"/>)</item>
    ///   <item>Cell-level hyperlink (<see cref="CellData.Hyperlink"/>)</item>
    ///   <item>Effective format link (<c>EffectiveFormat.TextFormat.Link</c>)</item>
    ///   <item>Raw Google Sheets URLs in the cell's formatted text</item>
    /// </list>
    /// </summary>
    private static List<string> ExtractHyperlinksFromCell(CellData? cell)
    {
        if (cell == null) return [];

        var links = new List<string>();

        // 1. Try per-run hyperlinks (rich text: "1, 2, 3, 4" where each number links somewhere different)
        if (cell.TextFormatRuns is { Count: > 0 })
        {
            foreach (var run in cell.TextFormatRuns)
            {
                var uri = run?.Format?.Link?.Uri;
                if (!string.IsNullOrWhiteSpace(uri))
                    links.Add(uri);
            }
        }

        // 2. Cell-level hyperlink (entire cell is one link)
        if (links.Count == 0 && !string.IsNullOrWhiteSpace(cell.Hyperlink))
            links.Add(cell.Hyperlink);

        // 3. Effective format link (covers HYPERLINK() formula cells)
        if (links.Count == 0)
        {
            var effectiveUri = cell.EffectiveFormat?.TextFormat?.Link?.Uri;
            if (!string.IsNullOrWhiteSpace(effectiveUri))
                links.Add(effectiveUri);
        }

        // 4. Parse raw Google Sheets URLs from the cell's display text
        if (links.Count == 0 && !string.IsNullOrWhiteSpace(cell.FormattedValue))
        {
            var urlMatches = System.Text.RegularExpressions.Regex.Matches(
                cell.FormattedValue,
                @"https?://docs\.google\.com/spreadsheets/d/[a-zA-Z0-9_-]+");
            foreach (System.Text.RegularExpressions.Match m in urlMatches)
                links.Add(m.Value);
        }

        // 5. Last resort: check if HYPERLINK formula is in userEnteredValue
        if (links.Count == 0)
        {
            var formula = cell.UserEnteredValue?.FormulaValue;
            if (!string.IsNullOrWhiteSpace(formula))
            {
                var formulaMatch = System.Text.RegularExpressions.Regex.Match(
                    formula, @"HYPERLINK\s*\(\s*""([^""]+)""");
                if (formulaMatch.Success)
                    links.Add(formulaMatch.Groups[1].Value);
            }
        }

        return links;
    }
}
