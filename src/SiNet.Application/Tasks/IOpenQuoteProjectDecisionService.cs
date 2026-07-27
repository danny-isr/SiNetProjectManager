namespace SiNet.Application.Tasks;

/// <summary>
/// Application command for recording a decision made by the Open Quote Project surface.
/// </summary>
public interface IOpenQuoteProjectDecisionService
{
    ValueTask<TaskCompletionResultDto> CompleteDecisionAsync(
        OpenQuoteProjectDecisionCommand command,
        CancellationToken ct);
}

/// <summary>Input for completing the workflow task after a quote-project decision.</summary>
public sealed record OpenQuoteProjectDecisionCommand(
    int TaskId,
    int ActingUserId,
    string EventCode,
    string ResultCode);

/// <summary>
/// Coordinates the surface decision through the existing task-completion port; it does not own
/// workflow progression directly.
/// </summary>
public sealed class OpenQuoteProjectDecisionService(ITaskCompletionService taskCompletion)
    : IOpenQuoteProjectDecisionService
{
    private readonly ITaskCompletionService _taskCompletion =
        taskCompletion ?? throw new ArgumentNullException(nameof(taskCompletion));

    /// <inheritdoc />
    public ValueTask<TaskCompletionResultDto> CompleteDecisionAsync(
        OpenQuoteProjectDecisionCommand command,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        return _taskCompletion.CompleteAsync(
            new CompleteTaskCommand(
                command.TaskId,
                command.EventCode,
                command.ResultCode,
                CompletedTaskLinkIds: null,
                command.ActingUserId),
            ct);
    }
}
