namespace SiNet.Application.Tasks;

/// <summary>Aggregate result of queue repair for one or more assignee+bucket pairs.</summary>
public sealed record TaskQueueRepairResult(
    int UsersProcessed,
    int BucketsProcessed,
    int TasksAssignedPriority,
    int DuplicatePrioritiesFixed,
    int NullPrioritiesFixed,
    int GapsClosed,
    IReadOnlyList<string> Errors)
{
    public static TaskQueueRepairResult Empty { get; } =
        new(0, 0, 0, 0, 0, 0, []);

    public TaskQueueRepairResult Merge(TaskQueueRepairResult other) =>
        new(
            UsersProcessed + other.UsersProcessed,
            BucketsProcessed + other.BucketsProcessed,
            TasksAssignedPriority + other.TasksAssignedPriority,
            DuplicatePrioritiesFixed + other.DuplicatePrioritiesFixed,
            NullPrioritiesFixed + other.NullPrioritiesFixed,
            GapsClosed + other.GapsClosed,
            Errors.Concat(other.Errors).ToList());
}
