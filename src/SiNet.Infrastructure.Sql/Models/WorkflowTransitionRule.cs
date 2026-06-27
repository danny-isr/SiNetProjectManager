namespace SiNetSQL.Models;

/// <summary>
/// Defines an allowed transition between two stages in a <see cref="WorkflowDefinition"/>.
/// If no rule exists for a given (From -> To) pair, the transition is forbidden.
/// </summary>
public class WorkflowTransitionRule
{
    public int Id { get; set; }

    /// <summary>FK to the parent definition.</summary>
    public int WorkflowDefinitionId { get; set; }

    /// <summary>FK to the source stage.</summary>
    public int FromStageId { get; set; }

    /// <summary>FK to the destination stage.</summary>
    public int ToStageId { get; set; }

    /// <summary>Optional human-readable label for this transition.</summary>
    public string? Name { get; set; }

    // Trigger / Condition / Evaluation

    /// <summary>What event causes this transition to be evaluated.</summary>
    public WorkflowTransitionTriggerType TriggerType { get; set; } = WorkflowTransitionTriggerType.Manual;

    /// <summary>The logical condition that must be satisfied for the transition to fire.</summary>
    public WorkflowTransitionConditionType ConditionType { get; set; } = WorkflowTransitionConditionType.Always;

    /// <summary>JSON payload with condition parameters (e.g. TaskTypeId, StatusId).</summary>
    public string? ConditionJson { get; set; }

    /// <summary>
    /// Deterministic hash of <see cref="ConditionJson"/> used as a key component
    /// in the unique index so multiple legitimate transitions between the same
    /// (From, To) pair can coexist when they differ only by condition payload.
    /// <para>
    /// Computed by <see cref="ComputeConditionHash(string?)"/>; a null/empty
    /// ConditionJson always maps to a fixed sentinel hash so the index stays
    /// deterministic (NOT NULL in DB).
    /// </para>
    /// </summary>
    public string ConditionHash { get; set; } = ComputeConditionHash(null);

    /// <summary>How the transition is evaluated: auto, manual, or auto with confirmation.</summary>
    public WorkflowEvaluationMode EvaluationMode { get; set; } = WorkflowEvaluationMode.Manual;

    // Visual Designer

    /// <summary>Condition expression for display.</summary>
    public string? Condition { get; set; }

    /// <summary>Display label on the arrow.</summary>
    public string? Label { get; set; }

    /// <summary>Priority for evaluating transitions from the same source (lower = first).</summary>
    public int Priority { get; set; }

    /// <summary>Visual routing waypoints for the connector line (JSON array).</summary>
    public string? RoutePointsJson { get; set; }

    // Navigation

    public virtual WorkflowDefinition WorkflowDefinition { get; set; } = null!;
    public virtual WorkflowStageDefinition FromStage { get; set; } = null!;
    public virtual WorkflowStageDefinition ToStage { get; set; } = null!;

    /// <summary>Actions executed when this transition fires (1:N).</summary>
    public virtual ICollection<WorkflowTransitionAction> Actions { get; set; } = new List<WorkflowTransitionAction>();

    // ───────────────────────────────────────────────────────────────────────
    //  ConditionHash helper
    // ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Deterministic 64-char hex SHA-256 of a normalized condition payload.
    /// Null / whitespace-only payloads collapse to a fixed sentinel hash so
    /// the unique index always has a stable, non-null value to compare.
    /// </summary>
    public static string ComputeConditionHash(string? conditionJson)
    {
        var normalized = string.IsNullOrWhiteSpace(conditionJson)
            ? "<null>"
            : conditionJson.Trim();

        var bytes = System.Text.Encoding.UTF8.GetBytes(normalized);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash); // 64 chars, deterministic.
    }
}
