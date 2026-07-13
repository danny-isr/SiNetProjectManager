using System.Windows.Media;

namespace SiNet.App.Wpf.Surfaces.Workflow;

/// <summary>Hebrew short labels + layout helpers for the visual canvas.</summary>
public static class WorkflowCanvasLabels
{
    public static string TriggerHe(string? code) => code switch
    {
        "Manual" => "ידני",
        "AllRequiredTasksClosed" => "כל המשימות הנדרשות נסגרו",
        "TaskStatusChanged" => "שינוי סטטוס/תוצאת משימה",
        "SubWorkflowCompleted" => "תת-תהליך הסתיים",
        "TimerElapsed" => "תם הזמן",
        "ActionCompleted" => "פעולה הושלמה",
        _ => code ?? string.Empty,
    };

    public static string ConditionHe(string? code) => code switch
    {
        "Always" => "תמיד",
        "AllTasksComplete" => "כל המשימות הושלמו",
        "TaskStatusEquals" => "סטטוס משימה שווה ל־",
        "TaskStatusNotEquals" => "סטטוס משימה שונה מ־",
        "SubWorkflowSucceeded" => "תת-תהליך הצליח",
        "SubWorkflowFailed" => "תת-תהליך נכשל",
        "TaskResultEquals" => "תוצאת משימה שווה ל־",
        "ActionCompleted" => "פעולה הושלמה",
        _ => code ?? string.Empty,
    };

    public static string EvaluationHe(string? code) => code switch
    {
        "Manual" => "ידני",
        "Auto" => "אוטומטי",
        "AutoWithConfirm" => "אוטומטי עם אישור",
        _ => code ?? string.Empty,
    };

    public static string ActionHe(string? code) => code switch
    {
        "CreateStageTasks" => "יצירת משימות שלב",
        "ClosePreviousStageTasks" => "סגירת משימות שלב קודם",
        "SendNotification" => "שליחת התראה",
        "StartSubWorkflow" => "הפעלת תת-תהליך",
        "SetProjectStatus" => "עדכון סטטוס פרויקט",
        "RecordTaskResult" => "רישום תוצאת משימה",
        "SetBillingPending" => "סימון ממתין לחשבון",
        "CloseProject" => "סגירת פרויקט",
        _ => code ?? string.Empty,
    };

    public static string ExplainTrigger(string trigger) => trigger switch
    {
        "Manual" => "מופעל רק כשמשתמש מקדם את התהליך ידנית (לא ב-auto-advance).",
        "AllRequiredTasksClosed" => "מופעל אחרי שכל המשימות הנדרשות בשלב המקור נסגרו — הרשימה מופיעה מיד מתחת.",
        "TaskStatusChanged" => "מופעל אחרי שינוי סטטוס/תוצאת משימה (לרוב עם TaskResultEquals).",
        "SubWorkflowCompleted" => "מופעל כשתת-התהליך הילד מסתיים.",
        "ActionCompleted" => "מופעל אחרי השלמת פעולת תהליך.",
        _ => "טריגר מעבר כפי שמוגדר בכלל.",
    };

    public static bool TriggerDependsOnRequiredTasks(string? trigger) =>
        string.Equals(trigger, "AllRequiredTasksClosed", StringComparison.Ordinal);

    public static bool IsEmphasizedTrigger(string? trigger) =>
        string.Equals(trigger, "TaskStatusChanged", StringComparison.Ordinal)
        || string.Equals(trigger, "AllRequiredTasksClosed", StringComparison.Ordinal)
        || string.Equals(trigger, "SubWorkflowCompleted", StringComparison.Ordinal);

    public static Brush EmphasizedLabelBrush { get; } = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
    public static Brush NormalLabelBrush { get; } = new SolidColorBrush(Color.FromRgb(0x26, 0x32, 0x38));

    /// <summary>World-space half-gap (px) for A↔B reverse pairs; applied along an undirected pair normal.</summary>
    public const double ReversePairGap = 22;

    /// <summary>
    /// Perpendicular offset for parallel edges on the same directed pair,
    /// plus reverse-pair bias when A↔B both exist.
    /// Must be applied along an undirected (lowerId→higherId) normal so reverse directions do not cancel.
    /// </summary>
    public static double ComputeLateral(int indexInGroup, int groupCount, bool hasReversePair, bool fromLessThanTo)
    {
        var fan = groupCount <= 1 ? 0 : (indexInGroup - (groupCount - 1) / 2.0) * 22;
        var reverse = 0.0;
        if (hasReversePair)
        {
            reverse = fromLessThanTo ? -ReversePairGap : ReversePairGap;
        }

        return fan + reverse;
    }
}
