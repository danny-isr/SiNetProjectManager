namespace SiNet.Application.Workflow;

/// <summary>
/// Read-only progress snapshot for a workflow instance's current stage:
/// how many required/optional tasks the stage defines and how many have been
/// created/closed so far. Pure value DTO — no EF entities cross the boundary.
/// </summary>
/// <param name="TotalRequired">Number of required tasks defined for the current stage.</param>
/// <param name="CompletedRequired">Number of required tasks that have been completed.</param>
/// <param name="TotalOptional">Number of optional tasks defined for the current stage.</param>
/// <param name="TotalCreated">Total tasks created for the current stage (required + optional).</param>
/// <param name="TotalClosed">Total tasks closed for the current stage.</param>
public sealed record StageTaskProgressDto(
    int TotalRequired,
    int CompletedRequired,
    int TotalOptional,
    int TotalCreated,
    int TotalClosed)
{
    /// <summary>An empty progress snapshot (all counts zero).</summary>
    public static StageTaskProgressDto Empty { get; } = new(0, 0, 0, 0, 0);

    /// <summary>True when the stage defines required tasks and all of them are completed.</summary>
    public bool IsComplete => TotalRequired > 0 && CompletedRequired >= TotalRequired;
}
