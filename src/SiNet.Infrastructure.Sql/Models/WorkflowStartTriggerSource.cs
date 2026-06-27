namespace SiNetSQL.Models;

/// <summary>
/// מקור האירוע שמפעיל Workflow Instance חדש.
/// כל WorkflowDefinition יכול להגדיר טריגר(ים) שיוצרים אותו אוטומטית.
/// </summary>
public enum WorkflowStartTriggerSource
{
    /// <summary>הפעלה ידנית על ידי משתמש.</summary>
    ManualStart = 0,

    /// <summary>פרויקט חדש נוצר במערכת.</summary>
    ProjectCreated = 1,

    /// <summary>סוג פרויקט הוגדר או השתנה.</summary>
    ProjectTypeAssigned = 2,

    /// <summary>מייל תויק (Email Filed) — לפי סוג התיוק.</summary>
    EmailFiled = 3,

    /// <summary>תת-תהליך שהופעל על ידי Workflow אב (ParentWorkflow).</summary>
    ParentWorkflow = 4,

    /// <summary>טיימר / תזמון מתוכנת.</summary>
    ScheduledTimer = 5,

    /// <summary>קריאת API חיצונית.</summary>
    ApiCall = 6,
}
