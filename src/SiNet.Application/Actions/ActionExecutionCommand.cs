namespace SiNet.Application.Actions;

/// <summary>
/// Input for <see cref="IProcessActionService.DispatchAsync"/>. Pure Application contract — no EF,
/// no legacy ViewModels, no WPF types.
/// </summary>
public sealed record ActionExecutionCommand(
    string ActionCode,
    int? ProjectId = null,
    int? WorkflowInstanceId = null,
    int? TaskId = null,
    int? UserId = null,
    IReadOnlyDictionary<string, object?>? Data = null);
