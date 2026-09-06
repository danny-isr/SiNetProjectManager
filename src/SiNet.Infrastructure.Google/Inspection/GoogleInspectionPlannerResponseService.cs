using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiNet.Application.Abstractions.Inspection;
using SiNetSQL.Data;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Google.Inspection;

/// <summary>
/// New-System adapter over the legacy Column-A planner-response pull + the same DB persistence
/// fields written by <c>InspectionReportService.SavePulledPlannerResponsesAsync</c>.
/// Does not invent a second import pipeline.
/// </summary>
internal sealed class GoogleInspectionPlannerResponseService(
    IGoogleDriveSheetsSession googleSession,
    IDbContextFactory<SiNetSQLDbContext> dbContextFactory,
    ILogger<GoogleInspectionPlannerResponseService>? logger = null)
    : IInspectionPlannerResponseService
{
    private static readonly Regex MappableNoteSubIndexRegex =
        new(@"^\d+\.\d+\.\d+$", RegexOptions.Compiled);

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

    private readonly IGoogleDriveSheetsSession _googleSession =
        googleSession ?? throw new ArgumentNullException(nameof(googleSession));
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbContextFactory =
        dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    private readonly ILogger<GoogleInspectionPlannerResponseService>? _logger = logger;

    public async Task<InspectionPlannerResponsePullResult> PullAndPersistAsync(
        int reportId,
        bool isRepull,
        CancellationToken cancellationToken = default)
    {
        if (reportId <= 0)
            return InspectionPlannerResponsePullResult.Fail("דוח לא תקף.");

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var report = await db.InspectionReports
            .AsNoTracking()
            .Where(r => r.ReportId == reportId)
            .Select(r => new { r.ReportId, r.SeriesId, r.SentSpreadsheetId, r.SentSpreadsheetUrl })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (report is null)
            return InspectionPlannerResponsePullResult.Fail($"דוח {reportId} לא נמצא.");

        var spreadsheetId = report.SentSpreadsheetId?.Trim();
        if (string.IsNullOrWhiteSpace(spreadsheetId))
            return InspectionPlannerResponsePullResult.Fail("לדוח אין מזהה גיליון שנשלח/יוצא.");

        await _googleSession.EnsureAuthenticatedAsync(cancellationToken).ConfigureAwait(false);
        var sheets = _googleSession.SheetsService
            ?? throw new InvalidOperationException("Google Sheets service is not available.");

        var notes = await db.InspectionNotes
            .AsNoTracking()
            .Where(n => n.ReportId == reportId && n.NoteSubIndex != null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var subIndexToNote = notes
            .Where(n => !string.IsNullOrWhiteSpace(n.NoteSubIndex))
            .GroupBy(n => n.NoteSubIndex!.Trim(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matches = new List<(long NoteId, string ResponseText, string? Sheet, int? Row, string? Cell)>();

        try
        {
            var spreadsheet = await sheets.Spreadsheets
                .Get(spreadsheetId)
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var sheet in spreadsheet.Sheets)
            {
                var sheetTitle = sheet.Properties.Title;
                var range = $"'{sheetTitle}'!A:D";
                global::Google.Apis.Sheets.v4.Data.ValueRange values;
                try
                {
                    values = await sheets.Spreadsheets.Values
                        .Get(spreadsheetId, range)
                        .ExecuteAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Planner pull failed reading sheet {Sheet}.", sheetTitle);
                    continue;
                }

                var rows = values.Values;
                if (rows is null)
                    continue;

                for (var r = 0; r < rows.Count; r++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var row = rows[r];
                    if (row is null || row.Count == 0)
                        continue;

                    var columnA = (row.Count > 0 ? row[0]?.ToString() : null)?.Trim() ?? string.Empty;
                    if (!IsMappableNoteRowKey(columnA))
                        continue;

                    var rawResponse = row.Count > 3 ? row[3]?.ToString() : null;
                    var responseText = NormalizePlannerResponse(rawResponse);
                    if (responseText.Length == 0 || IsPlaceholderPlannerResponse(responseText))
                        continue;
                    if (!subIndexToNote.TryGetValue(columnA, out var matchedNote))
                        continue;

                    matches.Add((matchedNote.NoteId, responseText, sheetTitle, r + 1, ToA1Address(r, 3)));
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Planner pull failed for report {ReportId} (repull={IsRepull}).", reportId, isRepull);
            return InspectionPlannerResponsePullResult.Fail(ex.Message);
        }

        try
        {
            var saved = await PersistMatchesAsync(
                    reportId,
                    report.SeriesId,
                    spreadsheetId,
                    report.SentSpreadsheetUrl,
                    matches,
                    cancellationToken)
                .ConfigureAwait(false);

            _logger?.LogInformation(
                "Planner pull persisted. ReportId={ReportId} IsRepull={IsRepull} Matched={Matched} Saved={Saved}",
                reportId,
                isRepull,
                matches.Count,
                saved);

            return InspectionPlannerResponsePullResult.Ok(matches.Count, saved);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Planner pull save failed for report {ReportId}.", reportId);
            return InspectionPlannerResponsePullResult.Fail($"Error saving: {ex.Message}");
        }
    }

    /// <summary>Same persistence fields as legacy SavePulledPlannerResponsesAsync.</summary>
    private async Task<int> PersistMatchesAsync(
        int reportId,
        int? seriesId,
        string sentSpreadsheetId,
        string? sentSpreadsheetUrl,
        IReadOnlyList<(long NoteId, string ResponseText, string? Sheet, int? Row, string? Cell)> matches,
        CancellationToken cancellationToken)
    {
        await using var ctx = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var cleanedPlaceholders = 0;
        var saved = 0;

        if (seriesId.HasValue)
        {
            var seriesReportIds = await ctx.InspectionReports
                .Where(r => r.SeriesId == seriesId.Value)
                .Select(r => r.ReportId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var dirtyNotes = await ctx.InspectionNotes
                .Where(n => seriesReportIds.Contains(n.ReportId) && n.PlannerResponseText != null)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var dirty in dirtyNotes)
            {
                var normalizedExisting = NormalizePlannerResponse(dirty.PlannerResponseText);
                if (!IsPlaceholderPlannerResponse(normalizedExisting))
                    continue;

                dirty.PlannerResponseText = null;
                dirty.PlannerResponseImportedAt = null;
                dirty.PlannerResponseSourceType = null;
                dirty.PlannerResponseSourceUrl = null;
                if (string.Equals(dirty.ResponseReviewStatus, "Pending", StringComparison.OrdinalIgnoreCase))
                    dirty.ResponseReviewStatus = null;
                cleanedPlaceholders++;
            }
        }

        foreach (var m in matches)
        {
            var note = await ctx.InspectionNotes
                .FirstOrDefaultAsync(n => n.NoteId == m.NoteId, cancellationToken)
                .ConfigureAwait(false);
            if (note is null)
                continue;

            var normalized = NormalizePlannerResponse(m.ResponseText);
            if (string.IsNullOrEmpty(normalized) || IsPlaceholderPlannerResponse(normalized))
                continue;

            note.PlannerResponseText = normalized;
            note.PlannerResponseImportedAt = now;
            note.PlannerResponseSourceType ??= "InSameReport";
            note.PlannerResponseSourceUrl = sentSpreadsheetUrl ?? note.PlannerResponseSourceUrl;
            note.ResponseReviewStatus = "Pending";
            note.PlannerResponsePulledAt = now;
            note.PlannerResponseSourceSpreadsheetId = sentSpreadsheetId;
            note.PlannerResponseSourceSpreadsheetUrl = sentSpreadsheetUrl;
            note.PlannerResponseSourceSheetName = m.Sheet;
            note.PlannerResponseSourceRow = m.Row;
            note.PlannerResponseSourceCell = m.Cell;
            saved++;
        }

        if (saved > 0 || cleanedPlaceholders > 0)
            await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return saved;
    }

    private static bool IsMappableNoteRowKey(string? columnAValue) =>
        !string.IsNullOrWhiteSpace(columnAValue) && MappableNoteSubIndexRegex.IsMatch(columnAValue.Trim());

    private static string NormalizePlannerResponse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value
            .Replace('\u00A0', ' ')
            .Replace('\u200B', '\0')
            .Replace('\u200C', '\0')
            .Replace('\u200D', '\0')
            .Replace('\uFEFF', '\0')
            .Replace("\0", string.Empty)
            .Trim();

        return string.IsNullOrWhiteSpace(normalized) ? string.Empty : normalized;
    }

    private static bool IsPlaceholderPlannerResponse(string? normalizedValue) =>
        !string.IsNullOrWhiteSpace(normalizedValue)
        && PlaceholderResponseValues.Contains(normalizedValue.Trim());

    private static string ToA1Address(int rowIndex, int colIndex)
    {
        if (rowIndex < 0 || colIndex < 0)
            return $"INVALID(R{rowIndex},C{colIndex})";

        var c = colIndex;
        var letters = string.Empty;
        do
        {
            var rem = c % 26;
            letters = (char)('A' + rem) + letters;
            c = (c / 26) - 1;
        } while (c >= 0);

        return $"{letters}{rowIndex + 1}";
    }
}
