namespace SiNetSQL.Models;

/// <summary>
/// Template defining a reusable workflow process (e.g. Design, Review, Opinion).
/// Each definition contains ordered stages and transition rules.
/// <para>
/// Multiple <see cref="WorkflowInstance"/>s can be created from one definition.
/// </para>
/// </summary>
public class WorkflowDefinition
{
    public int Id { get; set; }

    /// <summary>Internal code name (e.g. "Design", "Review", "Opinion").</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Human-readable display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description of the workflow purpose.</summary>
    public string? Description { get; set; }

    /// <summary>Whether this definition can be used to create new instances.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ModifiedAtUtc { get; set; }

    // ═══ Navigation ═══

    public virtual ICollection<WorkflowStageDefinition> Stages { get; set; } = new List<WorkflowStageDefinition>();

    public virtual ICollection<WorkflowTransitionRule> TransitionRules { get; set; } = new List<WorkflowTransitionRule>();

    public virtual ICollection<WorkflowInstance> Instances { get; set; } = new List<WorkflowInstance>();

    public virtual ICollection<ProjectTypeWorkflowDefinition> AllowedForProjectTypes { get; set; } = new List<ProjectTypeWorkflowDefinition>();

    public virtual ICollection<WorkflowStartTrigger> StartTriggers { get; set; } = new List<WorkflowStartTrigger>();
}
