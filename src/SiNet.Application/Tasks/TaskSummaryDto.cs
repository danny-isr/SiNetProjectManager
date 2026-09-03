namespace SiNet.Application.Tasks;

/// <summary>
/// Read-only task row for lists and shell panels. No EF entities cross this boundary.
/// </summary>
public sealed record TaskSummaryDto(
    int TaskId,
    int? ProjectId,
    string? TaskTypeCode,
    string? TaskTypeName,
    string? StatusCode,
    string? StatusName,
    bool IsOpen,
    int? AssignedToUserId,
    string? AssignedToUserName,
    int WorkQueueBucket,
    string WorkQueueBucketCode,
    string WorkQueueBucketDisplayName,
    int? WorkPriority,
    DateTime? DueDate,
    /// <summary>When the task row was created/opened (<c>ProjectAssignment.Created</c>), UTC when stored as UTC.</summary>
    DateTime? CreatedAt,
    string? LastTaskResultCode,
    string? Title,
    string? ComponentKey,
    string? WorkflowDefinitionName = null,
    string? JobTypeTitle = null,
    string? CurrentStageName = null,
    /// <summary>Preformatted process · track · stage line for list cards (null when unknown).</summary>
    string? TrackDisplayLine = null)
{
    public override string ToString() =>
        string.IsNullOrWhiteSpace(Title) ? $"Task {TaskId}" : Title;
}
