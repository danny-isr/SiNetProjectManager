namespace SiNetSQL.Models;

/// <summary>
/// Mapping table that defines which WorkflowDefinitions are allowed for a given ProjectType (JobType).
/// A project's allowed workflows = UNION of all its ProjectType mappings.
/// </summary>
public class ProjectTypeWorkflowDefinition
{
    public int Id { get; set; }

    /// <summary>Foreign key to JobType (ProjectType).</summary>
    public int ProjectTypeId { get; set; }

    /// <summary>Foreign key to WorkflowDefinition.</summary>
    public int WorkflowDefinitionId { get; set; }

    /// <summary>Whether this is the default workflow for the ProjectType.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Whether this mapping is active.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Display order within the ProjectType's allowed workflows.</summary>
    public int SortOrder { get; set; }

    // ═══ Navigation ═══
    public virtual JobType ProjectType { get; set; } = null!;
    public virtual WorkflowDefinition WorkflowDefinition { get; set; } = null!;
}
