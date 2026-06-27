namespace SiNetSQL.Models;

/// <summary>
/// מגדיר את אופן ההמתנה לתת-Workflow — לחכות לסיום או להמשיך מיד.
/// </summary>
public enum WorkflowSubWorkflowWaitMode
{
    /// <summary>השלב ממתין עד שתת-ה-Workflow מסתיים בהצלחה.</summary>
    WaitForCompletion = 0,

    /// <summary>תת-ה-Workflow רץ ברקע, השלב ממשיך הלאה מיד.</summary>
    FireAndForget = 1,
}
