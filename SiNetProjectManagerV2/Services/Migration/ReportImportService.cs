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
///   • <see cref="IInspectionReportService.CreateReportAsync"/> — create report (skipCarryOver=true)
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
    /// Rows are sorted by VersionIndex ascending for correct auto-numbering.
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
        var statusLookup = await BuildStatusLookupAsync(ct);

        // Track synced series to avoid redundant SyncAsync calls.
        var syncedSeriesIds = new HashSet<int>();

        // Sort rows by VersionIndex ascending — critical for auto-numbering.
        var sortedRows = rows.OrderBy(r => r.VersionIndex).ToList();

        // Sheet status based validation classification is postponed.
        // Currently using IsLatestVersion as the sole criterion.

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
        var expectedReportNumber = row.VersionIndex;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var existingReport = await db.InspectionReports
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.SeriesId == seriesId && r.ReportNumber == expectedReportNumber, ct);

        if (existingReport is not null)
        {
            ClassifyExistingReport(existingReport, envelope, row, seriesId, expectedReportNumber, result, log);
            return;
        }

        // Safety check: verify auto-numbering alignment.
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

        // ── F. Create report (skipCarryOver=true) ────────────────────
        // Migration Import: JSON cache is the sole content source.
        // skipCarryOver=true prevents:
        //   • CarryOverUnresolvedNotesAsync (unresolved notes from previous report)
        //   • CopyGeneralFieldsFromPreviousAsync (Chapter 0 fields from previous report)
        //   • CopyReviewedFilesFromPreviousAsync (reviewed file links from previous report)
        // Current logged-in user is NOT used as fallback inspector.
        string? inspectorName = row.MappedReviewerDisplayName;
        int? inspectorId = row.MappedReviewerUserId;

        var report = await _reportService.CreateReportAsync(
            projectId,
            selectedTemplate.Url,
            inspectorName,
            ct,
            inspectorId,
            seriesId,
            skipCarryOver: true);

        result.ReportsCreated++;
        result.Log($"[Phase2] Created report: ReportId={report.ReportId}, SeriesId={seriesId}, ReportNumber={report.ReportNumber} for project {projectNumber} V{row.VersionIndex} (skipCarryOver=true)", log);

        if (report.ReportNumber != expectedReportNumber)
        {
            result.Log(
                $"[Phase2] WARNING: Expected ReportNumber={expectedReportNumber} but got {report.ReportNumber}. " +
                $"This may indicate a concurrency issue.", log);
        }

        // ── G+H. Fill notes from JSON ────────────────────────────────
        await FillNotesFromJsonAsync(report, envelope, compat, statusLookup, row, result, log, ct);
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
    //  Note filling with duplicate prevention
    // ──────────────────────────────────────────────────────────────────

    private async Task FillNotesFromJsonAsync(
        InspectionReport report,
        ExtractionCacheEnvelope envelope,
        TemplateCompatibilityResult compat,
        IReadOnlyDictionary<string, int> statusLookup,
        GoogleSheetReviewMigrationPreviewRow row,
        ReportImportResult result,
        Action<string>? log,
        CancellationToken ct)
    {
        // Get ALL notes already in this report (placeholders from CreateReportAsync).
        // Since skipCarryOver=true, these are only the empty snapshot placeholders.
        var existingNotes = await _reportService.GetNotesForReportAsync(report.ReportId, ct);

        // ── Build comprehensive lookup ───────────────────────────────
        // Key: (SectionId, NoteSubIndex) → NoteId
        // Used for: (1) placeholder reuse, (2) duplicate prevention.
        var noteLookup = new Dictionary<(int SectionId, string SubIndex), long>();
        foreach (var note in existingNotes)
        {
            var key = (note.SectionId, note.NoteSubIndex ?? "");
            noteLookup.TryAdd(key, note.NoteId);
        }

        // Build section lookup: FullCode → SectionId (numbered chapters only).
        var sectionMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var note in existingNotes)
        {
            if (note.Section?.Chapter is null) continue;
            if (note.Section.Chapter.ChapterNumber == 0) continue;
            var fullCode = note.Section.FullCode;
            sectionMap.TryAdd(fullCode, note.SectionId);
        }

        // ── Extract parent codes ─────────────────────────────────────
        var notesWithParent = envelope.Sections
            .Where(s => !string.IsNullOrWhiteSpace(s.SectionCode))
            .Select(s => (
                Note: s,
                ParentCode: GoogleSheetReviewMigrationPreviewService.ExtractParentSectionCode(s.SectionCode)
            ))
            .Where(x => x.ParentCode is not null)
            .ToList();

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

        var sectionGroups = eligibleNotes
            .GroupBy(x => x.ParentCode!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key);

        // ── Determine validation defaults mode ───────────────────────
        bool applyHistoricalDefaults = ShouldApplyMigrationValidationDefaults(row);
        int? passedStatusId = null;
        if (applyHistoricalDefaults)
        {
            statusLookup.TryGetValue("Passed", out var pid);
            passedStatusId = pid > 0 ? pid : null;
            if (passedStatusId is null)
            {
                result.Log($"[Phase2] WARNING: Cannot find 'Passed' status for historical defaults. Defaults will be partial.", log);
            }
        }

        // Collect updates for batch save.
        var noteUpdates = new List<(long NoteId, string? Text, string? Status, int? StatusId)>();
        var plannerResponses = new List<(long NoteId, string ResponseText)>();

        foreach (var sectionGroup in sectionGroups)
        {
            var parentCode = sectionGroup.Key;
            if (!sectionMap.TryGetValue(parentCode, out var sectionId))
            {
                result.Log($"[Phase2] WARNING: Section {parentCode} is import-eligible but no DB Section entity found. Notes skipped.", log);
                result.NotesSkippedTemplateMismatch += sectionGroup.Count();
                continue;
            }

            var subNotes = sectionGroup
                .Select(x => x.Note)
                .OrderBy(s => s.NoteSubIndex, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var parsedNotes = subNotes
                .Select(n => (Note: n, SubIdx: ParseSubIndex(n.NoteSubIndex, parentCode)))
                .OrderBy(x => x.SubIdx)
                .ToList();

            if (parsedNotes.Count == 0) continue;

            int firstJsonSubIndex = parsedNotes[0].SubIdx;

            // Handle gap before first note.
            if (firstJsonSubIndex > 1)
            {
                for (int gapIdx = 2; gapIdx < firstJsonSubIndex; gapIdx++)
                {
                    var gapSubIndex = $"{parentCode}.{gapIdx}";
                    var beforeCount = noteLookup.Count;
                    await EnsureNoteExistsAsync(report.ReportId, sectionId, gapSubIndex, noteLookup, ct);
                    if (noteLookup.Count > beforeCount)
                    {
                        result.GapNotesCreated++;
                        result.Log($"[Phase2] Gap note created: {gapSubIndex}", log);
                    }
                }
            }

            int expectedSubIndex = firstJsonSubIndex > 1 ? firstJsonSubIndex : 2;
            bool isFirstAtPlaceholder = (firstJsonSubIndex == 1);

            foreach (var (jsonNote, actualSubIndex) in parsedNotes)
            {
                // Interior gaps.
                if (!isFirstAtPlaceholder || actualSubIndex > 1)
                {
                    while (expectedSubIndex < actualSubIndex)
                    {
                        var gapSubIndex = $"{parentCode}.{expectedSubIndex}";
                        var beforeCount = noteLookup.Count;
                        await EnsureNoteExistsAsync(report.ReportId, sectionId, gapSubIndex, noteLookup, ct);
                        if (noteLookup.Count > beforeCount)
                        {
                            result.GapNotesCreated++;
                            result.Log($"[Phase2] Gap note created: {gapSubIndex}", log);
                        }
                        expectedSubIndex++;
                    }
                }

                // Resolve or create the target note.
                long noteId;
                var targetSubIndex = jsonNote.NoteSubIndex;
                if (string.IsNullOrWhiteSpace(targetSubIndex))
                    targetSubIndex = $"{parentCode}.{actualSubIndex}";

                if (isFirstAtPlaceholder && actualSubIndex == 1)
                {
                    // Reuse the placeholder at X.Y.1
                    var placeholderKey = (sectionId, $"{parentCode}.1");
                    if (noteLookup.TryGetValue(placeholderKey, out var existingId))
                    {
                        noteId = existingId;
                    }
                    else
                    {
                        noteId = await EnsureNoteExistsAsync(report.ReportId, sectionId, $"{parentCode}.1", noteLookup, ct);
                    }
                    isFirstAtPlaceholder = false;
                    expectedSubIndex = 2;
                }
                else
                {
                    noteId = await EnsureNoteExistsAsync(report.ReportId, sectionId, targetSubIndex, noteLookup, ct);
                    expectedSubIndex = actualSubIndex + 1;
                    if (isFirstAtPlaceholder) isFirstAtPlaceholder = false;
                }

                // ── Determine note content with validation defaults ──
                string? noteText = jsonNote.NoteText;
                string? statusStr = jsonNote.StatusKey;
                int? statusId = null;

                // Map status if present.
                if (!string.IsNullOrWhiteSpace(statusStr) &&
                    statusLookup.TryGetValue(statusStr, out var mappedStatusId))
                {
                    statusId = mappedStatusId;
                }

                // Validation defaults: only when BOTH text AND status are empty,
                // AND this is a historical (non-latest) report.
                bool textEmpty = string.IsNullOrWhiteSpace(noteText);
                bool statusEmpty = string.IsNullOrWhiteSpace(statusStr);

                if (applyHistoricalDefaults && textEmpty && statusEmpty)
                {
                    // Historical report with no content — fill minimal defaults
                    // so the report passes validation. "Passed" = "מקובל" = no active issue.
                    noteText = " ";
                    statusStr = "Passed";
                    statusId = passedStatusId;
                }
                else if (!applyHistoricalDefaults && textEmpty && statusEmpty)
                {
                    // Latest/active report — leave gaps visible for manual review.
                    // Do not auto-fill. Validation will show the gap.
                }
                else if (!textEmpty && statusEmpty)
                {
                    // Note has text but no status — do NOT assign "Passed" automatically.
                    // That would mark a real finding as "accepted", which is misleading.
                    result.Log(
                        $"[Phase2] Missing status for non-empty note: " +
                        $"ReportId={report.ReportId}, SectionId={sectionId}, NoteSubIndex={targetSubIndex}", log);
                }

                noteUpdates.Add((noteId, noteText, statusStr, statusId));
                result.NotesImported++;

                // Planner response.
                if (!string.IsNullOrWhiteSpace(jsonNote.DesignerResponse))
                {
                    plannerResponses.Add((noteId, jsonNote.DesignerResponse));
                    result.PlannerResponsesImported++;
                }
            }
        }

        // ── Batch save ───────────────────────────────────────────────
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
    //  Duplicate-safe note creation
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the NoteId for the given (reportId, sectionId, subIndex).
    /// If a note already exists in the lookup → returns existing NoteId (no AddNoteAsync call).
    /// If not → calls AddNoteAsync, adds to lookup, returns new NoteId.
    /// </summary>
    private async Task<long> EnsureNoteExistsAsync(
        int reportId, int sectionId, string subIndex,
        Dictionary<(int SectionId, string SubIndex), long> noteLookup,
        CancellationToken ct)
    {
        var key = (sectionId, subIndex);
        if (noteLookup.TryGetValue(key, out var existingId))
            return existingId;

        var note = await _reportService.AddNoteAsync(reportId, sectionId, subIndex, ct);
        noteLookup[key] = note.NoteId;
        return note.NoteId;
    }

    // ──────────────────────────────────────────────────────────────────
    //  Validation defaults decision
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Determines whether to apply minimal validation defaults for a migrated report.
    /// Returns true for historical reports (not the latest version) where defaults
    /// are acceptable to pass validation.
    /// Returns false for latest/active reports where validation gaps should remain
    /// visible for manual review.
    /// 
    /// Note: Sheet status based validation classification is postponed.
    /// Currently using IsLatestVersion as the sole criterion.
    /// </summary>
    private static bool ShouldApplyMigrationValidationDefaults(GoogleSheetReviewMigrationPreviewRow row)
    {
        // Historical report: not the latest version → defaults OK.
        // Latest/active report: keep gaps visible.
        return !row.IsLatestVersion;
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

        if (noteSubIndex.StartsWith(parentCode + ".", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = noteSubIndex[(parentCode.Length + 1)..];
            if (int.TryParse(suffix, out var idx) && idx > 0)
                return idx;
        }

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
