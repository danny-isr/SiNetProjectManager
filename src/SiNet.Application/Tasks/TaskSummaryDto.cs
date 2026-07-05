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
    string? LastTaskResultCode,
    string? Title,
    string? ComponentKey);
