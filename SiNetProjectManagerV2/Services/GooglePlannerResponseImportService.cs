using Google.Apis.Sheets.v4;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiNetSQL.Data;
using SiNetSQL.Services.InspectionSync;
using SiOffice.GoogleConnector.Reports;

namespace SiNetProjectManagerV2.Services;

/// <summary>
/// Minimal Google Sheets implementation of <see cref="IPlannerResponseImportService"/>.
/// Reads the previously-sent inspection spreadsheet and tries to detect planner answers
/// for each note by:
/// <list type="bullet">
///   <item>finding a row that contains the note's <c>NoteSubIndex</c> or <c>SectionCode</c>,</item>
///   <item>locating the original note text column in that row,</item>
///   <item>treating subsequent non-empty cells in the row as the planner response.</item>
/// </list>
/// Persistence is intentionally NOT performed here — the view-model decides what to save.
/// </summary>
public sealed class GooglePlannerResponseImportService : IPlannerResponseImportService
{
    private readonly GoogleAuthService _authService;
    private readonly IDbContextFactory<SiNetSQLDbContext> _contextFactory;
    private readonly ILogger<GooglePlannerResponseImportService>? _logger;

    public GooglePlannerResponseImportService(
        GoogleAuthService authService,
        IDbContextFactory<SiNetSQLDbContext> contextFactory,
        ILogger<GooglePlannerResponseImportService>? logger = null)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _logger = logger;
    }

    public async Task<PlannerResponseImportResult> ScanForResponsesAsync(
        int reportId,
        string sentSpreadsheetId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sentSpreadsheetId);

        var warnings = new List<string>();
        var matches = new List<PlannerResponseMatch>();

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var notes = await context.InspectionNotes
                .Include(n => n.Section)
                .Where(n => n.ReportId == reportId)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (notes.Count == 0)
            {
                return new PlannerResponseImportResult
                {
                    IsSuccess = true,
                    Warnings = { "No notes found for this report." }
                };
            }

            var sheetsService = _authService.SheetsService
                ?? throw new InvalidOperationException("Google Sheets service is not available. Ensure the user is authenticated.");

            // Get all sheets and read all values
            var spreadsheet = await sheetsService.Spreadsheets
                .Get(sentSpreadsheetId)
                .ExecuteAsync(cancellationToken);

            foreach (var sheet in spreadsheet.Sheets)
            {
                var sheetTitle = sheet.Properties.Title;
                var range = $"'{sheetTitle}'";

                Google.Apis.Sheets.v4.Data.ValueRange values;
                try
                {
                    values = await sheetsService.Spreadsheets.Values
                        .Get(sentSpreadsheetId, range)
                        .ExecuteAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Failed to read sheet '{sheetTitle}': {ex.Message}");
                    continue;
                }

                if (values.Values == null) continue;

                for (int rowIdx = 0; rowIdx < values.Values.Count; rowIdx++)
                {
                    var row = values.Values[rowIdx];
                    if (row == null || row.Count == 0) continue;

                    var rowText = string.Join(" | ", row.Select(c => c?.ToString() ?? string.Empty));

                    foreach (var note in notes)
                    {
                        var subIndex = note.NoteSubIndex;
                        if (string.IsNullOrWhiteSpace(subIndex)) continue;

                        // Match by exact SubIndex appearance in any cell of the row
                        bool subIndexHit = row.Any(c =>
                            string.Equals(c?.ToString()?.Trim(), subIndex, StringComparison.OrdinalIgnoreCase));

                        if (!subIndexHit) continue;

                        // Find the column carrying the original note text (best effort)
                        var originalText = note.NoteText ?? string.Empty;
                        int textCol = -1;
                        for (int c = 0; c < row.Count; c++)
                        {
                            var cellText = row[c]?.ToString() ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(originalText)
                                && cellText.Contains(originalText, StringComparison.OrdinalIgnoreCase))
                            {
                                textCol = c;
                                break;
                            }
                        }

                        // The response is the first non-empty cell AFTER the note text column
                        // (or the last non-empty cell on the row if the text column is unknown).
                        string? responseText = null;
                        (int Row, int Col)? sourceCell = null;

                        if (textCol >= 0)
                        {
                            for (int c = textCol + 1; c < row.Count; c++)
                            {
                                var v = row[c]?.ToString();
                                if (!string.IsNullOrWhiteSpace(v))
                                {
                                    responseText = v;
                                    sourceCell = (rowIdx, c);
                                    break;
                                }
                            }
                        }
                        else
                        {
                            for (int c = row.Count - 1; c >= 0; c--)
                            {
                                var v = row[c]?.ToString();
                                if (string.IsNullOrWhiteSpace(v)) continue;
                                if (string.Equals(v.Trim(), subIndex, StringComparison.OrdinalIgnoreCase)) continue;
                                responseText = v;
                                sourceCell = (rowIdx, c);
                                break;
                            }
                        }

                        if (string.IsNullOrWhiteSpace(responseText)) continue;

                        // Skip if the "response" equals the original note text
                        if (!string.IsNullOrWhiteSpace(originalText) &&
                            string.Equals(responseText.Trim(), originalText.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var existingForNote = matches.FindAll(m => m.NoteId == note.NoteId);
                        var confidence = textCol >= 0 ? "Exact" : "Probable";

                        if (existingForNote.Count > 0)
                        {
                            // Multiple hits → mark all as ambiguous so the user reviews
                            confidence = "Ambiguous";
                            for (int i = 0; i < matches.Count; i++)
                            {
                                if (matches[i].NoteId == note.NoteId)
                                {
                                    matches[i] = new PlannerResponseMatch
                                    {
                                        NoteId = matches[i].NoteId,
                                        NoteSubIndex = matches[i].NoteSubIndex,
                                        SectionCode = matches[i].SectionCode,
                                        OriginalNoteText = matches[i].OriginalNoteText,
                                        ResponseText = matches[i].ResponseText,
                                        Confidence = "Ambiguous",
                                        SourceCell = matches[i].SourceCell
                                    };
                                }
                            }
                        }

                        matches.Add(new PlannerResponseMatch
                        {
                            NoteId = note.NoteId,
                            NoteSubIndex = subIndex,
                            SectionCode = note.Section?.FullCode,
                            OriginalNoteText = originalText,
                            ResponseText = responseText.Trim(),
                            Confidence = confidence,
                            SourceCell = sourceCell
                        });
                    }
                }
            }

            return new PlannerResponseImportResult
            {
                IsSuccess = true,
                Matches = matches,
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Planner response import failed for spreadsheet {Id}.", sentSpreadsheetId);
            return new PlannerResponseImportResult
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Warnings = warnings
            };
        }
    }

    /// <inheritdoc />
    public async Task<PlannerResponseImportResult> ImportFromSnapshotMapAsync(
        int reportId,
        string sentSpreadsheetId,
        IReadOnlyList<ExportedNoteCellMap> noteCellMap,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sentSpreadsheetId);

        if (noteCellMap == null || noteCellMap.Count == 0)
        {
            // Fallback: caller didn't have a snapshot mapping, use heuristic.
            return await ScanForResponsesAsync(reportId, sentSpreadsheetId, cancellationToken);
        }

        var warnings = new List<string>();
        var matches = new List<PlannerResponseMatch>();

        try
        {
            var sheetsService = _authService.SheetsService
                ?? throw new InvalidOperationException("Google Sheets service is not available. Ensure the user is authenticated.");

            _logger?.LogInformation(
                "[InspectionResponseImport] Starting snapshot-based import. SpreadsheetId={Id}, ReportId={ReportId}, MapCount={Count}",
                sentSpreadsheetId, reportId, noteCellMap.Count);

            // Group by sheet to minimize API calls.
            foreach (var bySheet in noteCellMap.GroupBy(m => m.SheetName ?? string.Empty))
            {
                var sheetTitle = bySheet.Key;
                if (string.IsNullOrWhiteSpace(sheetTitle))
                {
                    warnings.Add("Snapshot mapping entry has no SheetName; skipping.");
                    continue;
                }

                var rangeForLog = $"'{sheetTitle}'";
                _logger?.LogInformation(
                    "[InspectionResponseImport] Reading sheet range. SpreadsheetId={Id}, Sheet={Sheet}, Range={Range}",
                    sentSpreadsheetId, sheetTitle, rangeForLog);

                Google.Apis.Sheets.v4.Data.ValueRange values;
                try
                {
                    values = await sheetsService.Spreadsheets.Values
                        .Get(sentSpreadsheetId, rangeForLog)
                        .ExecuteAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Failed to read sheet '{sheetTitle}': {ex.Message}");
                    _logger?.LogError(ex,
                        "[InspectionResponseImport] Failed to read sheet '{Sheet}'.",
                        sheetTitle);
                    continue;
                }

                var rows = values.Values;
                _logger?.LogInformation(
                    "[InspectionResponseImport] Sheet read. Sheet={Sheet}, RowCount={RowCount}",
                    sheetTitle, rows?.Count ?? 0);

                if (rows == null) continue;

                foreach (var entry in bySheet)
                {
                    // Indices stored in the snapshot map are 0-based grid indices.
                    // A1 notation is 1-based, so the displayed cell address is row+1 / col->letter(col).
                    if (entry.ExportedRowIndex < 0 || entry.ExportedRowIndex >= rows.Count)
                    {
                        var noteA1OOR = ToA1Address(entry.ExportedRowIndex, entry.ExportedNoteColumnIndex);
                        var responseA1OOR = ToA1Address(entry.ExportedRowIndex, entry.PlannerResponseColumnIndex);
                        _logger?.LogWarning(
                            "[InspectionResponseImport] Row index out of range. NoteId={NoteId}, SectionCode={Section}, SubIndex={Sub}, " +
                            "Sheet={Sheet}, ExportedRowIndex={Row} (0-based), NoteCell={NoteCell}, ResponseCell={ResponseCell}, RowsAvailable={RowCount}",
                            entry.NoteId, entry.SectionCode, entry.NoteSubIndex,
                            sheetTitle, entry.ExportedRowIndex, noteA1OOR, responseA1OOR, rows.Count);

                        matches.Add(new PlannerResponseMatch
                        {
                            NoteId = entry.NoteId,
                            NoteSubIndex = entry.NoteSubIndex,
                            SectionCode = entry.SectionCode,
                            Confidence = "NotFound",
                            SourceCell = (entry.ExportedRowIndex, entry.PlannerResponseColumnIndex)
                        });
                        continue;
                    }

                    var row = rows[entry.ExportedRowIndex];
                    string? rawResponse = null;
                    if (row != null && entry.PlannerResponseColumnIndex >= 0
                        && entry.PlannerResponseColumnIndex < row.Count)
                    {
                        rawResponse = row[entry.PlannerResponseColumnIndex]?.ToString();
                    }
                    var responseText = NormalizePlannerResponse(rawResponse);
                    if (responseText.Length == 0) responseText = null;

                    bool placeholderRejected = false;
                    // Reject template header/label values like "תגובת המתכנן".
                    if (!string.IsNullOrEmpty(responseText) && IsPlaceholderPlannerResponse(responseText))
                    {
                        _logger?.LogInformation(
                            "[InspectionResponseImport] Ignored placeholder response. NoteId={NoteId}, Cell={Cell}, Value={Value}",
                            entry.NoteId,
                            ToA1Address(entry.ExportedRowIndex, entry.PlannerResponseColumnIndex),
                            responseText);
                        responseText = null;
                        placeholderRejected = true;
                    }

                    string? rawNote = null;
                    if (row != null && entry.ExportedNoteColumnIndex >= 0
                        && entry.ExportedNoteColumnIndex < row.Count)
                    {
                        rawNote = row[entry.ExportedNoteColumnIndex]?.ToString();
                    }
                    var originalText = rawNote?.Trim();

                    // Read the logical NoteSubIndex that was injected at export time
                    // into the column immediately to the left of the note column.
                    // This proves which logical note this physical row belongs to.
                    string? rowNoteSubIndexFromSheet = null;
                    int subIndexCol = entry.ExportedNoteColumnIndex - 1;
                    if (row != null && subIndexCol >= 0 && subIndexCol < row.Count)
                    {
                        rowNoteSubIndexFromSheet = row[subIndexCol]?.ToString()?.Trim();
                    }

                    string matchBy;
                    string confidence;
                    bool logicalMismatch = false;
                    if (!string.IsNullOrWhiteSpace(rowNoteSubIndexFromSheet)
                        && !string.IsNullOrWhiteSpace(entry.NoteSubIndex))
                    {
                        if (string.Equals(rowNoteSubIndexFromSheet, entry.NoteSubIndex, StringComparison.OrdinalIgnoreCase))
                        {
                            matchBy = "NoteSubIndex";
                            confidence = "High";
                        }
                        else
                        {
                            // Row identity does not match the snapshot mapping — refuse to attach
                            // to the wrong note. This prevents a section-level/header response
                            // from leaking into a specific note like 7.1.5.
                            matchBy = "Mismatch";
                            confidence = "Low";
                            logicalMismatch = true;
                        }
                    }
                    else
                    {
                        matchBy = "RowFallback";
                        confidence = "Low";
                    }

                    // Backward-compat fallback: older snapshots saved
                    // PlannerResponseColumnIndex = NoteCol + 1, but the actual planner
                    // response cell is NoteCol + 2. If the primary cell is empty, try
                    // (NoteCol + 2) and log when the fallback is used.
                    int effectiveResponseCol = entry.PlannerResponseColumnIndex;
                    bool fallbackUsed = false;
                    if (string.IsNullOrEmpty(responseText) && row != null)
                    {
                        int fallbackCol = entry.ExportedNoteColumnIndex + 2;
                        if (fallbackCol != entry.PlannerResponseColumnIndex
                            && fallbackCol >= 0 && fallbackCol < row.Count)
                        {
                            var fallbackRaw = row[fallbackCol]?.ToString();
                            var fallbackText = NormalizePlannerResponse(fallbackRaw);
                            if (fallbackText.Length > 0 && IsPlaceholderPlannerResponse(fallbackText))
                            {
                                _logger?.LogInformation(
                                    "[InspectionResponseImport] Ignored placeholder response. NoteId={NoteId}, Cell={Cell}, Value={Value}",
                                    entry.NoteId,
                                    ToA1Address(entry.ExportedRowIndex, fallbackCol),
                                    fallbackText);
                                fallbackText = string.Empty;
                            }
                            if (fallbackText.Length > 0)
                            {
                                _logger?.LogWarning(
                                    "[InspectionResponseImport] Used fallback response column. NoteId={NoteId}, PrimaryCell={Primary}, FallbackCell={Fallback}, FallbackLen={Len}",
                                    entry.NoteId,
                                    ToA1Address(entry.ExportedRowIndex, entry.PlannerResponseColumnIndex),
                                    ToA1Address(entry.ExportedRowIndex, fallbackCol),
                                    fallbackRaw?.Length ?? 0);
                                rawResponse = fallbackRaw;
                                responseText = fallbackText;
                                effectiveResponseCol = fallbackCol;
                                fallbackUsed = true;
                            }
                        }
                    }

                    bool willImport = !string.IsNullOrEmpty(responseText);
                    string skipReason = string.Empty;

                    // Reject any response when the row's logical NoteSubIndex does not match
                    // the mapped note. This prevents wrong-note assignment when the sheet
                    // layout drifts from the snapshot (e.g. inserted/removed rows).
                    if (willImport && logicalMismatch)
                    {
                        _logger?.LogWarning(
                            "[InspectionResponseImport] Logical mismatch — refusing to import. " +
                            "NoteId={NoteId}, MappedSubIndex={Mapped}, RowSubIndexFromSheet={Row}, ResponseCell={Cell}",
                            entry.NoteId, entry.NoteSubIndex, rowNoteSubIndexFromSheet,
                            ToA1Address(entry.ExportedRowIndex, effectiveResponseCol));
                        responseText = null;
                        willImport = false;
                        skipReason = "LogicalMismatch";
                    }

                    // Skip when planner response equals our exported note (no real answer yet).
                    if (willImport
                        && !string.IsNullOrWhiteSpace(originalText)
                        && string.Equals(responseText, originalText, StringComparison.OrdinalIgnoreCase))
                    {
                        responseText = null;
                        willImport = false;
                        skipReason = "ResponseEqualsOriginalNote";
                    }
                    else if (!willImport)
                    {
                        if (placeholderRejected)
                        {
                            skipReason = "PlaceholderResponse";
                        }
                        // Distinguish "cell had only invisible/whitespace chars" from "cell was truly empty".
                        else if (!string.IsNullOrEmpty(rawResponse))
                        {
                            skipReason = "EmptyAfterNormalization";
                            _logger?.LogInformation(
                                "[InspectionResponseImport] Response ignored as empty after normalization. NoteId={NoteId}, Cell={Cell}, RawLen={RawLen}, NormalizedLen=0",
                                entry.NoteId,
                                ToA1Address(entry.ExportedRowIndex, effectiveResponseCol),
                                rawResponse.Length);
                        }
                        else
                        {
                            skipReason = "EmptyResponseCell";
                        }
                    }

                    var notePreview = Preview(rawNote);
                    var responsePreview = Preview(rawResponse);

                    _logger?.LogInformation(
                        "[InspectionResponseImport] Read mapped response. NoteId={NoteId}, SectionCode={Section}, SubIndex={Sub}, " +
                        "Sheet={Sheet}, ExportedRowIndex={Row} (0-based, A1 row={A1Row}), " +
                        "ExportedNoteColumnIndex={NoteCol}, PlannerResponseColumnIndex={RespCol}, EffectiveRespCol={EffCol}, FallbackUsed={Fallback}, " +
                        "NoteCell={NoteCell}, ResponseCell={ResponseCell}, " +
                        "NoteValuePreview={NotePreview}, ResponseValuePreview={ResponsePreview}, " +
                        "ResponseLen={ResponseLen}, WillImport={WillImport}, SkipReason={SkipReason}",
                        entry.NoteId, entry.SectionCode, entry.NoteSubIndex,
                        sheetTitle, entry.ExportedRowIndex, entry.ExportedRowIndex + 1,
                        entry.ExportedNoteColumnIndex, entry.PlannerResponseColumnIndex, effectiveResponseCol, fallbackUsed,
                        ToA1Address(entry.ExportedRowIndex, entry.ExportedNoteColumnIndex),
                        ToA1Address(entry.ExportedRowIndex, effectiveResponseCol),
                        notePreview, responsePreview,
                        rawResponse?.Length ?? 0, willImport, skipReason);

                    _logger?.LogInformation(
                        "[InspectionResponseImport] Logical match. ResponseCell={Cell}, MappedNoteId={NoteId}, " +
                        "MappedSectionCode={Section}, MappedNoteSubIndex={Mapped}, RowNoteSubIndexFromSheet={RowSub}, " +
                        "MatchBy={MatchBy}, Confidence={Confidence}",
                        ToA1Address(entry.ExportedRowIndex, effectiveResponseCol),
                        entry.NoteId, entry.SectionCode, entry.NoteSubIndex,
                        rowNoteSubIndexFromSheet ?? "<empty>",
                        matchBy, confidence);

                    matches.Add(new PlannerResponseMatch
                    {
                        NoteId = entry.NoteId,
                        NoteSubIndex = entry.NoteSubIndex,
                        SectionCode = entry.SectionCode,
                        OriginalNoteText = originalText,
                        ResponseText = responseText,
                        Confidence = string.IsNullOrWhiteSpace(responseText)
                            ? "NotFound"
                            : confidence,
                        SourceCell = (entry.ExportedRowIndex, effectiveResponseCol)
                    });
                }
            }

            _logger?.LogInformation(
                "[InspectionResponseImport] Snapshot-based import complete. ReportId={ReportId}, MatchesWithText={WithText}, MatchesNotFound={NotFound}, Warnings={Warnings}",
                reportId,
                matches.Count(m => !string.IsNullOrWhiteSpace(m.ResponseText)),
                matches.Count(m => string.IsNullOrWhiteSpace(m.ResponseText)),
                warnings.Count);

            return new PlannerResponseImportResult
            {
                IsSuccess = true,
                Matches = matches,
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Snapshot-based planner response import failed for spreadsheet {Id}.", sentSpreadsheetId);
            return new PlannerResponseImportResult
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Warnings = warnings
            };
        }
    }

    /// <summary>
    /// Convert a 0-based grid (row, col) to A1 notation (e.g. (60, 0) → "A61").
    /// </summary>
    private static string ToA1Address(int rowIndex, int colIndex)
    {
        if (rowIndex < 0 || colIndex < 0)
            return $"INVALID(R{rowIndex},C{colIndex})";

        // Convert column index to letters (0 → A, 25 → Z, 26 → AA, ...).
        int c = colIndex;
        var letters = string.Empty;
        do
        {
            int rem = c % 26;
            letters = (char)('A' + rem) + letters;
            c = (c / 26) - 1;
        } while (c >= 0);

        return $"{letters}{rowIndex + 1}";
    }

    private static string Preview(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "<empty>";
        var trimmed = text.Replace("\r", " ").Replace("\n", " ");
        const int max = 60;
        return trimmed.Length > max ? trimmed[..max] + "…" : trimmed;
    }

    /// <summary>
    /// Normalizes a planner response cell value: strips invisible/zero-width characters,
    /// converts non-breaking spaces, and trims. Returns <see cref="string.Empty"/> when the
    /// value contains only whitespace or invisible characters.
    /// </summary>
    public static string NormalizePlannerResponse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value
            .Replace('\u00A0', ' ')   // non-breaking space
            .Replace('\u200B', '\0')  // zero-width space
            .Replace('\u200C', '\0')
            .Replace('\u200D', '\0')
            .Replace('\uFEFF', '\0')
            .Replace("\0", string.Empty)
            .Trim();

        return string.IsNullOrWhiteSpace(normalized)
            ? string.Empty
            : normalized;
    }

    /// <summary>
    /// Header/label values that may appear in the planner-response column but are not real responses.
    /// These come from template header cells (e.g. "תגובת המתכנן") that get picked up by the fallback
    /// column probe. They must never be imported or shown as a real planner answer.
    /// </summary>
    private static readonly HashSet<string> PlaceholderResponseValues = new(StringComparer.Ordinal)
    {
        "תגובת המתכנן",
        "תגובת מתכנן",
        "התייחסות המתכנן",
        "התייחסות מתכנן",
        "מענה המתכנן",
        "מענה מתכנן",
        "הערות המתכנן",
        "הערת מתכנן",
        "תגובה",
    };

    /// <summary>
    /// True when the normalized value exactly matches one of the well-known header/label strings
    /// used in the inspection sheet template (e.g. "תגובת המתכנן").
    /// </summary>
    public static bool IsPlaceholderPlannerResponse(string? normalizedValue)
    {
        if (string.IsNullOrWhiteSpace(normalizedValue)) return false;
        return PlaceholderResponseValues.Contains(normalizedValue.Trim());
    }

    /// <summary>
    /// Regex for a "real" mappable note row identifier: exactly three numeric segments
    /// separated by dots, e.g. "7.1.1". Section headers like "7.1" are NOT mappable.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex MappableNoteSubIndexRegex =
        new(@"^\d+\.\d+\.\d+$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// True when the trimmed value is a full <c>X.Y.Z</c> note identifier.
    /// </summary>
    public static bool IsMappableNoteRowKey(string? columnAValue)
    {
        if (string.IsNullOrWhiteSpace(columnAValue)) return false;
        return MappableNoteSubIndexRegex.IsMatch(columnAValue.Trim());
    }

    /// <inheritdoc />
    public async Task<PlannerResponseImportResult> PullResponsesByColumnAAsync(
        int reportId,
        string sentSpreadsheetId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sentSpreadsheetId);

        var warnings = new List<string>();
        var matches = new List<PlannerResponseMatch>();

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var notes = await context.InspectionNotes
                .Where(n => n.ReportId == reportId && n.NoteSubIndex != null)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            // Map NoteSubIndex -> NoteId (string compare; values like "7.1.1").
            var subIndexToNote = notes
                .Where(n => !string.IsNullOrWhiteSpace(n.NoteSubIndex))
                .GroupBy(n => n.NoteSubIndex!.Trim(), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            var sheetsService = _authService.SheetsService
                ?? throw new InvalidOperationException("Google Sheets service is not available. Ensure the user is authenticated.");

            _logger?.LogInformation(
                "[InspectionResponseImport] Pull-by-ColumnA START. SpreadsheetId={Id}, ReportId={ReportId}, NotesWithSubIndex={NoteCount}",
                sentSpreadsheetId, reportId, subIndexToNote.Count);

            var spreadsheet = await sheetsService.Spreadsheets
                .Get(sentSpreadsheetId)
                .ExecuteAsync(cancellationToken);

            int totalRowsScanned = 0;
            int mappableRows = 0;

            foreach (var sheet in spreadsheet.Sheets)
            {
                var sheetTitle = sheet.Properties.Title;
                // Read columns A:D for the entire sheet — this is enough for column-A id and column-D response.
                var range = $"'{sheetTitle}'!A:D";

                Google.Apis.Sheets.v4.Data.ValueRange values;
                try
                {
                    values = await sheetsService.Spreadsheets.Values
                        .Get(sentSpreadsheetId, range)
                        .ExecuteAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Failed to read sheet '{sheetTitle}': {ex.Message}");
                    _logger?.LogError(ex,
                        "[InspectionResponseImport] Failed to read sheet '{Sheet}'.", sheetTitle);
                    continue;
                }

                var rows = values.Values;
                if (rows == null) continue;

                _logger?.LogInformation(
                    "[InspectionResponseImport] Sheet read. Sheet={Sheet}, RowCount={Count}",
                    sheetTitle, rows.Count);

                for (int r = 0; r < rows.Count; r++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    totalRowsScanned++;

                    var row = rows[r];
                    if (row == null || row.Count == 0) continue;

                    string columnA = (row.Count > 0 ? row[0]?.ToString() : null)?.Trim() ?? string.Empty;
                    bool isMappable = IsMappableNoteRowKey(columnA);
                    if (!isMappable)
                    {
                        // Skip silently for performance; only log a sample to avoid log spam.
                        continue;
                    }
                    mappableRows++;

                    string? rawResponse = row.Count > 3 ? row[3]?.ToString() : null;
                    var responseText = NormalizePlannerResponse(rawResponse);
                    var responseCellAddr = ToA1Address(r, 3);
                    var responsePreview = Preview(rawResponse);

                    string skipReason = string.Empty;
                    bool willImport = false;
                    long? matchedNoteId = null;
                    string? matchedSection = null;

                    if (responseText.Length == 0)
                    {
                        skipReason = string.IsNullOrEmpty(rawResponse)
                            ? "EmptyResponseCell"
                            : "EmptyResponseCell";
                    }
                    else if (IsPlaceholderPlannerResponse(responseText))
                    {
                        skipReason = "PlaceholderResponse";
                        responseText = string.Empty;
                    }
                    else if (!subIndexToNote.TryGetValue(columnA, out var matchedNote))
                    {
                        skipReason = "NoMatchingNoteSubIndex";
                    }
                    else
                    {
                        willImport = true;
                        matchedNoteId = matchedNote.NoteId;
                        matchedSection = columnA[..columnA.LastIndexOf('.')];
                    }

                    _logger?.LogInformation(
                        "[InspectionResponseImport] Row scan. Row={Row}, ColumnAValue={ColA}, IsMappableNoteRow={Mappable}, " +
                        "MatchedNoteSubIndex={Matched}, ResponseCell={Cell}, ResponsePreview={Preview}, " +
                        "NormalizedResponseLength={Len}, WillImport={WillImport}, SkipReason={SkipReason}",
                        r + 1, columnA, isMappable,
                        willImport ? columnA : "<none>",
                        responseCellAddr, responsePreview,
                        responseText.Length, willImport, skipReason);

                    if (!willImport) continue;

                    matches.Add(new PlannerResponseMatch
                    {
                        NoteId = matchedNoteId,
                        NoteSubIndex = columnA,
                        SectionCode = matchedSection,
                        ResponseText = responseText,
                        Confidence = "Exact",
                        SourceCell = (r, 3),
                        SourceSheetName = sheetTitle,
                        SourceRowNumber = r + 1,
                        SourceCellAddress = responseCellAddr
                    });
                }
            }

            _logger?.LogInformation(
                "[InspectionResponseImport] Pull-by-ColumnA END. ReportId={ReportId}, RowsScanned={Scanned}, MappableRows={Mappable}, MatchesWithText={WithText}, Warnings={Warnings}",
                reportId, totalRowsScanned, mappableRows, matches.Count, warnings.Count);

            return new PlannerResponseImportResult
            {
                IsSuccess = true,
                Matches = matches,
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Pull-by-ColumnA planner response import failed for spreadsheet {Id}.", sentSpreadsheetId);
            return new PlannerResponseImportResult
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Warnings = warnings
            };
        }
    }
}
