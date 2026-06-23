namespace SiNetProjectManagerV2.Services.Migration.Models;

public class GoogleSheetReviewMigrationPreviewRow
{
    public int SheetRowIndex { get; set; }
    public string ProjectNumberFromSheet { get; set; } = string.Empty;
    public string ProjectNameFromSheet { get; set; } = string.Empty;
    public int? ResolvedProjectId { get; set; }
    public string? ResolvedProjectDisplayName { get; set; }
    public string ProjectMatchStatus { get; set; } = string.Empty;
    public bool IsDuplicateProjectRow { get; set; }
    public string SheetStatus { get; set; } = string.Empty;
    
    public string TargetWorkflowStageCode { get; set; } = string.Empty;
    public string TargetWorkflowStageDisplay { get; set; } = string.Empty;
    
    public string ReviewerNameFromSheet { get; set; } = string.Empty;
    public int? MappedReviewerUserId { get; set; }
    public string? MappedReviewerDisplayName { get; set; }
    public string ReviewerMappingStatus { get; set; } = string.Empty;
    
    /// <summary>Report number from the index sheet (e.g. "1", "2"). Used with ResolvedProjectNumber
    /// and VersionIndex to locate the JSON cache via ExtractionCacheService.LoadAsync.</summary>
    public string ReportNumber { get; set; } = string.Empty;

    /// <summary>The resolved project number (numeric key used as the cache folder name).
    /// Populated only when the project is resolved. Used for JSON cache lookup.</summary>
    public string ResolvedProjectNumber { get; set; } = string.Empty;

    public string JsonCacheStatus { get; set; } = string.Empty;
    public string JsonPath { get; set; } = string.Empty;
    public string JsonReportSpreadsheetId { get; set; } = string.Empty;
    
    public string ExistingReportStatus { get; set; } = string.Empty;
    public string ExistingWorkflowStatus { get; set; } = string.Empty;
    
    /// <summary>1-based index of this version row within its sheet row group (1 = V1, 2 = V2, …).</summary>
    public int VersionIndex { get; set; } = 1;

    /// <summary>
    /// True when this is the latest (highest-index) version row for the sheet row.
    /// The workflow action is proposed only on this row; earlier version rows carry a report-only action.
    /// </summary>
    public bool IsLatestVersion { get; set; } = true;

    /// <summary>Workflow-scoped action (once per project/review process).</summary>
    public string ProposedWorkflowAction { get; set; } = string.Empty;

    /// <summary>Report-scoped action (per report version).</summary>
    public string ProposedReportAction { get; set; } = string.Empty;

    public MigrationPreviewClassification Classification { get; set; }
    public string BlockingReason { get; set; } = string.Empty;
    public string WarningMessages { get; set; } = string.Empty;
    /// <summary>Human-readable combined action shown in the preview grid.</summary>
    public string ProposedAction { get; set; } = string.Empty;
    public bool IsCommitAllowed { get; set; }
}
