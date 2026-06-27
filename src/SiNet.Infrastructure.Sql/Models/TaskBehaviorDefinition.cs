namespace SiNetSQL.Models;

/// <summary>
/// Determines how a parent task's completion is computed from its work targets
/// (linked entities flagged as <see cref="TaskLink.IsWorkTarget"/>) or required child tasks.
/// </summary>
public enum TaskAggregationMode
{
    /// <summary>All required targets/children must reach <c>Done</c> (or <c>Skipped</c>).</summary>
    AllRequired = 0,

    /// <summary>Any single target/child reaching <c>Done</c> completes the parent.</summary>
    AnyOne = 1,

    /// <summary>No automatic aggregation — the user closes the parent manually.</summary>
    Manual = 2,
}

/// <summary>
/// מגדיר את ההתנהגות של סוג משימה: מה מפעיל אותה, מה מסיים אותה,
/// ואילו קישורים נדרשים.
/// <para>
/// קשור 1:0..1 ל-<see cref="TaskType"/> — כל התנהגות יכולה לציין
/// באיזה סוג משימה ליצור כש-Trigger מזוהה.
/// </para>
/// </summary>
public class TaskBehaviorDefinition
{
    public int Id { get; set; }

    /// <summary>קוד ייחודי לזיהוי ההתנהגות (אנגלית).</summary>
    public string Code { get; set; } = null!;

    /// <summary>שם תצוגה בעברית.</summary>
    public string DisplayName { get; set; } = null!;

    /// <summary>תיאור ההתנהגות.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// FK ל-<see cref="TaskType"/>: באיזה סוג משימה ליצור ProjectAssignment.
    /// </summary>
    public int? TaskTypeId { get; set; }

    /// <summary>האם ליצור משימה אוטומטית כשטריגר מזוהה.</summary>
    public bool AutoCreateOnTrigger { get; set; }

    /// <summary>האם לסגור משימה אוטומטית כשתנאי ההשלמה מתקיים.</summary>
    public bool AutoCloseOnCompletion { get; set; }

    /// <summary>
    /// כיצד מחושב סטטוס ההשלמה של משימת־אב מתוך יעדי העבודה / משימות־הבן שלה.
    /// ברירת מחדל: <see cref="TaskAggregationMode.AllRequired"/>.
    /// </summary>
    public TaskAggregationMode AggregationMode { get; set; } = TaskAggregationMode.AllRequired;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // ═══ Navigation ═══

    public virtual TaskType? TaskType { get; set; }

    public virtual ICollection<TaskTriggerRule> TriggerRules { get; set; } = new List<TaskTriggerRule>();

    public virtual ICollection<TaskCompletionRule> CompletionRules { get; set; } = new List<TaskCompletionRule>();
}
