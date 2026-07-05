namespace SiNet.Application.Tasks;

/// <summary>Input for creating a task through the Task Workbench.</summary>
public sealed record CreateTaskRequest(
    int ProjectId,
    int AssignedToUserId,
    int TaskTypeId,
    int StatusId,
    string Title,
    int WorkQueueBucket,
    DateTime? DueDate = null,
    string? Body = null);
