using SiNet.Application.Actions;

namespace SiNet.Infrastructure.Sql.Services.Actions;

/// <summary>
/// Native Infrastructure.Sql dispatcher for <see cref="IProcessActionService"/>. Replaces direct
/// dependency on legacy SiNetSQL <c>ProcessActionDispatcher</c> for new Work Surfaces.
/// </summary>
public sealed class ProcessActionService : IProcessActionService
{
    private readonly IReadOnlyDictionary<string, IProcessActionHandler> _handlers;

    public ProcessActionService(IEnumerable<IProcessActionHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);

        var map = new Dictionary<string, IProcessActionHandler>(StringComparer.Ordinal);
        foreach (var handler in handlers)
        {
            if (handler is null || string.IsNullOrWhiteSpace(handler.ActionCode))
                continue;

            if (map.ContainsKey(handler.ActionCode))
            {
                throw new InvalidOperationException(
                    $"Duplicate IProcessActionHandler registration for ActionCode '{handler.ActionCode}'.");
            }

            map.Add(handler.ActionCode, handler);
        }

        _handlers = map;
    }

    public bool HasHandler(string actionCode)
        => !string.IsNullOrEmpty(actionCode) && _handlers.ContainsKey(actionCode);

    public ValueTask<ActionExecutionResultDto> DispatchAsync(
        ActionExecutionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.ActionCode))
        {
            return ValueTask.FromResult(
                ActionExecutionResultDto.Failed(command.ActionCode ?? string.Empty, "ActionCode is required."));
        }

        if (!_handlers.TryGetValue(command.ActionCode, out var handler))
        {
            return ValueTask.FromResult(ActionExecutionResultDto.NotSupported(command.ActionCode));
        }

        return handler.ExecuteAsync(command, cancellationToken);
    }
}
