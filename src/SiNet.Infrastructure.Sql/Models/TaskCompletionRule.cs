namespace SiNetSQL.Models;

/// <summary>
/// כלל השלמה: מגדיר מתי ואיך משימה מסתיימת.
/// תומך בתוצאות שונות (אישור/הערות) עם סטטוס שונה לכל תוצאה.
/// <para>
/// דוגמאות:
/// <list type="bullet">
///   <item><see cref="TaskBehaviorCompletionType.AllAttachmentsTagged"/> — כשכל הקבצים תויקו → הושלם.</item>
///   <item><see cref="TaskBehaviorCompletionType.EmailReplySent"/> + אישור → מאושר.</item>
///   <item><see cref="TaskBehaviorCompletionType.EmailReplySent"/> + הערות → ממתין לתיקון.</item>
/// </list>
/// </para>
/// </summary>
public class TaskCompletionRule
{
    public int Id { get; set; }

    /// <summary>FK להגדרת ההתנהגות.</summary>
    public int BehaviorDefinitionId { get; set; }

    /// <summary>סוג תנאי ההשלמה.</summary>
    public TaskBehaviorCompletionType CompletionType { get; set; }

    /// <summary>
    /// תנאי נוסף בפורמט JSON (אופציונלי).
    /// דוגמאות: {"replyType":"Approval"}, {"replyType":"Comments"}
    /// </summary>
    public string? ConditionJson { get; set; }

    /// <summary>
    /// FK לסטטוס התוצאה — לאיזה סטטוס להעביר את המשימה כשהכלל מתקיים.
    /// </summary>
    public int ResultingStatusId { get; set; }

    /// <summary>תיאור בעברית לתצוגה.</summary>
    public string? Description { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    // ═══ Navigation ═══

    public virtual TaskBehaviorDefinition BehaviorDefinition { get; set; } = null!;

    public virtual ProjectAssignmentStatus ResultingStatus { get; set; } = null!;
}
