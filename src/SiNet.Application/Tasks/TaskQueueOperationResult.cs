namespace SiNet.Application.Tasks;

/// <summary>Result of a single queue mutation (move, reassign, bucket change).</summary>
public sealed record TaskQueueOperationResult(
    bool Succeeded,
    string Message,
    int? TaskId = null,
    int? OldUserId = null,
    int? NewUserId = null,
    int? OldBucket = null,
    int? NewBucket = null,
    int? OldPriority = null,
    int? NewPriority = null);
