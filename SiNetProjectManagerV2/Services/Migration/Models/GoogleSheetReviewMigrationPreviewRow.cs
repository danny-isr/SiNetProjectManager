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
    
    public string JsonCacheStatus { get; set; } = string.Empty;
    public string JsonPath { get; set; } = string.Empty;
    public string JsonReportSpreadsheetId { get; set; } = string.Empty;
    
    public string ExistingReportStatus { get; set; } = string.Empty;
    public string ExistingWorkflowStatus { get; set; } = string.Empty;
    
    public MigrationPreviewClassification Classification { get; set; }
    public string BlockingReason { get; set; } = string.Empty;
    public string WarningMessages { get; set; } = string.Empty;
    public string ProposedAction { get; set; } = string.Empty;
    public bool IsCommitAllowed { get; set; }
}
