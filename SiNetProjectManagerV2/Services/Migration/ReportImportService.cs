using Microsoft.EntityFrameworkCore;
using SiNetSQL.Data;
using SiNetSQL.Models;
using SiNetSQL.Services.InspectionSync;
using SiNetProjectManagerV2.Services.Migration.Models;

namespace SiNetProjectManagerV2.Services.Migration;

/// <summary>
/// Phase 2 thin coordinator: imports selected preview rows into the inspection
/// report DB model using existing services only.
/// 
/// This service contains NO direct DB write logic. All DB mutations go through:
///   • <see cref="TemplateSyncService.EnsureSeriesAsync"/> — find/create series
///   • <see cref="TemplateSyncService.SyncAsync"/> — sync template structure
///   • <see cref="IInspectionReportService.CreateReportAsync"/> — create report
///   • <see cref="IInspectionReportService.AddNoteAsync"/> — add sub-notes
///   • <see cref="IInspectionReportService.SaveNotesAsync"/> — update note content
///   • <see cref="IInspectionReportService.SaveImportedPlannerResponsesAsync"/> — planner responses
/// </summary>
public sealed class ReportImportService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;
    private readonly IInspectionReportService _reportService;
    private readonly TemplateSyncService _templateSyncService;

    public ReportImportService(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        IInspectionReportService reportService,
        TemplateSyncService templateSyncService)
    {
        _dbFactory = dbFactory;
        _reportService = reportService;
        _templateSyncService = templateSyncService;
    }

    /// <summary>
    /// Import a batch of selected preview rows into the DB.
    /// Each row is processed independently — individual failures do not block others.
    /// </summary>
    public async Task<ReportImportResult> ImportRowsAsync(
        IReadOnlyList<GoogleSheetReviewMigrationPreviewRow> rows,
        IReadOnlyDictionary<string, TemplateCompatibilityResult> compatResults,
        IReadOnlyList<TemplateSyncRow> templateRows,
        InspectionTemplateItem selectedTemplate,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        var result = new ReportImportResult();

        // Pre-load status lookup for StatusKey → NoteStatusId mapping.
        var statusLookup = await BuildStatusLookupAsync(ct);

        // Track synced series to avoid redundant SyncAsync calls.
        var syncedSeriesIds = new HashSet<int>();

        foreach (var row in rows)
        {
            result.RowsProcessed++;
            try
            {
                await ImportSingleRowAsync(
                    row, compatResults, templateRows, selectedTemplate,
                    statusLookup, syncedSeriesIds, result, log, ct);
            }
            catch (Exception ex)
            {
                result.Errors++;
                result.Log($"[Phase2] ERROR row {row.SheetRowIndex} project {row.ProjectNumberFromSheet}: {ex.Message}", log);
            }
        }

        result.Log(result.BuildSummary(), log);
        return result;
    }

    // ──────────────────────────────────────────────────────────────────
    //  Single-row import
    // ──────────────────────────────────────────────────────────────────

    private async Task ImportSingleRowAsync(
        GoogleSheetReviewMigrationPreviewRow row,
        IReadOnlyDictionary<string, TemplateCompatibilityResult> compatResults,
        IReadOnlyList<TemplateSyncRow> templateRows,
        InspectionTemplateItem selectedTemplate,
        IReadOnlyDictionary<string, int> statusLookup,
        HashSet<int> syncedSeriesIds,
        ReportImportResult result,
        Action<string>? log,
        CancellationToken ct)
    {
        // ── A. Validate eligibility ──────────────────────────────────
        if (row.ResolvedProjectId is not { } projectId)
        {
            result.Log($"[Phase2] SKIP row {row.SheetRowIndex}: no resolved project", log);
            return;
        }

        var templateStatus = row.TemplateValidationStatus ?? "NotValidated";
        if (templateStatus is not ("FullMatch" or "PartialMatch"))
        {
            result.Log($"[Phase2] SKIP row {row.SheetRowIndex}: template status = {templateStatus}", log);
            return;
        }

        var projectNumber = row.ResolvedProjectNumber ?? row.ProjectNumberFromSheet;
        var reportNumberStr = row.ReportNumber;
        if (string.IsNullOrWhiteSpace(projectNumber) || string.IsNullOrWhiteSpace(reportNumberStr))
        {
            result.Log($"[Phase2] SKIP row {row.SheetRowIndex}: missing project/report number", log);
            return;
        }

        // Look up compatibility result for this row.
        var compatKey = $"{projectNumber}|{row.VersionIndex}|{reportNumberStr}";
        if (!compatResults.TryGetValue(compatKey, out var compat) || !compat.HasAnyMatch)
        {
            result.Log($"[Phase2] SKIP row {row.SheetRowIndex}: no template compatibility result or no matches", log);
            return;
        }

        // ── B. Load JSON ─────────────────────────────────────────────
        var envelope = await ExtractionCacheService.LoadAsync(projectNumber, row.VersionIndex, reportNumberStr, ct);
        if (envelope is null)
        {
            result.JsonMissing++;
            result.Log($"[Phase2] SKIP row {row.SheetRowIndex}: JSON cache missing for {projectNumber} V{row.VersionIndex} R{reportNumberStr}", log);
            return;
        }

        // Count and skip general fields (not imported in first slice).
        if (envelope.GeneralFields.Count > 0)
        {
            result.GeneralFieldsSkipped += envelope.GeneralFields.Count;
            result.Log($"[Phase2] Skipping {envelope.GeneralFields.Count} general field(s) for row {row.SheetRowIndex} (Chapter 0 import postponed)", log);
        }

        // ── C. Ensure InspectionSeries ───────────────────────────────
        var seriesId = await _templateSyncService.EnsureSeriesAsync(
            projectId,
            selectedTemplate.SpreadsheetId,
            selectedTemplate.Url,
            ct);

        result.Log($"[Phase2] Row {row.SheetRowIndex}: SeriesId = {seriesId}", log);

        // ── D. Sync template structure (once per series) ─────────────
        if (syncedSeriesIds.Add(seriesId))
        {
            await _templateSyncService.SyncAsync(templateRows, seriesId, ct);
            result.Log($"[Phase2] Template synced for series {seriesId}", log);
        }

        // ── E. Duplicate guard ───────────────────────────────────────
        if (!int.TryParse(reportNumberStr, out var reportNumber))
        {
            result.Log($"[Phase2] SKIP row {row.SheetRowIndex}: cannot parse report number '{reportNumberStr}'", log);
            return;
        }

        // Determine expected report number for this version.
        // VersionIndex is 1-based; each version maps to its own ReportNumber.
        var targetReportNumber = row.VersionIndex;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var existingReport = await db.InspectionReports
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.SeriesId == seriesId && r.ReportNumber == targetReportNumber, ct);

        if (existingReport is not null)
        {
            if (!string.IsNullOrEmpty(existingReport.SentSpreadsheetId) &&
                existingReport.SentSpreadsheetId == envelope.ReportSpreadsheetId)
            {
                result.ReportsSkippedAlreadyExists++;
                result.Log($"[Phase2] SKIP row {row.SheetRowIndex}: report already exists (AlreadyUpToDate, SeriesId={seriesId}, ReportNumber={targetReportNumber})", log);
            }
            else if (!string.IsNullOrEmpty(existingReport.SentSpreadsheetId) &&
                     existingReport.SentSpreadsheetId != envelope.ReportSpreadsheetId)
            {
                result.ReportsSkippedConflict++;
                result.Log($"[Phase2] SKIP row {row.SheetRowIndex}: report already exists with different SentSpreadsheetId (Conflict)", log);
            }
            else
            {
                result.ReportsSkippedAlreadyExists++;
                result.Log($"[Phase2] SKIP row {row.SheetRowIndex}: report already exists (AlreadyExists, SentSpreadsheetId is null)", log);
            }
            return;
        }

        // ── F. Create report ─────────────────────────────────────────
        // Use mapped reviewer from preview row; null if not mapped.
        string? inspectorName = row.MappedReviewerDisplayName;
        int? inspectorId = row.MappedReviewerUserId;

        var report = await _reportService.CreateReportAsync(
            projectId,
            selectedTemplate.Url,
            inspectorName,
            ct,
            inspectorId,
            seriesId);

        result.ReportsCreated++;
        result.Log($"[Phase2] Created report: ReportId={report.ReportId}, SeriesId={seriesId}, ReportNumber={report.ReportNumber} for project {projectNumber} V{row.VersionIndex}", log);

        // ── G+H. Fill notes from JSON ────────────────────────────────
        await FillNotesFromJsonAsync(report, envelope, compat, statusLookup, result, log, ct);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Note filling
    // ──────────────────────────────────────────────────────────────────

    private async Task FillNotesFromJsonAsync(
        InspectionReport report,
        ExtractionCacheEnvelope envelope,
        TemplateCompatibilityResult compat,
        IReadOnlyDictionary<string, int> statusLookup,
        ReportImportResult result,
        Action<string>? log,
        CancellationToken ct)
    {
        // Get placeholder notes created by CreateReportAsync.
        var placeholderNotes = await _reportService.GetNotesForReportAsync(report.ReportId, ct);

        // Build lookup: Section.FullCode → (SectionId, placeholder NoteId).
        // Only numbered chapters (ChapterNumber > 0) — Chapter 0 general fields are skipped.
        var sectionMap = new Dictionary<string, (int SectionId, long PlaceholderNoteId)>(StringComparer.OrdinalIgnoreCase);
        foreach (var note in placeholderNotes)
        {
            if (note.Section?.Chapter is null) continue;
            if (note.Section.Chapter.ChapterNumber == 0) continue; // Skip Chapter 0
            var fullCode = note.Section.FullCode;
            if (!sectionMap.ContainsKey(fullCode))
            {
                sectionMap[fullCode] = (note.SectionId, note.NoteId);
            }
        }

        // Group JSON sections by parent code (X.Y), sorted by NoteSubIndex.
        var eligibleSections = envelope.Sections
            .Where(s => !string.IsNullOrWhiteSpace(s.SectionCode))
            .Where(s => compat.IsImportEligible(s.SectionCode))
            .GroupBy(s => s.SectionCode, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key);

        // Track skipped sections.
        var skippedSections = envelope.Sections
            .Where(s => !string.IsNullOrWhiteSpace(s.SectionCode))
            .Where(s => !compat.IsImportEligible(s.SectionCode))
            .ToList();

        result.NotesSkippedTemplateMismatch += skippedSections.Count;
        foreach (var skipped in skippedSections.GroupBy(s => s.SectionCode))
        {
            result.Log($"[Phase2] Skipped {skipped.Count()} note(s) for section {skipped.Key}: not import-eligible in target template", log);
        }

        // Collect updates for batch save.
        var noteUpdates = new List<(long NoteId, string? Text, string? Status, int? StatusId)>();
        var plannerResponses = new List<(long NoteId, string ResponseText)>();

        foreach (var sectionGroup in eligibleSections)
        {
            var parentCode = sectionGroup.Key; // e.g., "1.1"
            if (!sectionMap.TryGetValue(parentCode, out var sectionInfo))
            {
                // Section exists in JSON and is template-eligible, but no matching
                // Section entity was created by SyncAsync. This shouldn't happen
                // if template compatibility is correct — log as warning.
                result.Log($"[Phase2] WARNING: Section {parentCode} is import-eligible but no DB Section entity found. Notes skipped.", log);
                result.NotesSkippedTemplateMismatch += sectionGroup.Count();
                continue;
            }

            var subNotes = sectionGroup
                .OrderBy(s => s.NoteSubIndex, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Determine expected sub-indexes: X.Y.1, X.Y.2, ...
            int expectedSubIndex = 1;
            bool isFirst = true;

            foreach (var jsonNote in subNotes)
            {
                // Parse the Z from X.Y.Z (e.g., "1.1.3" → 3).
                int actualSubIndex = ParseSubIndex(jsonNote.NoteSubIndex, parentCode);

                // Handle gaps: create empty placeholder notes for missing indexes.
                while (expectedSubIndex < actualSubIndex && !isFirst)
                {
                    var gapSubIndex = $"{parentCode}.{expectedSubIndex}";
                    var gapNote = await _reportService.AddNoteAsync(
                        report.ReportId, sectionInfo.SectionId, gapSubIndex, ct);
                    result.GapNotesCreated++;
                    result.Log($"[Phase2] Gap note created: {gapSubIndex}", log);
                    expectedSubIndex++;
                }

                long noteId;
                if (isFirst)
                {
                    // First sub-note → update the placeholder created by CreateReportAsync.
                    noteId = sectionInfo.PlaceholderNoteId;
                    isFirst = false;
                    expectedSubIndex = actualSubIndex + 1;
                }
                else
                {
                    // Additional sub-notes → AddNoteAsync.
                    var subIndex = jsonNote.NoteSubIndex;
                    if (string.IsNullOrWhiteSpace(subIndex))
                        subIndex = $"{parentCode}.{actualSubIndex}";

                    var addedNote = await _reportService.AddNoteAsync(
                        report.ReportId, sectionInfo.SectionId, subIndex, ct);
                    noteId = addedNote.NoteId;
                    expectedSubIndex = actualSubIndex + 1;
                }

                // Map status.
                int? statusId = null;
                string? statusStr = jsonNote.StatusKey;
                if (!string.IsNullOrWhiteSpace(jsonNote.StatusKey) &&
                    statusLookup.TryGetValue(jsonNote.StatusKey, out var mappedStatusId))
                {
                    statusId = mappedStatusId;
                }

                // Collect note content update.
                noteUpdates.Add((noteId, jsonNote.NoteText, statusStr, statusId));
                result.NotesImported++;

                // Collect planner response if present.
                if (!string.IsNullOrWhiteSpace(jsonNote.DesignerResponse))
                {
                    plannerResponses.Add((noteId, jsonNote.DesignerResponse));
                    result.PlannerResponsesImported++;
                }
            }
        }

        // ── I. Batch save ────────────────────────────────────────────
        if (noteUpdates.Count > 0)
        {
            await _reportService.SaveNotesAsync(noteUpdates, [], ct);
        }

        if (plannerResponses.Count > 0)
        {
            await _reportService.SaveImportedPlannerResponsesAsync(plannerResponses, null, ct);
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Build a lookup from StatusKey → StatusId using existing active statuses.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, int>> BuildStatusLookupAsync(CancellationToken ct)
    {
        var statuses = await _reportService.GetActiveStatusOptionsAsync(ct);
        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in statuses)
        {
            if (!string.IsNullOrWhiteSpace(s.StatusKey) && !lookup.ContainsKey(s.StatusKey))
                lookup[s.StatusKey] = s.StatusId;
        }
        return lookup;
    }

    /// <summary>
    /// Parse the sub-index number Z from "X.Y.Z" given the parent "X.Y".
    /// Falls back to sequential numbering if parsing fails.
    /// </summary>
    private static int ParseSubIndex(string? noteSubIndex, string parentCode)
    {
        if (string.IsNullOrWhiteSpace(noteSubIndex))
            return 1;

        // Try to extract the trailing number after the parent code prefix.
        // e.g., "1.1.3" with parent "1.1" → "3" → 3
        if (noteSubIndex.StartsWith(parentCode + ".", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = noteSubIndex[(parentCode.Length + 1)..];
            if (int.TryParse(suffix, out var idx) && idx > 0)
                return idx;
        }

        // Fallback: try the last segment after the last dot.
        var lastDot = noteSubIndex.LastIndexOf('.');
        if (lastDot >= 0)
        {
            var lastPart = noteSubIndex[(lastDot + 1)..];
            if (int.TryParse(lastPart, out var idx) && idx > 0)
                return idx;
        }

        return 1;
    }
}
