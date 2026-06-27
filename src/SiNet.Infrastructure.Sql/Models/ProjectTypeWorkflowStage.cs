namespace SiNetSQL.Models;

/// <summary>
/// Per-<see cref="JobType"/> (ProjectType) configuration declaring which
/// <see cref="WorkflowStageDefinition"/> rows are active/required/repeatable for that ProjectType.
/// <para>
/// Example: a "הסדר תנועה" project may not include <c>PLN.Design.WorkPlans</c>,
/// while a "פיתוח רחוב" project may.
/// </para>
/// </summary>
public partial class ProjectTypeWorkflowStage
{
    public int Id { get; set; }

    public int ProjectTypeId { get; set; }

    public int WorkflowStageDefinitionId { get; set; }

    public bool IsRequired { get; set; } = true;

    public int SortOrder { get; set; }

    public bool CanRepeat { get; set; }

    public bool IsActive { get; set; } = true;

    public string? Notes { get; set; }

    // Navigation
    public virtual JobType ProjectType { get; set; } = null!;

    public virtual WorkflowStageDefinition WorkflowStageDefinition { get; set; } = null!;
}
