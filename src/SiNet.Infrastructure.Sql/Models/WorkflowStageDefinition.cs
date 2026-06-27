namespace SiNetSQL.Models;

/// <summary>
/// One stage within a <see cref="WorkflowDefinition"/> template.
/// Stages are ordered by <see cref="SortOrder"/> and have a unique <see cref="Code"/> per definition.
/// </summary>
public class WorkflowStageDefinition
{
    public int Id { get; set; }

    /// <summary>FK to the parent definition.</summary>
    public int WorkflowDefinitionId { get; set; }

    /// <summary>Machine-readable code (unique within the definition).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Human-readable display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description of what happens in this stage.</summary>
    public string? Description { get; set; }

    /// <summary>Display/execution order within the definition (1-based).</summary>
    public int SortOrder { get; set; }

    /// <summary>Whether this is the initial stage for new instances.</summary>
    public bool IsInitial { get; set; }

    /// <summary>Whether reaching this stage marks the workflow as completed.</summary>
    public bool IsFinal { get; set; }

    // Visual Designer

    /// <summary>Node type for visual designer rendering. Values: Stage, Decision, Fork, Join, Start, End, SubWorkflow.</summary>
    public string NodeType { get; set; } = "Stage";

    /// <summary>X position on the visual canvas (pixels).</summary>
    public double CanvasX { get; set; }

    /// <summary>Y position on the visual canvas (pixels).</summary>
    public double CanvasY { get; set; }

    /// <summary>Optional color/theme for the node (hex, e.g. "#2196F3").</summary>
    public string? Color { get; set; }

    // Sub-Workflow

    /// <summary>FK to the sub-workflow definition linked to this node (null if not a SubWorkflow node).</summary>
    public int? SubWorkflowDefinitionId { get; set; }

    /// <summary>How to wait for the sub-workflow: WaitForCompletion or FireAndForget.</summary>
    public WorkflowSubWorkflowWaitMode SubWorkflowWaitMode { get; set; } = WorkflowSubWorkflowWaitMode.WaitForCompletion;

    // Group Assignment

    /// <summary>
    /// FK to the <see cref="UserGroup"/> responsible for this stage.
    /// When the workflow reaches this stage, tasks are assigned to members of this group.
    /// Null = unassigned (manual assignment required).
    /// </summary>
    public int? AssignedGroupId { get; set; }

    // Navigation

    public virtual WorkflowDefinition WorkflowDefinition { get; set; } = null!;

    /// <summary>The sub-workflow definition linked to this stage (if NodeType == SubWorkflow).</summary>
    public virtual WorkflowDefinition? SubWorkflowDefinition { get; set; }

    /// <summary>The user group responsible for this stage.</summary>
    public virtual UserGroup? AssignedGroup { get; set; }

    public virtual ICollection<WorkflowTransitionRule> TransitionRulesFrom { get; set; } = new List<WorkflowTransitionRule>();

    public virtual ICollection<WorkflowTransitionRule> TransitionRulesTo { get; set; } = new List<WorkflowTransitionRule>();

    public virtual ICollection<WorkflowStageTransition> TransitionsEntered { get; set; } = new List<WorkflowStageTransition>();

    public virtual ICollection<WorkflowStageTask> StageTasks { get; set; } = new List<WorkflowStageTask>();
}
