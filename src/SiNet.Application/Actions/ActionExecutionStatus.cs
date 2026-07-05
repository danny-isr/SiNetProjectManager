namespace SiNet.Application.Actions;

/// <summary>Outcome status for <see cref="IProcessActionService.DispatchAsync"/>.</summary>
public enum ActionExecutionStatus
{
    Completed = 0,
    Failed = 1,
    NoOp = 2,
    NotSupported = 3,
    Deferred = 4,
}
