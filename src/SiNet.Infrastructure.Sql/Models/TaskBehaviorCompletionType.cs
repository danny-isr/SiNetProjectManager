namespace SiNetSQL.Models;

/// <summary>
/// סוג התנאי שמשלים/סוגר משימה אוטומטית.
/// </summary>
public enum TaskBehaviorCompletionType
{
    /// <summary>כשכל הקבצים המצורפים של המייל המפעיל תויקו ל-ProjectFile.</summary>
    AllAttachmentsTagged = 1,

    /// <summary>כשנשלח מייל תשובה (אישור/הערות).</summary>
    EmailReplySent = 2,

    /// <summary>סגירה ידנית ע"י המשתמש.</summary>
    Manual = 3,

    /// <summary>כשכל משימות החובה בשלב הושלמו (מטופל ע"י Orchestrator).</summary>
    AllRequiredStageTasksClosed = 4,
}
