namespace SiNetSQL.Models;

/// <summary>
/// The logical condition that must be satisfied for a transition to fire.
/// Evaluated after the <see cref="WorkflowTransitionTriggerType"/> fires.
/// </summary>
public enum WorkflowTransitionConditionType
{
    /// <summary>No condition — always passes.</summary>
    Always = 0,

    /// <summary>All required stage tasks are closed.</summary>
    AllTasksComplete = 1,

    /// <summary>A specific task's status equals a given value.</summary>
    TaskStatusEquals = 2,

    /// <summary>A specific task's status does NOT equal a given value.</summary>
    TaskStatusNotEquals = 3,

    /// <summary>A linked Sub-Workflow completed successfully.</summary>
    SubWorkflowSucceeded = 4,

    /// <summary>A linked Sub-Workflow was cancelled or failed.</summary>
    SubWorkflowFailed = 5,

    /// <summary>
    /// A specific <see cref="TaskResultDefinition"/> code was recorded on a recent task event.
    /// ConditionJson example: { "TaskResultCode": "AuthorityApproved" } or { "TaskResultCodes": ["A","B"] }.
    /// </summary>
    TaskResultEquals = 6,

    /// <summary>
    /// An Action completed with a specific code (and optionally a specific outcome).
    /// ConditionJson example: { "ActionCode": "PerformReview", "Outcome": "Succeeded" }.
    /// If <c>Outcome</c> is omitted, only <c>ActionCode</c> is checked.
    /// Infrastructure-only at this stage — no listener is wired yet.
    /// </summary>
    ActionCompleted = 7,
}
