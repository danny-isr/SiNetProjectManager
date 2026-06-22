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
    private readonly IGoogleAuthService _authService;

    public GoogleSheetReviewMigrationPreviewService(
        IDbContextFactory<SiNetSQLDbContext> dbFactory,
        WorkflowQueryService workflowQueryService,
        IGoogleAuthService authService)
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
        
        // Track seen projects to detect duplicates
        var seenProjectIds = new HashSet<int>();

        int rowIndex = 1; // Assuming 1-based indexing for display
        foreach (var link in links)
        {
            var row = new GoogleSheetReviewMigrationPreviewRow
            {
                SheetRowIndex = rowIndex++,
                ProjectNumberFromSheet = link.ProjectRef,
                ProjectNameFromSheet = link.ProjectRef,
                SheetStatus = link.Status ?? "Unknown",
                ReviewerNameFromSheet = link.Reviewer ?? "Unknown"
            };

            // 1. Resolve Project (Read Only)
            var (resolvedId, resolvedName) = await ResolveProjectReadOnlyAsync(db, link.ProjectRef, ct);
            row.ResolvedProjectId = resolvedId;
            row.ResolvedProjectDisplayName = resolvedName;
            
            if (resolvedId.HasValue)
            {
                row.ProjectMatchStatus = "Found";
                if (!seenProjectIds.Add(resolvedId.Value))
                {
                    row.IsDuplicateProjectRow = true;
                }
            }
            else
            {
                row.ProjectMatchStatus = "Not Found";
                row.Classification = MigrationPreviewClassification.NoMatch;
                row.BlockingReason = "Project not found in DB.";
                results.Add(row);
                continue;
            }

            // 2. Reviewer Mapping
            if (reviewerMapping.TryGetValue(row.ReviewerNameFromSheet, out var mappedUserId))
            {
                row.MappedReviewerUserId = mappedUserId;
                var user = await db.Siusers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == mappedUserId, ct);
                row.MappedReviewerDisplayName = user?.DisplayName ?? user?.Name;
                row.ReviewerMappingStatus = "Mapped";
                
                // Group validation would go here. Since we can't easily validate workflow stage group without resolving workflow first,
                // we will flag as "Group validation not verified in Phase 1"
                row.WarningMessages = "Reviewer mapped — group validation not verified in Phase 1";
            }
            else
            {
                row.ReviewerMappingStatus = "Not Mapped";
                row.Classification = MigrationPreviewClassification.ReviewerNotMapped;
                row.BlockingReason = "Reviewer mapping required.";
                results.Add(row);
                continue;
            }

            // 3. JSON Cache
            var jsonCache = await ExtractionCacheService.LoadAsync(resolvedId.Value.ToString(), 1, link.ReportNumber, ct);
            if (jsonCache != null)
            {
                row.JsonCacheStatus = "Found";
                row.JsonReportSpreadsheetId = jsonCache.ReportSpreadsheetId;
                row.JsonPath = ExtractionCacheService.GetProjectCacheFolder(resolvedId.Value.ToString());
            }
            else
            {
                row.JsonCacheStatus = "Missing";
                // JSON is missing. This might be just a workflow start without a report.
            }

            // 4. Resolve Existing Workflows and Target Stage
            // We need to determine the target stage based on the Sheet Status.
            // For PoC, let's assume we want to start a default Review workflow.
            var targetStageId = await DetermineTargetStageIdAsync(db, link.Status, ct);
            if (!targetStageId.HasValue)
            {
                row.TargetWorkflowStageDisplay = "Unknown Stage";
                row.Classification = MigrationPreviewClassification.MissingData;
                row.BlockingReason = $"Cannot determine workflow stage for status: {link.Status}";
                results.Add(row);
                continue;
            }

            var targetStage = await db.WorkflowStageDefinitions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == targetStageId.Value, ct);
            row.TargetWorkflowStageCode = targetStage?.Code ?? "";
            row.TargetWorkflowStageDisplay = targetStage?.Name ?? "";

            var existingWorkflows = await _workflowQueryService.GetActiveWorkflowsForProjectAsync(resolvedId.Value, ct);
            var reviewWorkflows = existingWorkflows.Where(w => w.WorkflowDefinitionId == targetStage?.WorkflowDefinitionId).ToList();

            if (reviewWorkflows.Count > 1)
            {
                row.ExistingWorkflowStatus = $"Multiple active ({reviewWorkflows.Count})";
                row.Classification = MigrationPreviewClassification.ExistingWorkflowConflict;
                row.BlockingReason = "Multiple active review workflows found.";
                results.Add(row);
                continue;
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
                
                if (existingWorkflow.CurrentStageId == targetStageId.Value)
                {
                    row.ProposedAction = "Workflow already at target stage";
                    isWorkflowAlreadyDone = true;
                }
                else if (targetStage != null && existingWorkflow.CurrentStage?.SortOrder < targetStage.SortOrder)
                {
                    // Existing workflow is behind target stage.
                    // Check if direct transition exists.
                    var hasDirectTransition = await db.WorkflowTransitionRules.AsNoTracking()
                        .AnyAsync(r => r.FromStageId == existingWorkflow.CurrentStageId && r.ToStageId == targetStageId.Value, ct);
                        
                    if (hasDirectTransition)
                    {
                        row.ProposedAction = $"Advance workflow to {row.TargetWorkflowStageDisplay} (Direct transition exists)";
                    }
                    else
                    {
                        row.Classification = MigrationPreviewClassification.ManagerReview;
                        row.BlockingReason = "Workflow needs advancing, but no direct transition exists.";
                        results.Add(row);
                        continue;
                    }
                }
                else
                {
                    // Existing workflow is ahead or parallel.
                    row.Classification = MigrationPreviewClassification.BackwardMovement;
                    row.BlockingReason = "Existing workflow is ahead of target stage.";
                    results.Add(row);
                    continue;
                }
            }

            // 5. Resolve Existing Reports
            bool isReportAlreadyDone = false;
            var existingReports = await db.InspectionReports.AsNoTracking()
                .Where(r => r.ProjectId == resolvedId.Value)
                .ToListAsync(ct);

            if (jsonCache != null)
            {
                var matchingReport = existingReports.FirstOrDefault(r => r.SentSpreadsheetId == jsonCache.ReportSpreadsheetId);
                if (matchingReport != null)
                {
                    row.ExistingReportStatus = $"Found matching report (ID: {matchingReport.Id})";
                    isReportAlreadyDone = true;
                }
                else
                {
                    row.ExistingReportStatus = $"No matching SentSpreadsheetId (Found {existingReports.Count} other reports)";
                    row.Classification = MigrationPreviewClassification.ExistingReportConflict;
                    row.BlockingReason = "Existing reports do not match JSON source ID.";
                    results.Add(row);
                    continue;
                }
            }
            else
            {
                row.ExistingReportStatus = "No JSON to compare against.";
            }

            // 6. Final Classification
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

            results.Add(row);
        }

        return results;
    }

    /// <summary>
    /// Purely read-only project resolution logic.
    /// Extracts leading digits or matches exact Title/NameAndNumber.
    /// </summary>
    private static async Task<(int? Id, string Name)> ResolveProjectReadOnlyAsync(
        SiNetSQLDbContext context, string projectRef, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectRef))
            return (null, string.Empty);

        Project? project = null;

        // Try as numeric ID
        var match = System.Text.RegularExpressions.Regex.Match(projectRef.Trim(), @"^\d+");
        if (match.Success && int.TryParse(match.Value, out var numericId))
        {
            project = await context.Projects.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == numericId || p.Number == numericId, ct);
        }

        // Try by Title (exact)
        project ??= await context.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Title != null && p.Title == projectRef, ct);

        // Try by NameAndNumber (exact)
        project ??= await context.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.NameAndNumber != null && p.NameAndNumber == projectRef, ct);

        if (project != null)
        {
            return (project.Id, project.NameAndNumber ?? project.Title ?? project.Name ?? project.Id.ToString());
        }

        return (null, string.Empty);
    }

    /// <summary>
    /// Dummy implementation for determining target stage ID based on sheet status.
    /// In a real scenario, this would map the Hebrew status to a WorkflowStageDefinition.
    /// </summary>
    private async Task<int?> DetermineTargetStageIdAsync(SiNetSQLDbContext db, string sheetStatus, CancellationToken ct)
    {
        // For PoC, just find the first active stage in any active Review workflow.
        // In real implementation, you'd map the string `sheetStatus` to the correct stage Code.
        var stage = await db.WorkflowStageDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.IsActive && s.WorkflowDefinition.IsActive, ct);
            
        return stage?.Id;
    }
}
