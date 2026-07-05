namespace SiNet.Application.Actions;

/// <summary>
/// Executes a single process action identified by <see cref="ActionCode"/>. Handlers live in
/// Infrastructure and must not expose SiNetSQL or WPF types through this contract.
/// </summary>
public interface IProcessActionHandler
{
    string ActionCode { get; }

    ValueTask<ActionExecutionResultDto> ExecuteAsync(
        ActionExecutionCommand command,
        CancellationToken cancellationToken = default);
}
