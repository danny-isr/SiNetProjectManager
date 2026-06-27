namespace SiNetSQL.Models;

/// <summary>
/// Links a <see cref="WorkflowStageDefinition"/> to a <see cref="TaskType"/>
/// and optionally assigns a default employee.
/// This defines "which tasks should be performed at each workflow stage and by whom."
/// </summary>
public class WorkflowStageTask
{
    public int Id { get; set; }

    /// <summary>FK to the stage this task belongs to.</summary>
    public int StageDefinitionId { get; set; }

    /// <summary>FK to the task type.</summary>
    public int TaskTypeId { get; set; }

    /// <summary>FK to the default assignee (nullable — unassigned until set).</summary>
    public int? DefaultAssigneeId { get; set; }

    /// <summary>Display/execution order within the stage (1-based).</summary>
    public int SortOrder { get; set; }

    /// <summary>Whether this task must be completed before the stage can advance.</summary>
    public bool IsRequired { get; set; }

    /// <summary>Optional instructions or notes for the assignee.</summary>
    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // ═══ Navigation ═══

    public virtual WorkflowStageDefinition StageDefinition { get; set; } = null!;

    public virtual TaskType TaskType { get; set; } = null!;

    public virtual Siuser? DefaultAssignee { get; set; }
}
