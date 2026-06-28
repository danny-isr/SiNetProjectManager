using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SiNet.Application.Workflow;
using SiNetProjectManagerV2.Services.Migration.Models;
using SiNetSQL.Data;
using SiNetSQL.Services.InspectionSync;
using SiOffice.GoogleConnector.Reports;
using SiNetSQL.Services.Workflow;
using SiNetSQL.Services;
using SiNetSQL.Models;

namespace SiNetProjectManagerV2.Services.Migration;

/// <summary>
/// Service for building a purely read-only preview of the Google Sheet Review Migration.
/// This service ONLY reads data. It does NOT call SaveChanges, Add, Update, Remove, CreateReportAsync, etc.
/// </summary>
public sealed class GoogleSheetReviewMigrationPreviewService
{
    private readonly IDbContextFactory<SiNetSQLDbContext> _dbFactory;
    private readonly IWorkflowQueryService _workflowQueryService;
    private readonly GoogleAuthService _authService;

    /// <summary>
    /// Template compatibility results keyed by "ProjectNumber|VersionIndex|ReportNumber".
    /// Populated during BuildPreviewAsync when targetTemplateSections is provided.
    /// Used by the UI for double-click detail preview.
    /// </summary>
    private readonly Dictionary<string, TemplateCompatibilityResult> _compatibilityResults = new();

    /// <summary>Public accessor for compatibility results (for double-click preview).</summary>
    public IReadOnlyDictionary<string, TemplateCompatibilityResult> CompatibilityResults => _compatibilityResults;

    public GoogleSheetReviewMigrationPreviewService(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        IWorkflowQueryService workflowQueryService,
        GoogleAuthService authService)
    {
        _dbFactory = dbFactory;
        _workflowQueryService = workflowQueryService;
        _authService = authService;
    }

    /// <summary>
    /// Scans the index sheet and returns a list of unique reviewer names found in the sheet.
    /// This is used to populate the UI for the Reviewer Mapping step.
    /// </summary>
    public async Task<List<string>> GetDistinctReviewersAsync(string indexSheetId, Action<string>? log = null)
    {
        log?.Invoke($"Starting reviewer scan for sheet: {indexSheetId}");
        var reader = new IndexSheetReader(_authService);
        var links = await reader.ReadReportHyperlinksAsync(indexSheetId, log, includeRowsWithoutLinks: true);

        log?.Invoke($"Reader returned {links.Count} rows total.");

        var withReviewer = links.Where(l => !string.IsNullOrWhiteSpace(l.Reviewer)).ToList();
        log?.Invoke($"Rows with reviewer text: {withReviewer.Count}");

        if (withReviewer.Count == 0)
        {
            log?.Invoke("⚠ No reviewer names found in any row. Check that the sheet has a recognized reviewer/inspector column (בודק, שם בודק, מבקר, etc.).");
        }

        var distinct = withReviewer
            .Select(l => l.Reviewer!.Trim())
            .Distinct()
            .OrderBy(r => r)
            .ToList();

        log?.Invoke($"Distinct reviewers ({distinct.Count}): [{string.Join(", ", distinct.Take(15))}]");
        return distinct;
    }

    /// <summary>
    /// Builds the complete, read-only preview for Phase 1.
    /// </summary>
    public async Task<List<GoogleSheetReviewMigrationPreviewRow>> BuildPreviewAsync(
        string indexSheetId,
        IReadOnlyDictionary<string, int> reviewerMapping,
        IReadOnlyList<TemplateSyncRow>? targetTemplateSections = null,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        var reader = new IndexSheetReader(_authService);
        var links = await reader.ReadReportHyperlinksAsync(indexSheetId, log, ct, includeRowsWithoutLinks: true);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Pre-load all projects once for efficient in-memory matching across all rows
        var allProjects = await db.Projects.AsNoTracking().ToListAsync(ct);

        // Build in-memory target template section map: "X.Y" → TemplateSyncRow
        Dictionary<string, TemplateSyncRow>? templateMap = null;
        if (targetTemplateSections?.Count > 0)
        {
            templateMap = new Dictionary<string, TemplateSyncRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var tsr in targetTemplateSections)
            {
                if (string.IsNullOrWhiteSpace(tsr.SectionCode) || tsr.ChapterNumber == 0) continue;
                var parentCode = ExtractParentSectionCode(tsr.SectionCode);
                if (!string.IsNullOrWhiteSpace(parentCode))
                    templateMap.TryAdd(parentCode, tsr);
            }
            log?.Invoke($"[Template] Target template map built: {templateMap.Count} sections.");
        }

        _compatibilityResults.Clear();

        var results = new List<GoogleSheetReviewMigrationPreviewRow>();

        foreach (var link in links)
        {
            // Generate rows per version — SheetRowIndex uses the real Sheet row (1-based)
            var versionCount = link.ReportSpreadsheetIds.Count;
            if (versionCount == 0)
            {
                var row = await ProcessSingleRowAsync(db, allProjects, link, reviewerMapping, 1, 1, null, templateMap, ct);
                results.Add(row);
            }
            else
            {
                for (int i = 0; i < versionCount; i++)
                {
                    var spreadsheetId = link.ReportSpreadsheetIds[i];
                    var row = await ProcessSingleRowAsync(db, allProjects, link, reviewerMapping, i + 1, versionCount, spreadsheetId, templateMap, ct);
                    results.Add(row);
                }
            }
        }

        // ── Post-process: detect duplicate projects by ResolvedProjectId ──
        // A project is "duplicate" when more than one distinct source Sheet row
        // resolves to the same DB project. Multiple version rows from the same
        // source Sheet row do NOT count as duplicates by themselves.
        var duplicateProjectIds = results
            .Where(r => r.ResolvedProjectId.HasValue)
            .GroupBy(r => r.ResolvedProjectId!.Value)
            .Where(g => g.Select(r => r.SheetRowIndex).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

        foreach (var row in results)
        {
            if (row.ResolvedProjectId.HasValue && duplicateProjectIds.Contains(row.ResolvedProjectId.Value))
            {
                row.IsDuplicateProjectRow = true;

                // Only override classification if the row is not already in a stronger blocking state
                if (row.Classification is MigrationPreviewClassification.CommitReady
                    or MigrationPreviewClassification.CommitReadyWithWarning
                    or MigrationPreviewClassification.AlreadyDone)
                {
                    row.Classification = MigrationPreviewClassification.DuplicateProjectRow;
                    row.BlockingReason = "Duplicate project row in sheet.";
                }
            }
        }

        // ── Preview Summary ────────────────────────────────────────────
        int distinctSheetRows        = links.Count;
        int distinctResolvedProjects = results.Where(r => r.ResolvedProjectId.HasValue)
                                              .Select(r => r.ResolvedProjectId!.Value)
                                              .Distinct().Count();
        int versionRows              = results.Count(r => r.VersionIndex > 1);

        // Count workflow creations only on the latest version row of each group,
        // where the row actually proposes creation and is not in a blocked/conflict state.
        static bool IsBlockedClassification(MigrationPreviewClassification c) =>
            c is MigrationPreviewClassification.NoMatch
              or MigrationPreviewClassification.ExistingWorkflowConflict
              or MigrationPreviewClassification.ExistingReportConflict
              or MigrationPreviewClassification.MissingData
              or MigrationPreviewClassification.DuplicateProjectRow
              or MigrationPreviewClassification.ManagerReview
              or MigrationPreviewClassification.BackwardMovement;

        int proposedWorkflowCreations = results.Count(r =>
            r.IsLatestVersion &&
            r.ProposedWorkflowAction.StartsWith("Create workflow", StringComparison.OrdinalIgnoreCase) &&
            !IsBlockedClassification(r.Classification));

        int proposedReportImports    = results.Count(r => r.ProposedReportAction.StartsWith("Import report", StringComparison.OrdinalIgnoreCase));
        int noMatch                  = results.Count(r => r.Classification == MigrationPreviewClassification.NoMatch);
        int reviewerNotMapped        = results.Count(r => r.Classification == MigrationPreviewClassification.ReviewerNotMapped);
        int jsonMissing              = results.Count(r => r.JsonCacheStatus.StartsWith("Missing", StringComparison.OrdinalIgnoreCase));
        int workflowConflict         = results.Count(r => r.Classification == MigrationPreviewClassification.ExistingWorkflowConflict);
        int reportConflict           = results.Count(r => r.Classification == MigrationPreviewClassification.ExistingReportConflict);

        log?.Invoke("── Preview Summary ──────────────────────────────────");
        log?.Invoke($"  Total preview rows:              {results.Count}");
        log?.Invoke($"  Distinct sheet rows:             {distinctSheetRows}");
        log?.Invoke($"  Distinct resolved projects:      {distinctResolvedProjects}");
        log?.Invoke($"  Report-version rows (V2+):       {versionRows}");
        log?.Invoke($"  Proposed workflow creations:     {proposedWorkflowCreations}");
        log?.Invoke($"  Proposed report imports:         {proposedReportImports}");
        log?.Invoke($"  NoMatch rows:                    {noMatch}");
        log?.Invoke($"  ReviewerNotMapped rows:          {reviewerNotMapped}");
        log?.Invoke($"  JSON Missing rows:               {jsonMissing}");
        log?.Invoke($"  ExistingWorkflowConflict rows:   {workflowConflict}");
        log?.Invoke($"  ExistingReportConflict rows:     {reportConflict}");
        log?.Invoke("─────────────────────────────────────────────────────");

        return results;
    }

    private async Task<GoogleSheetReviewMigrationPreviewRow> ProcessSingleRowAsync(
        SiNetSQLDbContext db,
        IReadOnlyList<Project> allProjects,
        IndexSheetReportLink link,
        IReadOnlyDictionary<string, int> reviewerMapping,
        int versionIndex,
        int totalVersions,
        string? reportSpreadsheetId,
        Dictionary<string, TemplateSyncRow>? templateMap,
        CancellationToken ct)
    {
        bool isLatestVersion = (versionIndex == totalVersions);

        var row = new GoogleSheetReviewMigrationPreviewRow
        {
            SheetRowIndex = link.RowIndex + 1, // 1-based: actual Google Sheet row number
            ProjectNumberFromSheet = link.ProjectRef,
            ProjectNameFromSheet = link.ProjectRef,
            ReportNumber = link.ReportNumber,
            SheetStatus = link.Status ?? "Unknown",
            ReviewerNameFromSheet = link.Reviewer ?? "Unknown",
            VersionIndex = versionIndex,
            IsLatestVersion = isLatestVersion,
        };

        // 1. Resolve Project (Read Only) — in-memory, no DB queries
        var (resolvedId, resolvedNumber, resolvedName) = ResolveProjectReadOnly(allProjects, link.ProjectRef);
        row.ResolvedProjectId = resolvedId;
        row.ResolvedProjectDisplayName = resolvedName;
        if (!string.IsNullOrWhiteSpace(resolvedNumber))
            row.ResolvedProjectNumber = resolvedNumber;
        
        if (resolvedId.HasValue)
        {
            row.ProjectMatchStatus = "Found";
        }
        else
        {
            row.ProjectMatchStatus = "Not Found";
            row.Classification = MigrationPreviewClassification.NoMatch;
            row.BlockingReason = "Project not found in DB.";
            return row;
        }

        // 2. Reviewer Mapping
        bool hasReviewerGroupWarning = false;
        if (reviewerMapping.TryGetValue(row.ReviewerNameFromSheet, out var mappedUserId))
        {
            row.MappedReviewerUserId = mappedUserId;
            var user = await db.Siusers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == mappedUserId, ct);
            row.MappedReviewerDisplayName = user?.Name;
            row.ReviewerMappingStatus = "Mapped";
            row.WarningMessages = "Reviewer mapped — group validation not verified in Phase 1";
            hasReviewerGroupWarning = true;
        }
        else
        {
            row.ReviewerMappingStatus = "Not Mapped";
            row.Classification = MigrationPreviewClassification.ReviewerNotMapped;
            row.BlockingReason = "Reviewer mapping required.";
            return row;
        }

        // 3. JSON Cache
        ExtractionCacheEnvelope? jsonCache = null;
        if (!string.IsNullOrWhiteSpace(resolvedNumber))
        {
            jsonCache = await ExtractionCacheService.LoadAsync(resolvedNumber, versionIndex, link.ReportNumber, ct);
            if (jsonCache != null)
            {
                row.JsonCacheStatus = $"Found (V{versionIndex})";
                row.JsonReportSpreadsheetId = jsonCache.ReportSpreadsheetId;
                row.JsonPath = ExtractionCacheService.GetProjectCacheFolder(resolvedNumber);
                
                if (!string.IsNullOrWhiteSpace(reportSpreadsheetId) && jsonCache.ReportSpreadsheetId != reportSpreadsheetId)
                {
                    row.JsonCacheStatus += " [Source mismatch — blocked]";
                    row.Classification = MigrationPreviewClassification.ExistingReportConflict;
                    row.BlockingReason = "JSON cache source does not match Sheet report link.";
                    return row;
                }
            }
            else
            {
                row.JsonCacheStatus = $"Missing (V{versionIndex})";
            }
        }
        else
        {
            row.JsonCacheStatus = "Missing (No Project Number)";
        }

        // 3.5 Template Compatibility Validation (read-only, no DB writes)
        if (templateMap != null && jsonCache != null && jsonCache.Sections.Count > 0)
        {
            var compatibility = ValidateTemplateCompatibility(jsonCache, templateMap);

            // Store for double-click access
            var compatKey = $"{row.ResolvedProjectNumber}|{versionIndex}|{row.ReportNumber}";
            _compatibilityResults[compatKey] = compatibility;

            row.TemplateMatchedNoteCount = compatibility.MatchedCount;
            row.TemplateMismatchCount = compatibility.MismatchCount;
            row.TemplateMissingSectionCount = compatibility.MissingCount;
            row.TemplateSkippedNoteCount = compatibility.MismatchCount + compatibility.MissingCount;
            row.TemplateWarnings = compatibility.BuildWarningsSummary();

            if (compatibility.MatchedCount > 0 && compatibility.MismatchCount == 0 && compatibility.MissingCount == 0)
            {
                row.TemplateValidationStatus = "FullMatch";
            }
            else if (compatibility.HasAnyMatch)
            {
                row.TemplateValidationStatus = "PartialMatch";
            }
            else
            {
                row.TemplateValidationStatus = "NoMatch";
            }
        }
        else if (templateMap != null && jsonCache == null)
        {
            // Template was provided but no JSON cache — cannot validate
            row.TemplateValidationStatus = "NotValidated";
        }
        // else: templateMap == null → no template provided → stays "NotValidated"

        // 4. Resolve Target Stage
        var targetStageCode = DetermineTargetStageCode(link.Status);
        if (string.IsNullOrWhiteSpace(targetStageCode))
        {
            row.TargetWorkflowStageDisplay = "Unknown Stage";
            row.Classification = MigrationPreviewClassification.MissingData;
            row.BlockingReason = $"Cannot determine workflow stage for status: {link.Status}";
            return row;
        }

        var targetStage = await db.WorkflowStageDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Code == targetStageCode && s.WorkflowDefinition.IsActive, ct);

        if (targetStage == null)
        {
            row.TargetWorkflowStageCode = targetStageCode;
            row.TargetWorkflowStageDisplay = targetStageCode;
            row.Classification = MigrationPreviewClassification.MissingData;
            row.BlockingReason = $"Target stage code '{targetStageCode}' not found in DB.";
            return row;
        }

        row.TargetWorkflowStageCode = targetStage.Code;
        row.TargetWorkflowStageDisplay = targetStage.Name;

        // 5. Existing Workflows
        var existingWorkflows = await _workflowQueryService.GetActiveByProjectAsync(resolvedId.Value, ct);
        var reviewWorkflows = existingWorkflows.Where(w => w.WorkflowDefinitionId == targetStage.WorkflowDefinitionId).ToList();

        if (reviewWorkflows.Count > 1)
        {
            row.ExistingWorkflowStatus = $"Multiple active ({reviewWorkflows.Count})";
            row.Classification = MigrationPreviewClassification.ExistingWorkflowConflict;
            row.BlockingReason = "Multiple active review workflows found.";
            return row;
        }

        var existingWorkflow = reviewWorkflows.FirstOrDefault();
        bool isWorkflowAlreadyDone = false;

        if (existingWorkflow == null)
        {
            row.ExistingWorkflowStatus = "None";
            // Workflow action is scoped to the project/review process — recorded here, surfaced in ProposedAction only on the latest version row
            row.ProposedWorkflowAction = $"Create workflow at stage: {row.TargetWorkflowStageDisplay}";
        }
        else
        {
            row.ExistingWorkflowStatus = $"Stage: {existingWorkflow.CurrentStage?.Name ?? "Unknown"}";

            if (existingWorkflow.CurrentStageId == targetStage.Id)
            {
                row.ProposedWorkflowAction = "Workflow already at target stage";
                isWorkflowAlreadyDone = true;
            }
            else if (existingWorkflow.CurrentStage?.SortOrder < targetStage.SortOrder)
            {
                var hasDirectTransition = await db.WorkflowTransitionRules.AsNoTracking()
                    .AnyAsync(r => r.FromStageId == existingWorkflow.CurrentStageId && r.ToStageId == targetStage.Id, ct);

                if (hasDirectTransition)
                {
                    row.ProposedWorkflowAction = $"Advance workflow to {row.TargetWorkflowStageDisplay} (Direct transition exists)";
                }
                else
                {
                    row.Classification = MigrationPreviewClassification.ManagerReview;
                    row.BlockingReason = "Workflow needs advancing, but no direct transition exists.";
                    return row;
                }
            }
            else
            {
                row.Classification = MigrationPreviewClassification.BackwardMovement;
                row.BlockingReason = "Existing workflow is ahead of target stage.";
                return row;
            }
        }

        // 6. Existing Reports
        bool isReportAlreadyDone = false;
        int.TryParse(link.ReportNumber, out var linkReportNumberInt);
        var existingReports = await db.InspectionReports.AsNoTracking()
            .Where(r => r.ProjectId == resolvedId.Value && r.ReportNumber == linkReportNumberInt)
            .ToListAsync(ct);

        string? effectiveSourceId = jsonCache?.ReportSpreadsheetId ?? reportSpreadsheetId;

        if (!string.IsNullOrWhiteSpace(effectiveSourceId))
        {
            var matchingReport = existingReports.FirstOrDefault(r => r.SentSpreadsheetId == effectiveSourceId);
            if (matchingReport != null)
            {
                row.ExistingReportStatus = $"Found matching report (ID: {matchingReport.ReportId})";
                isReportAlreadyDone = true;
            }
            else
            {
                // Check if there are other reports with a different SentSpreadsheetId
                var conflictReport = existingReports.FirstOrDefault(r => !string.IsNullOrEmpty(r.SentSpreadsheetId) && r.SentSpreadsheetId != effectiveSourceId);
                if (conflictReport != null)
                {
                    row.ExistingReportStatus = $"Conflict (ID: {conflictReport.ReportId} has different source)";
                    row.Classification = MigrationPreviewClassification.ExistingReportConflict;
                    row.BlockingReason = "Existing report has a different JSON source ID.";
                    return row;
                }
                
                // Check if there's a report with missing source ID but we can't reliably map it
                var unknownSourceReport = existingReports.FirstOrDefault(r => string.IsNullOrEmpty(r.SentSpreadsheetId));
                if (unknownSourceReport != null)
                {
                    row.ExistingReportStatus = $"Unlinked existing report (ID: {unknownSourceReport.ReportId})";
                    row.Classification = MigrationPreviewClassification.ExistingReportConflict;
                    row.BlockingReason = "Found existing report without SentSpreadsheetId link.";
                    return row;
                }

                row.ExistingReportStatus = "None (Will Import)";
            }
        }
        else
        {
            row.ExistingReportStatus = "No source ID to match against.";
        }

        // 7. Final Classification — workflow action and report action composed separately
        if (isWorkflowAlreadyDone && isReportAlreadyDone)
        {
            row.Classification = MigrationPreviewClassification.AlreadyDone;
            row.ProposedReportAction = "Nothing to do.";
            row.ProposedAction = "Nothing to do.";
        }
        else if (isWorkflowAlreadyDone && !isReportAlreadyDone && jsonCache != null)
        {
            // Workflow at target, JSON available — import report only
            row.ProposedReportAction = $"Import report V{versionIndex}";
            row.ProposedAction = row.ProposedReportAction;
            row.Classification = hasReviewerGroupWarning
                ? MigrationPreviewClassification.CommitReadyWithWarning
                : MigrationPreviewClassification.CommitReady;
            row.IsCommitAllowed = true;
        }
        else if (isWorkflowAlreadyDone)
        {
            // Workflow at target, no JSON and report not done — nothing to import
            row.ProposedReportAction = $"No JSON available for V{versionIndex}.";
            row.ProposedAction = $"Workflow already at target stage. No JSON available for V{versionIndex}.";
            row.Classification = MigrationPreviewClassification.CommitReadyWithWarning;
            row.IsCommitAllowed = false;
        }
        else if (isReportAlreadyDone)
        {
            // Workflow needed, report already done — workflow action only (on latest version row)
            row.ProposedReportAction = $"Report V{versionIndex} already done.";
            if (isLatestVersion)
            {
                row.ProposedAction = row.ProposedWorkflowAction + " (Report already done — workflow only)";
            }
            else
            {
                row.ProposedAction = $"Report V{versionIndex} already done. Workflow action handled on latest version row.";
            }
            row.Classification = hasReviewerGroupWarning
                ? MigrationPreviewClassification.CommitReadyWithWarning
                : MigrationPreviewClassification.CommitReady;
            row.IsCommitAllowed = true;
        }
        else if (jsonCache == null)
        {
            // Workflow needed, report missing, no JSON — workflow action only on latest version row
            row.ProposedReportAction = $"No JSON for V{versionIndex} — report import not possible.";
            if (isLatestVersion)
            {
                row.ProposedAction = row.ProposedWorkflowAction + $" (Workflow only; missing JSON for V{versionIndex})";
            }
            else
            {
                row.ProposedAction = $"Import V{versionIndex}: no JSON. Workflow action handled on latest version row.";
            }
            row.Classification = MigrationPreviewClassification.CommitReadyWithWarning;
            row.IsCommitAllowed = true;
        }
        else
        {
            // Workflow needed + JSON available — compose action depending on version scope
            row.ProposedReportAction = $"Import report V{versionIndex}";
            if (isLatestVersion)
            {
                // Latest version: show full workflow + report action
                row.ProposedAction = row.ProposedWorkflowAction + $" + Import report V{versionIndex}";
            }
            else
            {
                // Earlier version: report import only; workflow is handled on the latest version row
                row.ProposedAction = $"Import report V{versionIndex} only. Workflow action handled on latest version row.";
            }
            row.Classification = hasReviewerGroupWarning
                ? MigrationPreviewClassification.CommitReadyWithWarning
                : MigrationPreviewClassification.CommitReady;
            row.IsCommitAllowed = true;
        }

        return row;
    }

    /// <summary>
    /// Purely read-only project resolution against a pre-loaded project list.
    /// Strategies (in order of precedence):
    /// 1. Leading number from projectRef matches project.Number.
    /// 2. Project number appears inside projectRef at a word boundary.
    /// 3. Exact NameAndNumber match (case-insensitive).
    /// 4. Exact Title match (case-insensitive).
    /// </summary>
    private static (int? Id, string Number, string Name) ResolveProjectReadOnly(
        IReadOnlyList<Project> allProjects, string projectRef)
    {
        if (string.IsNullOrWhiteSpace(projectRef))
            return (null, string.Empty, string.Empty);

        Project? project = null;

        // Strategy 1: extract leading number from projectRef, match against project.Number
        var leadingMatch = System.Text.RegularExpressions.Regex.Match(projectRef.Trim(), @"^\d+");
        if (leadingMatch.Success)
        {
            var leadingStr = leadingMatch.Value;
            project = allProjects.FirstOrDefault(p =>
                p.Number.HasValue && p.Number.Value.ToString("0") == leadingStr);
        }

        // Strategy 2: project number appears inside projectRef using word-boundary regex
        // (handles e.g. "פרויקט 2774" where the number is not the leading token)
        if (project == null)
        {
            foreach (var p in allProjects)
            {
                if (!p.Number.HasValue) continue;
                var numStr = p.Number.Value.ToString("0");
                if (string.IsNullOrEmpty(numStr)) continue;
                var pattern = new System.Text.RegularExpressions.Regex(
                    @"(?<!\d)" + System.Text.RegularExpressions.Regex.Escape(numStr) + @"(?!\d)");
                if (pattern.IsMatch(projectRef))
                {
                    project = p;
                    break;
                }
            }
        }

        // Strategy 3: exact NameAndNumber match
        project ??= allProjects.FirstOrDefault(p =>
            !string.IsNullOrEmpty(p.NameAndNumber) &&
            p.NameAndNumber.Equals(projectRef, StringComparison.OrdinalIgnoreCase));

        // Strategy 4: exact Title match
        project ??= allProjects.FirstOrDefault(p =>
            !string.IsNullOrEmpty(p.Title) &&
            p.Title.Equals(projectRef, StringComparison.OrdinalIgnoreCase));

        if (project != null)
        {
            return (project.Id, project.Number?.ToString("0") ?? string.Empty,
                    project.NameAndNumber ?? project.Title ?? project.Id.ToString());
        }

        return (null, string.Empty, string.Empty);
    }

    /// <summary>
    /// Maps the Hebrew sheet status to the corresponding Workflow Stage Code.
    /// </summary>
    private static string? DetermineTargetStageCode(string? sheetStatus)
    {
        if (string.IsNullOrWhiteSpace(sheetStatus)) return null;
        
        var trimmed = sheetStatus.Trim();
        return trimmed switch
        {
            "בתהליך בדיקה" => "REV.ProfessionalReview",
            "נבדק- ממתין לבדיקה פנימית" => "REV.AwaitingManagerApproval",
            "ממתין לתיקון הערות" => "REV.AwaitingPlannerCorrections",
            "ממתין לתיקון הערות משטרה" => "REV.AwaitingPoliceCorrections",
            "בתהליך בדיקה הערות משטרה" => "REV.AwaitingPoliceApproval",
            "נבדק- ממתין לתשובה מהרשויות" => "REV.AwaitingPoliceApproval",
            "מאושר תנועתית" => "REV.Completed",
            "מאושר תנועתית לאחר משטרה" => "REV.Completed",
            _ => null
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Template Compatibility Validation (read-only, in-memory only)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates each JSON section against the target template sections (code + title).
    /// Returns a <see cref="TemplateCompatibilityResult"/> with per-section match entries.
    /// This is purely in-memory — no DB reads or writes.
    /// </summary>
    private static TemplateCompatibilityResult ValidateTemplateCompatibility(
        ExtractionCacheEnvelope jsonCache,
        Dictionary<string, TemplateSyncRow> templateMap)
    {
        var entries = new List<SectionCompatibilityEntry>();

        // Group JSON sections by parent code (X.Y) to avoid duplicate checks
        var jsonSectionsByParent = jsonCache.Sections
            .Where(s => !string.IsNullOrWhiteSpace(s.SectionCode))
            .GroupBy(s => ExtractParentSectionCode(s.SectionCode))
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .ToList();

        foreach (var group in jsonSectionsByParent)
        {
            var parentCode = group.Key!;
            var firstSection = group.First();

            // Get JSON section title — prefer ChapterTitle + SectionTitle, fall back to SectionTitle alone
            var jsonTitle = !string.IsNullOrWhiteSpace(firstSection.SectionTitle)
                ? firstSection.SectionTitle
                : firstSection.ChapterTitle;

            if (templateMap.TryGetValue(parentCode, out var templateRow))
            {
                // Section code found — compare titles
                var templateTitle = templateRow.SectionTitle ?? templateRow.SectionCode;

                if (AreTitlesCompatible(jsonTitle, templateTitle))
                {
                    entries.Add(new SectionCompatibilityEntry
                    {
                        SectionCode = parentCode,
                        JsonSectionTitle = jsonTitle ?? "(empty)",
                        TemplateSectionTitle = templateTitle,
                        MatchResult = SectionMatchResult.Matched,
                        Reason = "Section code and title match."
                    });
                }
                else
                {
                    entries.Add(new SectionCompatibilityEntry
                    {
                        SectionCode = parentCode,
                        JsonSectionTitle = jsonTitle ?? "(empty)",
                        TemplateSectionTitle = templateTitle,
                        MatchResult = SectionMatchResult.TitleMismatch,
                        Reason = $"JSON: \"{jsonTitle ?? "(empty)"}\" ≠ Template: \"{templateTitle}\""
                    });
                }
            }
            else
            {
                entries.Add(new SectionCompatibilityEntry
                {
                    SectionCode = parentCode,
                    JsonSectionTitle = jsonTitle ?? "(empty)",
                    TemplateSectionTitle = null,
                    MatchResult = SectionMatchResult.MissingInTemplate,
                    Reason = $"Section {parentCode} not found in target template."
                });
            }
        }

        return new TemplateCompatibilityResult { Entries = entries };
    }

    /// <summary>
    /// Extracts the parent section code "X.Y" from a full code like "X.Y.Z" or "X.Y".
    /// Returns null if the code cannot be parsed.
    /// </summary>
    internal static string? ExtractParentSectionCode(string sectionCode)
    {
        if (string.IsNullOrWhiteSpace(sectionCode)) return null;

        // Strip bidi marks and <<>> markers
        var cleaned = sectionCode
            .Replace("\u200F", "", StringComparison.Ordinal)
            .Replace("\u200E", "", StringComparison.Ordinal)
            .Replace("<<", "", StringComparison.Ordinal)
            .Replace(">>", "", StringComparison.Ordinal)
            .Trim();

        // Extract leading numeric part with dots
        var i = 0;
        while (i < cleaned.Length && (char.IsDigit(cleaned[i]) || cleaned[i] == '.'))
            i++;
        var numericPart = cleaned[..i].TrimEnd('.');

        // Split into parts and take the first two (X.Y)
        var parts = numericPart.Split('.');
        if (parts.Length >= 2)
            return $"{parts[0]}.{parts[1]}";
        if (parts.Length == 1 && parts[0].Length > 0)
            return parts[0]; // Single number like "3"

        return null;
    }

    /// <summary>
    /// Compares two section titles using normalized comparison.
    /// Normalization: trim, collapse whitespace, remove bidi marks, remove brackets/parentheses,
    /// case-insensitive (invariant culture).
    /// If either title is null/empty, considers it a match (no title to compare against).
    /// </summary>
    internal static bool AreTitlesCompatible(string? jsonTitle, string? templateTitle)
    {
        // If either side has no title, we cannot compare — treat as compatible
        if (string.IsNullOrWhiteSpace(jsonTitle) || string.IsNullOrWhiteSpace(templateTitle))
            return true;

        var normalizedJson = NormalizeSectionTitle(jsonTitle);
        var normalizedTemplate = NormalizeSectionTitle(templateTitle);

        // If normalization produced empty strings, treat as compatible
        if (string.IsNullOrWhiteSpace(normalizedJson) || string.IsNullOrWhiteSpace(normalizedTemplate))
            return true;

        // Check containment in both directions — handles cases where one title is a substring of the other
        // (e.g., "חניה" vs "3.6 חניה [גישה לחניות]")
        return normalizedJson.Contains(normalizedTemplate, StringComparison.OrdinalIgnoreCase)
            || normalizedTemplate.Contains(normalizedJson, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalizes a section title for comparison.
    /// Removes bidi marks, brackets, parentheses, <<>>, collapses whitespace, trims.
    /// </summary>
    internal static string NormalizeSectionTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;

        var result = title;

        // Remove bidi marks
        result = result.Replace("\u200F", "", StringComparison.Ordinal);
        result = result.Replace("\u200E", "", StringComparison.Ordinal);

        // Remove << and >> template markers
        result = result.Replace("<<", "", StringComparison.Ordinal);
        result = result.Replace(">>", "", StringComparison.Ordinal);

        // Remove content inside brackets [...] and parentheses (...) for comparison
        // But keep the text inside for containment check
        result = Regex.Replace(result, @"[\[\]\(\)]", " ");

        // Remove leading numeric code (e.g., "3.6 " prefix)
        result = Regex.Replace(result, @"^\d+(\.\d+)*\s*", "");

        // Collapse whitespace and trim
        result = Regex.Replace(result, @"\s+", " ").Trim();

        return result;
    }
}

