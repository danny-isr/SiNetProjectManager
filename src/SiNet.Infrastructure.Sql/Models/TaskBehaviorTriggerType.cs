namespace SiNetSQL.Models;

/// <summary>
/// סוג האירוע שמפעיל יצירת משימה אוטומטית.
/// </summary>
public enum TaskBehaviorTriggerType
{
    /// <summary>כשמייל משויך/מועבר לפרויקט.</summary>
    EmailAssignedToProject = 1,

    /// <summary>כשקובץ מצורף מתויק ל-ProjectFile.</summary>
    AttachmentTagged = 2,

    /// <summary>כש-Workflow מתקדם לשלב חדש (מטופל ע"י Orchestrator).</summary>
    WorkflowStageEntered = 3,

    /// <summary>יצירה ידנית ע"י המשתמש.</summary>
    Manual = 4,
}
