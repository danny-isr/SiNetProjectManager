namespace SiNet.Application.Actions;

/// <summary>Outcome of <see cref="IProcessActionService.DispatchAsync"/>.</summary>
public sealed record ActionExecutionResultDto(
    string ActionCode,
    ActionExecutionStatus Status,
    string? Outcome = null,
    string? Message = null)
{
    public static ActionExecutionResultDto Completed(string actionCode, string? message = null, string? outcome = null) =>
        new(actionCode, ActionExecutionStatus.Completed, outcome ?? "Succeeded", message);

    public static ActionExecutionResultDto Failed(string actionCode, string message) =>
        new(actionCode, ActionExecutionStatus.Failed, "Failed", message);

    public static ActionExecutionResultDto NotSupported(string actionCode) =>
        new(actionCode, ActionExecutionStatus.NotSupported, Message: $"No handler registered for action code '{actionCode}'.");
}
