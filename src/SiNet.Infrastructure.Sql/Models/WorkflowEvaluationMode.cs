namespace SiNetSQL.Models;

/// <summary>
/// מגדיר כיצד מעבר (Transition) מוערך — אוטומטי, ידני, או אוטומטי עם אישור.
/// </summary>
public enum WorkflowEvaluationMode
{
    /// <summary>המעבר מופעל אוטומטית כשהתנאי מתקיים.</summary>
    Auto = 0,

    /// <summary>המעבר דורש הפעלה ידנית על ידי המשתמש.</summary>
    Manual = 1,

    /// <summary>המערכת מזהה שהתנאי מתקיים ומציגה אישור למשתמש.</summary>
    AutoWithConfirm = 2,
}
