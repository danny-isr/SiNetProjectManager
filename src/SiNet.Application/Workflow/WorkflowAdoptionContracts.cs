namespace SiNet.Application.Workflow;

public enum WorkflowAdoptionDisposition
{
    ReadyToAdopt,
    BlockedAlreadyActive,
    BlockedAlreadyPaused,
    RequiresReviewCompletedExists,
    RequiresReviewExistingWorkflow,
    BlockedBackwardMovement,
    BlockedConflict,
    BlockedInvalidStage,
    BlockedNoAssignee,
    BlockedResponsibleUser,
    BlockedNotAllowed,
    AlreadyUpToDate,
    AlreadyExists,
    Committed,
}

public enum WorkflowAdoptionReportMode
{
    Historical,
    Active,
}

public sealed record WorkflowAdoptionReportIntent(int ReportId, WorkflowAdoptionReportMode Mode);

/// <summary>
/// Adopt an existing business process directly at its real current stage.
/// <paramref name="JobTypeId"/> is required. There is no null-JobType adoption.
/// </summary>
public sealed record WorkflowAdoptionRequest(
    int ProjectId,
    int WorkflowDefinitionId,
    int JobTypeId,
    string CurrentStageCode,
    int UserId,
    DateTime? OriginalStartedAt = null,
    string? Notes = null,
    int? ResponsibleUserId = null,
    IReadOnlyList<WorkflowAdoptionReportIntent>? Reports = null);

public sealed record WorkflowAdoptionHistoricalStage(string Code, string? Name, int SortOrder);

public sealed record WorkflowAdoptionTaskPreview(string? TaskTypeCode, string? TaskTypeName);

public sealed record WorkflowAdoptionSkippedAction(string FromStageCode, string ToStageCode, string ActionType);

public sealed record WorkflowAdoptionReportPreview(
    int ReportId,
    int ReportNumber,
    WorkflowAdoptionReportMode? RequestedMode,
    bool IsLockedAfterSend,
    bool HasExportSnapshot,
    string Note,
    int? SeriesId = null,
    string? SeriesName = null)
{
    public string DisplayLabel =>
        !string.IsNullOrWhiteSpace(SeriesName)
            ? $"{SeriesName.Trim()} — Report {ReportNumber}"
            : SeriesId is int seriesId
                ? $"Series #{seriesId} — Report {ReportNumber}"
                : $"Report {ReportNumber}";
}

public sealed record WorkflowAdoptionUserOption(int UserId, string? Name);

public sealed record WorkflowAdoptionPreview(
    WorkflowAdoptionDisposition Disposition,
    bool CanCommit,
    string Message,
    int ProjectId,
    string? ProjectTitle,
    float? ProjectNumber,
    string? ProjectStatusCode,
    int JobTypeId,
    string? JobTypeTitle,
    int WorkflowDefinitionId,
    string? WorkflowCode,
    string? WorkflowName,
    string? CurrentStageCode,
    string? CurrentStageName,
    int? CurrentStageSortOrder,
    string? CurrentStageNodeType,
    bool? CurrentStageIsFinal,
    int? ExistingInstanceId,
    string? ExistingStageCode,
    IReadOnlyList<WorkflowAdoptionHistoricalStage> HistoricalStages,
    IReadOnlyList<WorkflowAdoptionTaskPreview> WillCreateTasks,
    IReadOnlyList<string> WillNotCreate,
    IReadOnlyList<WorkflowAdoptionSkippedAction> WillNotRunActions,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<WorkflowAdoptionReportPreview> Reports,
    int? ResolvedAssigneeId);

public sealed record WorkflowAdoptionCommitResult(
    WorkflowAdoptionDisposition Disposition,
    string Message,
    int? WorkflowInstanceId,
    int? CreatedTaskId,
    int? ActiveReportLinkId,
    IReadOnlyList<string> Warnings);

public sealed record WorkflowAdoptionStageOption(
    string Code,
    string Name,
    int SortOrder,
    IReadOnlyList<WorkflowAdoptionUserOption> ResponsibleCandidates);

public sealed record WorkflowAdoptionJobTypeOption(
    int JobTypeId,
    string? Title,
    IReadOnlyList<WorkflowAdoptionStageOption> Stages);

public sealed record WorkflowAdoptionWorkflowOption(
    int DefinitionId,
    string? Code,
    string? Name,
    IReadOnlyList<WorkflowAdoptionJobTypeOption> JobTypes);

public sealed record WorkflowAdoptionOptions(
    int ProjectId,
    string? ProjectTitle,
    IReadOnlyList<WorkflowAdoptionWorkflowOption> Workflows,
    IReadOnlyList<WorkflowAdoptionReportPreview> ExistingReports,
    string? Message);

public interface IWorkflowAdoptionService
{
    ValueTask<WorkflowAdoptionOptions> GetOptionsAsync(int projectId, CancellationToken ct);

    ValueTask<WorkflowAdoptionPreview> PreviewAsync(WorkflowAdoptionRequest request, CancellationToken ct);

    ValueTask<WorkflowAdoptionCommitResult> CommitAsync(WorkflowAdoptionRequest request, CancellationToken ct);
}
