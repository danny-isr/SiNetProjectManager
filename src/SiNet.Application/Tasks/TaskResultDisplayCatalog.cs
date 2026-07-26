namespace SiNet.Application.Tasks;

/// <summary>Visual meaning for a task-result option in completion ComboBoxes.</summary>
public enum TaskResultColorKind
{
    Neutral = 0,
    Positive = 1,
    Negative = 2,
}

/// <summary>Resolved Hebrew label + color for a task-result code (completion still uses the English code).</summary>
public readonly record struct TaskResultDisplay(string Code, string DisplayName, TaskResultColorKind ColorKind);

/// <summary>
/// Single UI catalog: English <c>TaskResultCode</c> → Hebrew label + Positive/Negative/Neutral.
/// Material-check uses the short QA phrasing ("חסר חומר" / "לא חסר חומר").
/// </summary>
public static class TaskResultDisplayCatalog
{
    public static TaskResultDisplay Resolve(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return new TaskResultDisplay(code ?? string.Empty, code ?? string.Empty, TaskResultColorKind.Neutral);

        if (Map.TryGetValue(code, out var display))
            return display;

        return new TaskResultDisplay(code, code, TaskResultColorKind.Neutral);
    }

    private static readonly Dictionary<string, TaskResultDisplay> Map = Build();

    private static Dictionary<string, TaskResultDisplay> Build()
    {
        var d = new Dictionary<string, TaskResultDisplay>(StringComparer.Ordinal);

        void Add(string code, string name, TaskResultColorKind color)
            => d[code] = new TaskResultDisplay(code, name, color);

        // Intake
        Add("QuoteRequestDetected", "זוהתה כפנייה להצעת מחיר", TaskResultColorKind.Positive);
        Add("NotQuoteRequest", "לא פנייה להצעת מחיר", TaskResultColorKind.Negative);

        // Quote / material (short UX labels for completeness decisions)
        Add("QuoteMaterialComplete", "לא חסר חומר להצעה", TaskResultColorKind.Positive);
        Add("QuoteMaterialMissing", "חסר חומר להצעה", TaskResultColorKind.Negative);
        Add("MaterialComplete", "לא חסר חומר", TaskResultColorKind.Positive);
        Add("MaterialMissing", "חסר חומר", TaskResultColorKind.Negative);
        Add("MissingMaterialRequestSent", "נשלחה דרישה להשלמת חומר", TaskResultColorKind.Neutral);
        Add("MissingMaterialReceived", "התקבלה השלמת חומר", TaskResultColorKind.Positive);

        // Quote prep
        Add("QuoteCalculationCompleted", "תחשיב הצעה הושלם", TaskResultColorKind.Positive);
        Add("QuotePrepared", "הצעת מחיר מוכנה", TaskResultColorKind.Positive);
        Add("QuoteApprovedInternally", "הצעה אושרה פנימית", TaskResultColorKind.Positive);
        Add("QuoteRequiresRevision", "הצעה דורשת תיקון", TaskResultColorKind.Negative);
        Add("QuoteSent", "הצעת מחיר נשלחה ללקוח", TaskResultColorKind.Neutral);
        Add("QuoteApprovedByClient", "הצעה אושרה ע״י הלקוח", TaskResultColorKind.Positive);
        Add("QuoteRejectedByClient", "הצעה נדחתה ע״י הלקוח", TaskResultColorKind.Negative);

        // Work order
        Add("WorkOrderReceived", "התקבלה הזמנת עבודה", TaskResultColorKind.Positive);
        Add("WorkOrderFiled", "הזמנת עבודה תויקה", TaskResultColorKind.Positive);

        // Design
        Add("DesignDraftCompleted", "טיוטת תכנון הושלמה", TaskResultColorKind.Positive);
        Add("PreliminaryDesignCompleted", "תכנון מוקדם הושלם", TaskResultColorKind.Positive);
        Add("DetailedDesignCompleted", "תכנון מפורט הושלם", TaskResultColorKind.Positive);

        // Authority
        Add("SubmittedForApproval", "הוגש לאישור", TaskResultColorKind.Neutral);
        Add("AuthorityCommentsReceived", "התקבלו הערות גורם מאשר", TaskResultColorKind.Neutral);
        Add("AuthorityApproved", "אושר ע״י גורם מאשר", TaskResultColorKind.Positive);
        Add("CorrectionsRequired", "נדרשות תיקונים", TaskResultColorKind.Negative);
        Add("CorrectionsCompleted", "תיקונים הושלמו", TaskResultColorKind.Positive);

        // Work plans
        Add("WorkPlansCompleted", "תוכניות עבודה הושלמו", TaskResultColorKind.Positive);
        Add("WorkPlansDelivered", "תוכניות עבודה נמסרו", TaskResultColorKind.Positive);

        // Billing
        Add("BillingMilestoneReached", "הגיע אבן דרך לחשבון", TaskResultColorKind.Neutral);
        Add("BillRequired", "נדרש להוציא חשבון", TaskResultColorKind.Neutral);
        Add("BillNotRequired", "לא נדרש חשבון בשלב זה", TaskResultColorKind.Neutral);
        Add("BillPrepared", "חשבון הוכן", TaskResultColorKind.Positive);
        Add("BillSubmitted", "חשבון הוגש", TaskResultColorKind.Neutral);
        Add("BillApproved", "חשבון אושר", TaskResultColorKind.Positive);

        // Close
        Add("ProjectClosed", "פרויקט נסגר", TaskResultColorKind.Neutral);
        Add("ProjectCloseApproved", "אישור סגירת פרויקט", TaskResultColorKind.Positive);
        Add("ProjectCloseRejected", "סגירה נדחתה", TaskResultColorKind.Negative);
        Add("ProjectCloseNeedsMoreInfo", "דרוש מידע נוסף לפני סגירה", TaskResultColorKind.Neutral);

        // Review
        Add("RequestFromMunicipality", "פנייה מהרשות", TaskResultColorKind.Neutral);
        Add("RequestFromPlanner", "פנייה מהמתכנן", TaskResultColorKind.Neutral);
        Add("MunicipalityRequestReceived", "התקבלה פנייה רשמית מהרשות", TaskResultColorKind.Positive);
        Add("ProjectOpened", "פרויקט נפתח", TaskResultColorKind.Positive);
        Add("ProfessionalReviewCompleted", "בדיקה מקצועית הושלמה", TaskResultColorKind.Positive);
        Add("ManagerApproved", "מנהל אישר", TaskResultColorKind.Positive);
        Add("ManagerRequestedChanges", "מנהל ביקש תיקונים", TaskResultColorKind.Negative);
        Add("CommentsSentToPlanner", "הערות נשלחו למתכנן", TaskResultColorKind.Neutral);
        Add("PlannerCorrectionsReceived", "התקבלו תיקוני מתכנן", TaskResultColorKind.Positive);
        Add("RecheckPassed", "בדיקה חוזרת עברה", TaskResultColorKind.Positive);
        Add("RecheckRequiresMoreCorrections", "בדיקה חוזרת דורשת תיקונים", TaskResultColorKind.Negative);
        Add("PrincipallyApproved", "אושר עקרונית", TaskResultColorKind.Positive);
        Add("PoliceApprovalRequired", "נדרש אישור משטרה", TaskResultColorKind.Neutral);
        Add("PoliceApprovalNotRequired", "אינו דורש אישור משטרה", TaskResultColorKind.Positive);
        Add("SubmittedToPolice", "הוגש למשטרה", TaskResultColorKind.Neutral);
        Add("PoliceApproved", "אושר ע״י משטרה", TaskResultColorKind.Positive);
        Add("PoliceCommentsReceived", "התקבלו הערות משטרה", TaskResultColorKind.Negative);
        Add("PoliceCorrectionsReceived", "התקבלו תיקונים בעקבות הערות משטרה", TaskResultColorKind.Positive);
        Add("ReviewProjectClosed", "פרויקט בדיקה נסגר", TaskResultColorKind.Neutral);

        // Opinion
        Add("OpinionAnalysisCompleted", "ניתוח מסמכים לחוות דעת הושלם", TaskResultColorKind.Positive);
        Add("OpinionDraftPrepared", "טיוטת חוות דעת מוכנה", TaskResultColorKind.Positive);
        Add("OpinionRequiresRevision", "חוות דעת דורשת תיקונים", TaskResultColorKind.Negative);
        Add("OpinionApprovedInternally", "חוות דעת אושרה פנימית", TaskResultColorKind.Positive);
        Add("OpinionSent", "חוות דעת נשלחה", TaskResultColorKind.Neutral);

        return d;
    }
}
