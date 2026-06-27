namespace SiNetSQL.Models;

/// <summary>
/// A running instance of a workflow, linked to a project.
/// Tracks the current stage, lifecycle status, and what triggered its creation.
/// <para>
/// When <see cref="IsProjectBound"/> is <c>false</c>, the workflow is logically independent
/// of the project (e.g. Proposal / Price-Quote). The <see cref="ProjectId"/> still references
/// a default project (e.g. "ניהול משרד") for DB integrity, but the workflow is not
/// considered part of that project's deliverables.
/// </para>
/// <para>
/// History of stage transitions is stored in <see cref="WorkflowStageTransition"/>.
/// </para>
/// </summary>
public class WorkflowInstance
{
    public int Id { get; set; }

    /// <summary>FK to the template this instance was created from.</summary>
    public int WorkflowDefinitionId { get; set; }

    /// <summary>
    /// FK to the project. Always required (DB constraint).
    /// For project-independent workflows, this is set to the default "Office Management" project.
    /// </summary>
    public int ProjectId { get; set; }

    /// <summary>
    /// Whether this workflow is logically bound to the project.
    /// <c>true</c> — standard project workflow (Design, Review, etc.).
    /// <c>false</c> — project-independent workflow (Proposal, Price-Quote);
    /// ProjectId is just a placeholder for DB integrity.
    /// </summary>
    public bool IsProjectBound { get; set; } = true;

    /// <summary>Lifecycle status of this instance.</summary>
    public WorkflowStatus Status { get; set; }

    /// <summary>FK to the current stage (null when Draft or Completed/Cancelled).</summary>
    public int? CurrentStageId { get; set; }

    /// <summary>What triggered the creation of this instance.</summary>
    public WorkflowTriggerType TriggerType { get; set; }

    /// <summary>
    /// Optional FK to the entity that triggered this workflow.
    /// Interpretation depends on <see cref="TriggerType"/>:
    /// Email → EmailInboxMessage.Id, Manual → SIUser.Id, System → null.
    /// <para>
    /// NOTE: This field is NOT used as a parent-workflow link. For sub-workflow
    /// parent/child relationships use the explicit <see cref="ParentWorkflowInstanceId"/>
    /// (decision 2026-05-23, see Docs/WorkflowDecisions.md).
    /// </para>
    /// </summary>
    public int? TriggerEntityId { get; set; }

    /// <summary>
    /// Explicit parent link populated when this instance was started as a
    /// sub-workflow by a parent <see cref="WorkflowInstance"/> (see
    /// <see cref="WorkflowTransitionActionType.StartSubWorkflow"/>).
    /// <c>null</c> for root workflows.
    /// </summary>
    public int? ParentWorkflowInstanceId { get; set; }

    /// <summary>User who started this workflow.</summary>
    public int CreatedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>Optional notes (e.g. reason for cancellation).</summary>
    public string? Notes { get; set; }

    // ═══ Navigation ═══

    public virtual WorkflowDefinition WorkflowDefinition { get; set; } = null!;

    public virtual Project Project { get; set; } = null!;

    public virtual WorkflowStageDefinition? CurrentStage { get; set; }

    public virtual Siuser CreatedByUser { get; set; } = null!;

    public virtual ICollection<WorkflowStageTransition> StageTransitions { get; set; } = new List<WorkflowStageTransition>();

    /// <summary>Parent workflow instance, if this is a sub-workflow.</summary>
    public virtual WorkflowInstance? ParentWorkflowInstance { get; set; }

    /// <summary>Child sub-workflow instances started by this instance.</summary>
    public virtual ICollection<WorkflowInstance> ChildWorkflowInstances { get; set; } = new List<WorkflowInstance>();
}
