namespace SiNetSQL.Models;

/// <summary>
/// What event causes a transition to be evaluated.
/// Each <see cref="WorkflowTransitionRule"/> has exactly one trigger.
/// </summary>
public enum WorkflowTransitionTriggerType
{
    /// <summary>User clicks "Advance" manually.</summary>
    Manual = 0,

    /// <summary>All required tasks in the current stage are closed.</summary>
    AllRequiredTasksClosed = 1,

    /// <summary>A specific task changed its status.</summary>
    TaskStatusChanged = 2,

    /// <summary>A linked Sub-Workflow completed execution.</summary>
    SubWorkflowCompleted = 3,

    /// <summary>Timer elapsed since entering the stage (future).</summary>
    TimerElapsed = 10,

    /// <summary>
    /// An <c>Action</c> finished executing (success, failure, cancellation, etc.).
    /// Infrastructure-only at this stage — no listener is wired yet.
    /// Evaluated via <see cref="WorkflowTransitionConditionType.ActionCompleted"/>.
    /// </summary>
    ActionCompleted = 11,
}
