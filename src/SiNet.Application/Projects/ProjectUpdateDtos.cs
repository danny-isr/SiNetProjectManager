namespace SiNet.Application.Projects;

/// <summary>One job-type line on the project edit / create surfaces.</summary>
public sealed record ProjectJobTypeEditLine(
    int JobTypeId,
    string JobTypeTitle,
    bool IsSelected,
    int? AdminWorkerId,
    decimal BidValue);

/// <summary>Full editable snapshot for «עדכון פרויקט».</summary>
public sealed record ProjectEditDto(
    int ProjectId,
    string ProjectNumberDisplay,
    string Title,
    string? NameAndNumber,
    int? PlaceId,
    int? CompanyId,
    int? ContactId,
    int? ParentProjectId,
    int? ProjectStatusId,
    string? ApproveDescription,
    IReadOnlyList<ProjectJobTypeEditLine> JobTypes);

/// <summary>Command to persist metadata + job types (number is immutable).</summary>
public sealed record UpdateProjectCommand(
    int ProjectId,
    int PlaceId,
    int CompanyId,
    int ContactId,
    int? ParentProjectId,
    int? ProjectStatusId,
    string? ApproveDescription,
    IReadOnlyList<ProjectJobTypeEditLine> JobTypes);

/// <summary>
/// Open workflow track that would become orphaned if its JobType is removed from the project.
/// </summary>
public sealed record ProjectJobTypeRemovalRiskDto(
    int WorkflowInstanceId,
    int JobTypeId,
    string? JobTypeTitle,
    string WorkflowName,
    string StatusLabel);

public sealed record UpdateProjectResult(
    bool Succeeded,
    string? ErrorMessage = null)
{
    public static UpdateProjectResult Ok() => new(true);

    public static UpdateProjectResult Fail(string errorMessage) =>
        new(false, errorMessage);
}

public enum ProjectRenameStepKind
{
    FileServer,
    AccDocs,
    GoogleDrive,
    Database,
}

public enum ProjectRenameStepStatus
{
    Pending,
    Succeeded,
    Failed,
    Skipped,
}

public sealed record ProjectRenameStepPlan(
    ProjectRenameStepKind Kind,
    string Description,
    string? SourcePathOrId,
    string? TargetPathOrName);

public sealed record ProjectRenameAnalysis(
    int ProjectId,
    string CurrentTitle,
    string DesiredTitle,
    string CurrentNameAndNumber,
    string PredictedNameAndNumber,
    bool CanExecute,
    string? ReasonIfCannot,
    IReadOnlyList<ProjectRenameStepPlan> Steps);

public sealed record ProjectRenameStepResult(
    ProjectRenameStepKind Kind,
    ProjectRenameStepStatus Status,
    string Message);

public sealed record ProjectRenameExecuteResult(
    bool Succeeded,
    IReadOnlyList<ProjectRenameStepResult> Steps,
    string? ErrorMessage = null);

public enum ProjectGmailLabelSyncAction
{
    Unchanged,
    Renamed,
    NeedsUserDecision,
    Failed,
}

public sealed record ProjectGmailLabelSyncItem(
    string LabelId,
    string CurrentFullPath,
    string LeafName,
    int ProjectNumber,
    string? ExpectedLeafName,
    ProjectGmailLabelSyncAction Action,
    string? Message);

public sealed record ProjectGmailLabelSyncResult(
    bool SettingEnabled,
    int ExaminedCount,
    int RenamedCount,
    IReadOnlyList<ProjectGmailLabelSyncItem> Items,
    IReadOnlyList<ProjectGmailLabelSyncItem> NeedsUserDecision);

/// <summary>Outcome of keeping one leaf and deleting sibling duplicates for the same <c>(Number)</c>.</summary>
public sealed record ProjectGmailLabelDuplicateResolveResult(
    int ProjectNumber,
    string KeptLabelId,
    int DeletedCount,
    IReadOnlyList<string> Errors);
