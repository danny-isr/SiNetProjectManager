using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SiNetProjectManagerV2.Services.Migration.Models;
using SiNetSQL.Data;
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
    private readonly WorkflowQueryService _workflowQueryService;
    private readonly GoogleAuthService _authService;

    public GoogleSheetReviewMigrationPreviewService(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        WorkflowQueryService workflowQueryService,
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
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        var reader = new IndexSheetReader(_authService);
        var links = await reader.ReadReportHyperlinksAsync(indexSheetId, log, ct, includeRowsWithoutLinks: true);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Pre-load all projects once for efficient in-memory matching across all rows
        var allProjects = await db.Projects.AsNoTracking().ToListAsync(ct);

        var results = new List<GoogleSheetReviewMigrationPreviewRow>();

        // Find duplicates in advance by ProjectRef
        var duplicateRefs = links
            .GroupBy(l => l.ProjectRef)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

        int rowIndex = 1;
        foreach (var link in links)
        {
            var isDuplicateProjectRow = duplicateRefs.Contains(link.ProjectRef);

            // Generate rows per version
            var versionCount = link.ReportSpreadsheetIds.Count;
            if (versionCount == 0)
            {
                var row = await ProcessSingleRowAsync(db, allProjects, link, rowIndex++, isDuplicateProjectRow, reviewerMapping, 1, null, ct);
                results.Add(row);
            }
            else
            {
                for (int i = 0; i < versionCount; i++)
                {
                    var spreadsheetId = link.ReportSpreadsheetIds[i];
                    var row = await ProcessSingleRowAsync(db, allProjects, link, rowIndex++, isDuplicateProjectRow, reviewerMapping, i + 1, spreadsheetId, ct);
                    results.Add(row);
                }
            }
        }

        return results;
    }

    private async Task<GoogleSheetReviewMigrationPreviewRow> ProcessSingleRowAsync(
        SiNetSQLDbContext db,
        IReadOnlyList<Project> allProjects,
        IndexSheetReportLink link,
        int rowIndex,
        bool isDuplicateProjectRow,
        IReadOnlyDictionary<string, int> reviewerMapping,
        int versionIndex,
        string? reportSpreadsheetId,
        CancellationToken ct)
    {
        var row = new GoogleSheetReviewMigrationPreviewRow
        {
            SheetRowIndex = rowIndex,
            ProjectNumberFromSheet = link.ProjectRef,
            ProjectNameFromSheet = link.ProjectRef,
            SheetStatus = link.Status ?? "Unknown",
            ReviewerNameFromSheet = link.Reviewer ?? "Unknown",
            IsDuplicateProjectRow = isDuplicateProjectRow
        };

        // 1. Resolve Project (Read Only) — in-memory, no DB queries
        var (resolvedId, resolvedNumber, resolvedName) = ResolveProjectReadOnly(allProjects, link.ProjectRef);
        row.ResolvedProjectId = resolvedId;
        row.ResolvedProjectDisplayName = resolvedName;
        
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
            row.ProposedAction = $"Create workflow at stage: {row.TargetWorkflowStageDisplay}";
        }
        else
        {
            row.ExistingWorkflowStatus = $"Stage: {existingWorkflow.CurrentStage?.Name ?? "Unknown"}";
            
            if (existingWorkflow.CurrentStageId == targetStage.Id)
            {
                row.ProposedAction = "Workflow already at target stage";
                isWorkflowAlreadyDone = true;
            }
            else if (existingWorkflow.CurrentStage?.SortOrder < targetStage.SortOrder)
            {
                var hasDirectTransition = await db.WorkflowTransitionRules.AsNoTracking()
                    .AnyAsync(r => r.FromStageId == existingWorkflow.CurrentStageId && r.ToStageId == targetStage.Id, ct);
                    
                if (hasDirectTransition)
                {
                    row.ProposedAction = $"Advance workflow to {row.TargetWorkflowStageDisplay} (Direct transition exists)";
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
        if (row.IsDuplicateProjectRow)
        {
            row.Classification = MigrationPreviewClassification.DuplicateProjectRow;
            row.BlockingReason = "Duplicate project row in sheet.";
        }
        else if (isWorkflowAlreadyDone && isReportAlreadyDone)
        {
            row.Classification = MigrationPreviewClassification.AlreadyDone;
            row.ProposedAction = "Nothing to do.";
        }
        else if (isWorkflowAlreadyDone && !isReportAlreadyDone && jsonCache != null)
        {
            // Workflow at target, JSON available — import report only
            row.ProposedAction = "Import report only (workflow already at target stage)";
            row.Classification = hasReviewerGroupWarning
                ? MigrationPreviewClassification.CommitReadyWithWarning
                : MigrationPreviewClassification.CommitReady;
            row.IsCommitAllowed = true;
        }
        else if (isWorkflowAlreadyDone)
        {
            // Workflow at target, no JSON and report not done — nothing to import
            row.Classification = MigrationPreviewClassification.CommitReadyWithWarning;
            row.ProposedAction = "Workflow already at target stage. No JSON available to import report.";
            row.IsCommitAllowed = false;
        }
        else if (isReportAlreadyDone)
        {
            // Workflow needed, report already done — create workflow only
            row.ProposedAction = row.ProposedAction + " (Report already done — workflow only)";
            row.Classification = hasReviewerGroupWarning
                ? MigrationPreviewClassification.CommitReadyWithWarning
                : MigrationPreviewClassification.CommitReady;
            row.IsCommitAllowed = true;
        }
        else if (jsonCache == null)
        {
            // Workflow needed, report missing, no JSON — create workflow only
            row.Classification = MigrationPreviewClassification.CommitReadyWithWarning;
            row.ProposedAction = row.ProposedAction + " (Workflow only; missing JSON)";
            row.IsCommitAllowed = true;
        }
        else
        {
            // Workflow needed + JSON available — create workflow + import report
            row.Classification = hasReviewerGroupWarning
                ? MigrationPreviewClassification.CommitReadyWithWarning
                : MigrationPreviewClassification.CommitReady;
            row.ProposedAction = row.ProposedAction + " + Import Report";
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
}
