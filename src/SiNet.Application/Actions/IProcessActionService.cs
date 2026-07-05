namespace SiNet.Application.Actions;

/// <summary>
/// Resolves and invokes the handler registered for a process action code. This is the sanctioned
/// Application port for domain actions; new Work Surfaces must not call legacy
/// <c>ProcessActionDispatcher</c> in SiNetSQL directly.
/// </summary>
public interface IProcessActionService
{
    bool HasHandler(string actionCode);

    ValueTask<ActionExecutionResultDto> DispatchAsync(
        ActionExecutionCommand command,
        CancellationToken cancellationToken = default);
}
