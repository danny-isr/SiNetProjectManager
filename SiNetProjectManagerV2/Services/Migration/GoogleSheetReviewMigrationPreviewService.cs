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
        var reader = new IndexSheetReader(_authService);
        var links = await reader.ReadReportHyperlinksAsync(indexSheetId, log);

        return links
            .Select(l => l.Reviewer?.Trim())
            .Where(r => !string.IsNullOrEmpty(r))
            .Distinct()
            .OrderBy(r => r)
            .ToList()!;
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
        var links = await reader.ReadReportHyperlinksAsync(indexSheetId, log);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        
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
                var row = await ProcessSingleRowAsync(db, link, rowIndex++, isDuplicateProjectRow, reviewerMapping, 1, null, ct);
                results.Add(row);
            }
            else
            {
                for (int i = 0; i < versionCount; i++)
                {
                    var spreadsheetId = link.ReportSpreadsheetIds[i];
                    var row = await ProcessSingleRowAsync(db, link, rowIndex++, isDuplicateProjectRow, reviewerMapping, i + 1, spreadsheetId, ct);
                    results.Add(row);
                }
            }
        }

        return results;
    }

    private async Task<GoogleSheetReviewMigrationPreviewRow> ProcessSingleRowAsync(
        SiNetSQLDbContext db,
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

        // 1. Resolve Project (Read Only)
        var (resolvedId, resolvedNumber, resolvedName) = await ResolveProjectReadOnlyAsync(db, link.ProjectRef, ct);
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
        if (reviewerMapping.TryGetValue(row.ReviewerNameFromSheet, out var mappedUserId))
        {
            row.MappedReviewerUserId = mappedUserId;
            var user = await db.Siusers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == mappedUserId, ct);
            row.MappedReviewerDisplayName = user?.Name;
            row.ReviewerMappingStatus = "Mapped";
            row.WarningMessages = "Reviewer mapped — group validation not verified in Phase 1";
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
                    row.WarningMessages += " | JSON Source ID mismatch with sheet link";
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

        // 7. Final Classification
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
        else if (isWorkflowAlreadyDone && jsonCache == null)
        {
            row.Classification = MigrationPreviewClassification.AlreadyDone;
            row.ProposedAction = "Workflow is already at target stage. No JSON available to import report.";
        }
        else if (jsonCache == null)
        {
            row.Classification = MigrationPreviewClassification.CommitReadyWithWarning;
            row.ProposedAction = row.ProposedAction + " (Workflow only; missing JSON)";
            row.IsCommitAllowed = true;
        }
        else
        {
            row.Classification = MigrationPreviewClassification.CommitReady;
            row.ProposedAction = row.ProposedAction + " + Import Report";
            row.IsCommitAllowed = true;
        }

        return row;
    }

    /// <summary>
    /// Purely read-only project resolution logic.
    /// </summary>
    private static async Task<(int? Id, string Number, string Name)> ResolveProjectReadOnlyAsync(
        SiNetSQLDbContext context, string projectRef, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectRef))
            return (null, string.Empty, string.Empty);

        Project? project = null;

        var match = System.Text.RegularExpressions.Regex.Match(projectRef.Trim(), @"^\d+");
        if (match.Success && int.TryParse(match.Value, out var numericId))
        {
            project = await context.Projects.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == numericId || p.Number == numericId, ct);
        }

        project ??= await context.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Title != null && p.Title == projectRef, ct);

        project ??= await context.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.NameAndNumber != null && p.NameAndNumber == projectRef, ct);

        if (project != null)
        {
            return (project.Id, project.Number?.ToString("0") ?? string.Empty, project.NameAndNumber ?? project.Title ?? project.Id.ToString());
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
