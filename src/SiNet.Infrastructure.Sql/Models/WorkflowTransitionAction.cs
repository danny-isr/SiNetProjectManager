namespace SiNetSQL.Models;

/// <summary>
/// A single action to execute when a <see cref="WorkflowTransitionRule"/> fires.
/// Each transition can have multiple actions executed in <see cref="SortOrder"/>.
/// </summary>
public class WorkflowTransitionAction
{
    public int Id { get; set; }

    /// <summary>FK to the parent transition rule.</summary>
    public int TransitionRuleId { get; set; }

    /// <summary>The type of action to perform.</summary>
    public WorkflowTransitionActionType ActionType { get; set; }

    /// <summary>
    /// Stable string code for the underlying process action (e.g. "CreateStageTasks",
    /// "RecordTaskResult"). Populated by the seeder via
    /// <c>ActionDefinitionRegistry.MapFromWorkflowTransitionActionType(...)</c>.
    /// Nullable for backward compatibility with rows seeded before this column
    /// existed; runtime uses it when present and falls back to enum mapping otherwise.
    /// </summary>
    public string? ActionCode { get; set; }

    /// <summary>JSON configuration payload for the action (e.g. notification template, sub-workflow params).</summary>
    public string? ConfigJson { get; set; }

    /// <summary>Execution order within the transition (lower = first).</summary>
    public int SortOrder { get; set; }

    // Navigation

    public virtual WorkflowTransitionRule TransitionRule { get; set; } = null!;
}
