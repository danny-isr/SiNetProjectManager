using SiNet.Infrastructure.Sql.Constants;

namespace SiNet.Infrastructure.Sql.Services.SeedData;

/// <summary>
/// Clean baseline seed for <see cref="Models.TaskResultDefinition"/>.
/// Professional/business outcomes — separated from generic
/// <see cref="Models.ProjectAssignmentStatus"/>.
/// </summary>
public static class TaskResultDefinitionSeedData
{
    public static readonly TaskResultDefinitionRecord[] Definitions = new[]
    {
        // Lead / Quote intake
        new TaskResultDefinitionRecord(TaskResultCodes.QuoteRequestDetected,        "זוהתה כפנייה להצעת מחיר",         "Intake",   10),
        new TaskResultDefinitionRecord(TaskResultCodes.NotQuoteRequest,             "לא פנייה להצעת מחיר",            "Intake",   20),

        // Quote material
        new TaskResultDefinitionRecord(TaskResultCodes.QuoteMaterialComplete,       "חומר להצעה הושלם",                "Quote",    100),
        new TaskResultDefinitionRecord(TaskResultCodes.QuoteMaterialMissing,        "חסר חומר להצעה",                  "Quote",    110),

        // Quote preparation
        new TaskResultDefinitionRecord(TaskResultCodes.QuoteCalculationCompleted,   "תחשיב הצעה הושלם",                "Quote",    200),
        new TaskResultDefinitionRecord(TaskResultCodes.QuotePrepared,               "הצעת מחיר מוכנה",                  "Quote",    210),
        new TaskResultDefinitionRecord(TaskResultCodes.QuoteApprovedInternally,     "הצעה אושרה פנימית",                "Quote",    220),
        new TaskResultDefinitionRecord(TaskResultCodes.QuoteRequiresRevision,       "הצעה דורשת תיקון",                 "Quote",    230),
        new TaskResultDefinitionRecord(TaskResultCodes.QuoteSent,                   "הצעת מחיר נשלחה ללקוח",            "Quote",    240),
        new TaskResultDefinitionRecord(TaskResultCodes.QuoteApprovedByClient,       "הצעה אושרה ע״י הלקוח",             "Quote",    250),
        new TaskResultDefinitionRecord(TaskResultCodes.QuoteRejectedByClient,       "הצעה נדחתה ע״י הלקוח",             "Quote",    260),

        // Work order
        new TaskResultDefinitionRecord(TaskResultCodes.WorkOrderReceived,           "התקבלה הזמנת עבודה",               "WorkOrder",300),
        new TaskResultDefinitionRecord(TaskResultCodes.WorkOrderFiled,              "הזמנת עבודה תויקה",                "WorkOrder",310),

        // Execution material
        new TaskResultDefinitionRecord(TaskResultCodes.MaterialComplete,            "חומר לביצוע הושלם",                "Material", 400),
        new TaskResultDefinitionRecord(TaskResultCodes.MaterialMissing,             "חסר חומר לביצוע",                  "Material", 410),
        new TaskResultDefinitionRecord(TaskResultCodes.MissingMaterialRequestSent,  "נשלחה דרישה להשלמת חומר",          "Material", 420),
        new TaskResultDefinitionRecord(TaskResultCodes.MissingMaterialReceived,     "התקבלה השלמת חומר",                "Material", 430),

        // Design
        new TaskResultDefinitionRecord(TaskResultCodes.DesignDraftCompleted,        "טיוטת תכנון הושלמה",               "Design",   500),
        new TaskResultDefinitionRecord(TaskResultCodes.PreliminaryDesignCompleted,  "תכנון מוקדם הושלם",                 "Design",   510),
        new TaskResultDefinitionRecord(TaskResultCodes.DetailedDesignCompleted,     "תכנון מפורט הושלם",                 "Design",   520),

        // Authority approval
        new TaskResultDefinitionRecord(TaskResultCodes.SubmittedForApproval,        "הוגש לאישור",                       "Approval", 600),
        new TaskResultDefinitionRecord(TaskResultCodes.AuthorityCommentsReceived,   "התקבלו הערות גורם מאשר",            "Approval", 610),
        new TaskResultDefinitionRecord(TaskResultCodes.AuthorityApproved,           "אושר ע״י גורם מאשר",                "Approval", 620),
        new TaskResultDefinitionRecord(TaskResultCodes.CorrectionsRequired,         "נדרשות תיקונים",                    "Approval", 630),
        new TaskResultDefinitionRecord(TaskResultCodes.CorrectionsCompleted,        "תיקונים הושלמו",                    "Approval", 640),

        // Work plans
        new TaskResultDefinitionRecord(TaskResultCodes.WorkPlansCompleted,          "תוכניות עבודה הושלמו",              "WorkPlans",700),
        new TaskResultDefinitionRecord(TaskResultCodes.WorkPlansDelivered,          "תוכניות עבודה נמסרו",               "WorkPlans",710),

        // Billing
        new TaskResultDefinitionRecord(TaskResultCodes.BillingMilestoneReached,     "הגיע אבן דרך לחשבון",                "Billing",  800),
        new TaskResultDefinitionRecord(TaskResultCodes.BillRequired,                "נדרש להוציא חשבון",                  "Billing",  810),
        new TaskResultDefinitionRecord(TaskResultCodes.BillNotRequired,             "לא נדרש חשבון בשלב זה",              "Billing",  820),
        new TaskResultDefinitionRecord(TaskResultCodes.BillPrepared,                "חשבון הוכן",                         "Billing",  830),
        new TaskResultDefinitionRecord(TaskResultCodes.BillSubmitted,               "חשבון הוגש",                         "Billing",  840),
        new TaskResultDefinitionRecord(TaskResultCodes.BillApproved,                "חשבון אושר",                         "Billing",  850),

        // Close
        new TaskResultDefinitionRecord(TaskResultCodes.ProjectClosed,               "פרויקט נסגר",                        "Close",    900),
        new TaskResultDefinitionRecord(TaskResultCodes.ProjectCloseApproved,        "אישור סגירת פרויקט",                  "Close",    910),
        new TaskResultDefinitionRecord(TaskResultCodes.ProjectCloseRejected,        "סגירה נדחתה",                         "Close",    920),
        new TaskResultDefinitionRecord(TaskResultCodes.ProjectCloseNeedsMoreInfo,   "דרוש מידע נוסף לפני סגירה",           "Close",    930),

        // ─── Review workflow (REV.*) ─────────────────────────────────────────
        new TaskResultDefinitionRecord(TaskResultCodes.RequestFromMunicipality,        "פנייה מהרשות",                    "Review", 1000),
        new TaskResultDefinitionRecord(TaskResultCodes.RequestFromPlanner,             "פנייה מהמתכנן",                   "Review", 1010),
        new TaskResultDefinitionRecord(TaskResultCodes.MunicipalityRequestReceived,    "התקבלה פנייה רשמית מהרשות",        "Review", 1020),
        new TaskResultDefinitionRecord(TaskResultCodes.ProjectOpened,                  "פרויקט נפתח",                      "Review", 1030),
        new TaskResultDefinitionRecord(TaskResultCodes.ProfessionalReviewCompleted,    "בדיקה מקצועית הושלמה",             "Review", 1040),
        new TaskResultDefinitionRecord(TaskResultCodes.ManagerApproved,                "מנהל אישר",                        "Review", 1050),
        new TaskResultDefinitionRecord(TaskResultCodes.ManagerRequestedChanges,        "מנהל ביקש תיקונים",                 "Review", 1060),
        new TaskResultDefinitionRecord(TaskResultCodes.CommentsSentToPlanner,          "הערות נשלחו למתכנן",                "Review", 1070),
        new TaskResultDefinitionRecord(TaskResultCodes.PlannerCorrectionsReceived,     "התקבלו תיקוני מתכנן",               "Review", 1080),
        new TaskResultDefinitionRecord(TaskResultCodes.RecheckPassed,                  "בדיקה חוזרת עברה",                  "Review", 1090),
        new TaskResultDefinitionRecord(TaskResultCodes.RecheckRequiresMoreCorrections, "בדיקה חוזרת דורשת תיקונים נוספים",   "Review", 1100),
        new TaskResultDefinitionRecord(TaskResultCodes.PrincipallyApproved,            "אושר עקרונית",                      "Review", 1110),
        new TaskResultDefinitionRecord(TaskResultCodes.PoliceApprovalRequired,         "נדרש אישור משטרה",                  "Review", 1120),
        new TaskResultDefinitionRecord(TaskResultCodes.PoliceApprovalNotRequired,      "אינו דורש אישור משטרה",             "Review", 1130),
        new TaskResultDefinitionRecord(TaskResultCodes.SubmittedToPolice,              "הוגש למשטרה",                       "Review", 1140),
        new TaskResultDefinitionRecord(TaskResultCodes.PoliceApproved,                 "אושר ע״י משטרה",                    "Review", 1150),
        new TaskResultDefinitionRecord(TaskResultCodes.PoliceCommentsReceived,         "התקבלו הערות משטרה",                "Review", 1160),
        new TaskResultDefinitionRecord(TaskResultCodes.PoliceCorrectionsReceived,      "התקבלו תיקונים בעקבות הערות משטרה", "Review", 1170),
        new TaskResultDefinitionRecord(TaskResultCodes.ReviewProjectClosed,            "פרויקט בדיקה נסגר",                  "Review", 1180),

        // ─── Opinion workflow (OPN.*) ────────────────────────────────────────
        new TaskResultDefinitionRecord(TaskResultCodes.OpinionAnalysisCompleted,       "ניתוח מסמכים לחוות דעת הושלם",       "Opinion", 1200),
        new TaskResultDefinitionRecord(TaskResultCodes.OpinionDraftPrepared,           "טיוטת חוות דעת מוכנה",               "Opinion", 1210),
        new TaskResultDefinitionRecord(TaskResultCodes.OpinionRequiresRevision,        "חוות דעת דורשת תיקונים",             "Opinion", 1220),
        new TaskResultDefinitionRecord(TaskResultCodes.OpinionApprovedInternally,      "חוות דעת אושרה פנימית",              "Opinion", 1230),
        new TaskResultDefinitionRecord(TaskResultCodes.OpinionSent,                    "חוות דעת נשלחה",                      "Opinion", 1240),
    };

    public record TaskResultDefinitionRecord(string Code, string Name, string? Category, int SortOrder);
}
