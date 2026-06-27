namespace SiNetSQL.Models;

/// <summary>
/// Records a single stage transition within a <see cref="WorkflowInstance"/>.
/// Provides a full audit trail of workflow progression.
/// </summary>
public class WorkflowStageTransition
{
    public int Id { get; set; }

    /// <summary>FK to the workflow instance.</summary>
    public int WorkflowInstanceId { get; set; }

    /// <summary>FK to the stage that was entered in this transition.</summary>
    public int ToStageId { get; set; }

    /// <summary>FK to the stage that was exited (null for the initial entry).</summary>
    public int? FromStageId { get; set; }

    /// <summary>User who triggered this transition.</summary>
    public int TransitionedByUserId { get; set; }

    public DateTime TransitionedAtUtc { get; set; }

    /// <summary>Optional notes about this transition.</summary>
    public string? Notes { get; set; }

    // ═══ Navigation ═══

    public virtual WorkflowInstance WorkflowInstance { get; set; } = null!;

    public virtual WorkflowStageDefinition ToStage { get; set; } = null!;

    public virtual Siuser TransitionedByUser { get; set; } = null!;
}
