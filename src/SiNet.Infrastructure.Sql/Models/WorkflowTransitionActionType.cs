namespace SiNetSQL.Models;

/// <summary>
/// סוג הפעולה שמתבצעת כאשר מעבר (Transition) מופעל.
/// </summary>
public enum WorkflowTransitionActionType
{
    /// <summary>יוצר את המשימות המוגדרות בשלב היעד.</summary>
    CreateStageTasks = 0,

    /// <summary>סוגר את המשימות הפתוחות בשלב הקודם.</summary>
    ClosePreviousStageTasks = 1,

    /// <summary>שולח התראה (מייל / הודעה במערכת).</summary>
    SendNotification = 2,

    /// <summary>מפעיל תת-Workflow מקושר.</summary>
    StartSubWorkflow = 3,

    /// <summary>
    /// מעדכן את <see cref="Project.ProjectStatusId"/> לסטטוס לפי
    /// <see cref="ProjectStatus.Code"/> שמועבר ב־ConfigJson.
    /// </summary>
    SetProjectStatus = 4,

    /// <summary>
    /// רושם תוצאת משימה (TaskResult) — יוצר ProjectAssignmentEvent עם TaskResultId
    /// ומעדכן את ProjectAssignment.LastTaskResultId. הקוד נקרא לפי TaskResultDefinition.Code.
    /// </summary>
    RecordTaskResult = 5,

    /// <summary>קיצור דרך: SetProjectStatus = BillingPending.</summary>
    SetBillingPending = 6,

    /// <summary>סגירת פרויקט: SetProjectStatus = Closed וסגירת משימות פתוחות לפי הכללים.</summary>
    CloseProject = 7,
}
