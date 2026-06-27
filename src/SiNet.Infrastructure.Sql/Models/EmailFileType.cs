namespace SiNetSQL.Models;

/// <summary>
/// סוג תיוק מייל — מגדיר מה עושים עם המייל שתויק.
/// </summary>
public enum EmailFileType
{
    /// <summary>תכתובת כללית — ללא השפעה על תהליך.</summary>
    General = 0,

    /// <summary>הזמנת עבודה — מייל שמכיל הזמנה מלקוח. חייב פרויקט קיים + קבצים.</summary>
    WorkOrder = 1,

    /// <summary>חומר לתכנון — תוכניות, מפרטים, נתונים. חייב פרויקט קיים + קבצים.</summary>
    PlanningMaterial = 2,

    /// <summary>הצעת מחיר — מייל שדורש יצירת פרויקט חדש מסוג הצעה.</summary>
    Proposal = 3,

    /// <summary>בקשת שינוי — שינוי על פרויקט קיים.</summary>
    ChangeRequest = 4,

    /// <summary>אישור / אשרור — מייל שמאשר משהו בתהליך.</summary>
    Approval = 5,

    /// <summary>דוח / סיכום — מייל אינפורמטיבי.</summary>
    Report = 6,
}
