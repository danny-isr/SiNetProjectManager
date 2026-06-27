namespace SiNetSQL.Models;

/// <summary>
/// כלל טריגר: מגדיר באיזה אירוע מערכתי תיווצר משימה אוטומטית.
/// <para>
/// דוגמאות:
/// <list type="bullet">
///   <item><see cref="TaskBehaviorTriggerType.EmailAssignedToProject"/> — כשמייל משויך לפרויקט.</item>
///   <item><see cref="TaskBehaviorTriggerType.AttachmentTagged"/> — כשקובץ מצורף מתויק.</item>
/// </list>
/// </para>
/// </summary>
public class TaskTriggerRule
{
    public int Id { get; set; }

    /// <summary>FK להגדרת ההתנהגות.</summary>
    public int BehaviorDefinitionId { get; set; }

    /// <summary>סוג האירוע המפעיל.</summary>
    public TaskBehaviorTriggerType TriggerType { get; set; }

    /// <summary>
    /// תנאי נוסף בפורמט JSON (אופציונלי).
    /// דוגמאות: {"emailStatus":"Processing"}, {"projectFileType":"בדיקה"}
    /// </summary>
    public string? ConditionJson { get; set; }

    /// <summary>תיאור בעברית לתצוגה.</summary>
    public string? Description { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    // ═══ Navigation ═══

    public virtual TaskBehaviorDefinition BehaviorDefinition { get; set; } = null!;
}
