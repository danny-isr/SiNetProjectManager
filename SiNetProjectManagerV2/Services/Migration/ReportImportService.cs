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
///   • <see cref="IInspectionReportService.CreateReportAsync"/> — create report (auto-numbered)
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
    /// 
    /// IMPORTANT: Rows must be sorted by VersionIndex ascending for correct auto-numbering.
    /// CreateReportAsync auto-generates ReportNumber = MAX+1 per series.
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
        // StatusKey values in the DB are exact, stable strings:
        // "Passed", "Failed", "RecurringFailed", "NotApplicable", "ManagerReview".
        // JSON StatusKey uses the same exact values. No normalization needed.
        var statusLookup = await BuildStatusLookupAsync(ct);

        // Track synced series to avoid redundant SyncAsync calls.
        var syncedSeriesIds = new HashSet<int>();

        // Sort rows by VersionIndex ascending — critical for auto-numbering.
        var sortedRows = rows.OrderBy(r => r.VersionIndex).ToList();

        foreach (var row in sortedRows)
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
        // CreateReportAsync auto-numbers: MAX(ReportNumber)+1 per series.
        // VersionIndex is 1-based: V1 should become ReportNumber=1, V2→2, etc.
        // The expected ReportNumber for this version equals VersionIndex —
        // but only if versions are imported in ascending order.
        var expectedReportNumber = row.VersionIndex;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Check if a report with this number already exists in the series.
        var existingReport = await db.InspectionReports
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.SeriesId == seriesId && r.ReportNumber == expectedReportNumber, ct);

        if (existingReport is not null)
        {
            ClassifyExistingReport(existingReport, envelope, row, seriesId, expectedReportNumber, result, log);
            return;
        }

        // Safety check: verify the next auto-generated number matches expectations.
        // If previous versions were not imported (e.g., V1 missing, importing V2),
        // the auto-number would be 1 but we expect 2 — block this.
        var currentMaxReportNumber = await db.InspectionReports
            .Where(r => r.SeriesId == seriesId)
            .MaxAsync(r => (int?)r.ReportNumber, ct) ?? 0;

        var nextAutoNumber = currentMaxReportNumber + 1;
        if (nextAutoNumber != expectedReportNumber)
        {
            result.Errors++;
            result.Log(
                $"[Phase2] BLOCKED row {row.SheetRowIndex}: expected ReportNumber={expectedReportNumber} " +
                $"(V{row.VersionIndex}) but next auto-number would be {nextAutoNumber}. " +
                $"Import versions in ascending order. Current max in series: {currentMaxReportNumber}.", log);
            return;
        }

        // ── F. Create report ─────────────────────────────────────────
        // Use mapped reviewer from preview row; null if not mapped.
        // Current logged-in user is NOT used as fallback.
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

        // Verify the auto-generated number matches our expectation.
        if (report.ReportNumber != expectedReportNumber)
        {
            result.Log(
                $"[Phase2] WARNING: Expected ReportNumber={expectedReportNumber} but got {report.ReportNumber}. " +
                $"This may indicate a concurrency issue.", log);
        }

        // ── G+H. Fill notes from JSON ────────────────────────────────
        await FillNotesFromJsonAsync(report, envelope, compat, statusLookup, result, log, ct);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Duplicate guard classification
    // ──────────────────────────────────────────────────────────────────

    private static void ClassifyExistingReport(
        InspectionReport existing,
        ExtractionCacheEnvelope envelope,
        GoogleSheetReviewMigrationPreviewRow row,
        int seriesId,
        int expectedReportNumber,
        ReportImportResult result,
        Action<string>? log)
    {
        if (!string.IsNullOrEmpty(existing.SentSpreadsheetId) &&
            existing.SentSpreadsheetId == envelope.ReportSpreadsheetId)
        {
            result.ReportsSkippedAlreadyExists++;
            result.Log($"[Phase2] SKIP row {row.SheetRowIndex}: report already exists (AlreadyUpToDate, SeriesId={seriesId}, ReportNumber={expectedReportNumber})", log);
        }
        else if (!string.IsNullOrEmpty(existing.SentSpreadsheetId) &&
                 existing.SentSpreadsheetId != envelope.ReportSpreadsheetId)
        {
            result.ReportsSkippedConflict++;
            result.Log($"[Phase2] SKIP row {row.SheetRowIndex}: report already exists with different SentSpreadsheetId (Conflict)", log);
        }
        else
        {
            result.ReportsSkippedAlreadyExists++;
            result.Log($"[Phase2] SKIP row {row.SheetRowIndex}: report already exists (AlreadyExists, SentSpreadsheetId is null)", log);
        }
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

        // Extract parent section code for each JSON note using the same logic
        // as the Full Report Preview (GoogleSheetReviewMigrationPreviewService).
        // This converts "1.1.3" → "1.1", "3.6" → "3.6", etc.
        var notesWithParent = envelope.Sections
            .Where(s => !string.IsNullOrWhiteSpace(s.SectionCode))
            .Select(s => (
                Note: s,
                ParentCode: GoogleSheetReviewMigrationPreviewService.ExtractParentSectionCode(s.SectionCode)
            ))
            .Where(x => x.ParentCode is not null)
            .ToList();

        // Split into eligible and skipped based on template compatibility.
        var eligibleNotes = notesWithParent
            .Where(x => compat.IsImportEligible(x.ParentCode))
            .ToList();

        var skippedNotes = notesWithParent
            .Where(x => !compat.IsImportEligible(x.ParentCode))
            .ToList();

        result.NotesSkippedTemplateMismatch += skippedNotes.Count;
        foreach (var group in skippedNotes.GroupBy(x => x.ParentCode))
        {
            result.Log($"[Phase2] Skipped {group.Count()} note(s) for section {group.Key}: not import-eligible in target template", log);
        }

        // Group eligible notes by parent section code.
        var sectionGroups = eligibleNotes
            .GroupBy(x => x.ParentCode!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key);

        // Collect updates for batch save.
        var noteUpdates = new List<(long NoteId, string? Text, string? Status, int? StatusId)>();
        var plannerResponses = new List<(long NoteId, string ResponseText)>();

        foreach (var sectionGroup in sectionGroups)
        {
            var parentCode = sectionGroup.Key; // e.g., "1.1"
            if (!sectionMap.TryGetValue(parentCode, out var sectionInfo))
            {
                // Section is import-eligible but no DB Section entity found.
                // This shouldn't happen if template compatibility is correct.
                result.Log($"[Phase2] WARNING: Section {parentCode} is import-eligible but no DB Section entity found. Notes skipped.", log);
                result.NotesSkippedTemplateMismatch += sectionGroup.Count();
                continue;
            }

            var subNotes = sectionGroup
                .Select(x => x.Note)
                .OrderBy(s => s.NoteSubIndex, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Parse sub-indexes to determine positions.
            // The placeholder from CreateReportAsync has NoteSubIndex = "X.Y.1".
            // We must NOT write content from a different sub-index into the placeholder.
            var parsedNotes = subNotes
                .Select(n => (Note: n, SubIdx: ParseSubIndex(n.NoteSubIndex, parentCode)))
                .OrderBy(x => x.SubIdx)
                .ToList();

            if (parsedNotes.Count == 0) continue;

            int firstJsonSubIndex = parsedNotes[0].SubIdx;

            // If the first JSON note is NOT index 1, we need gap handling.
            // The placeholder exists at index 1 (created by CreateReportAsync).
            // We must create gap notes for indexes 2..firstJsonSubIndex-1,
            // then create the actual content note at firstJsonSubIndex via AddNoteAsync
            // (do NOT put its content into the placeholder at index 1).
            if (firstJsonSubIndex > 1)
            {
                // Create gap notes for indexes 2 through firstJsonSubIndex-1.
                // The placeholder at index 1 remains empty.
                for (int gapIdx = 2; gapIdx < firstJsonSubIndex; gapIdx++)
                {
                    var gapSubIndex = $"{parentCode}.{gapIdx}";
                    await _reportService.AddNoteAsync(
                        report.ReportId, sectionInfo.SectionId, gapSubIndex, ct);
                    result.GapNotesCreated++;
                    result.Log($"[Phase2] Gap note created: {gapSubIndex}", log);
                }
            }

            int expectedSubIndex = firstJsonSubIndex > 1 ? firstJsonSubIndex : 2;
            // Track whether first note at index 1 is being filled from JSON.
            bool isFirstAtPlaceholder = (firstJsonSubIndex == 1);

            foreach (var (jsonNote, actualSubIndex) in parsedNotes)
            {
                // Handle interior gaps (between JSON notes).
                if (!isFirstAtPlaceholder || actualSubIndex > 1)
                {
                    while (expectedSubIndex < actualSubIndex)
                    {
                        var gapSubIndex = $"{parentCode}.{expectedSubIndex}";
                        await _reportService.AddNoteAsync(
                            report.ReportId, sectionInfo.SectionId, gapSubIndex, ct);
                        result.GapNotesCreated++;
                        result.Log($"[Phase2] Gap note created: {gapSubIndex}", log);
                        expectedSubIndex++;
                    }
                }

                long noteId;
                if (isFirstAtPlaceholder && actualSubIndex == 1)
                {
                    // First JSON note at index 1 → update the existing placeholder.
                    noteId = sectionInfo.PlaceholderNoteId;
                    isFirstAtPlaceholder = false;
                    expectedSubIndex = 2;
                }
                else
                {
                    // Additional sub-notes or first note at index > 1 → AddNoteAsync.
                    var subIndex = jsonNote.NoteSubIndex;
                    if (string.IsNullOrWhiteSpace(subIndex))
                        subIndex = $"{parentCode}.{actualSubIndex}";

                    var addedNote = await _reportService.AddNoteAsync(
                        report.ReportId, sectionInfo.SectionId, subIndex, ct);
                    noteId = addedNote.NoteId;
                    expectedSubIndex = actualSubIndex + 1;
                    if (isFirstAtPlaceholder) isFirstAtPlaceholder = false;
                }

                // Map status. StatusKey values are exact, stable strings in the DB seed:
                // "Passed", "Failed", "RecurringFailed", "NotApplicable", "ManagerReview".
                // No normalization needed — JSON StatusKey uses the same values.
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
    /// StatusKey values are exact, stable strings (seeded in InspectionSystemConfiguration):
    /// "Passed", "Failed", "RecurringFailed", "NotApplicable", "ManagerReview".
    /// OrdinalIgnoreCase is used defensively but not strictly needed.
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
