using SiNet.Infrastructure.Sql.Constants;

namespace SiNet.Infrastructure.Sql.Services.SeedData;

/// <summary>
/// Seed data definitions for TaskType table.
/// These are the baseline task types that should exist in the system.
/// Includes the legacy generic types plus the full PlanningWorkflow taxonomy
/// (<see cref="TaskTypeCodes"/>) used for planning tasks and ProjectType disciplines.
/// </summary>
public static class TaskTypeSeedData
{
    /// <summary>
    /// Baseline TaskType definitions.
    /// Code is the stable machine identifier; Name is the editable Hebrew display name.
    /// Id is informational only — inserts dedup by <see cref="TaskTypeDefinition.Code"/>.
    /// </summary>
    public static readonly TaskTypeDefinition[] Definitions = new[]
    {
        // Legacy / generic
        new TaskTypeDefinition(1, Code: "General",             Name: "כללי",            IsActive: true, SortOrder: 1),
        new TaskTypeDefinition(2, Code: "OfficePlanning",      Name: "תכנון במשרד",     IsActive: true, SortOrder: 2),
        new TaskTypeDefinition(3, Code: "PlanReview",          Name: "בדיקת תוכנית",    IsActive: true, SortOrder: 3),

        // PlanningWorkflow taxonomy — Intake / Quote
        new TaskTypeDefinition(100, TaskTypeCodes.IdentifyQuoteRequest,           "זיהוי בקשת הצעת מחיר",        true, 100),
        new TaskTypeDefinition(101, TaskTypeCodes.OpenQuoteProject,               "פתיחת פרויקט הצעת מחיר",      true, 101),
        new TaskTypeDefinition(102, TaskTypeCodes.FileInitialInquiry,             "תיוק פנייה ראשונית",          true, 102),
        new TaskTypeDefinition(103, TaskTypeCodes.FileQuoteMaterial,              "תיוק חומר להצעת מחיר",        true, 103),
        new TaskTypeDefinition(104, TaskTypeCodes.CheckQuoteMaterialCompleteness, "בדיקת שלמות חומר",            true, 104),
        new TaskTypeDefinition(105, TaskTypeCodes.PrepareMissingMaterialList,     "הכנת רשימת חוסרים",           true, 105),
        new TaskTypeDefinition(106, TaskTypeCodes.SendMissingMaterialRequest,     "שליחת בקשת חומר חסר",         true, 106),
        new TaskTypeDefinition(107, TaskTypeCodes.FollowMissingMaterial,          "מעקב אחר חומר חסר",           true, 107),
        new TaskTypeDefinition(108, TaskTypeCodes.PrepareQuoteCalculation,        "הכנת אומדן הצעה",             true, 108),
        new TaskTypeDefinition(109, TaskTypeCodes.PrepareQuoteDocument,           "הכנת מסמך הצעת מחיר",         true, 109),
        new TaskTypeDefinition(110, TaskTypeCodes.ApproveQuoteInternal,           "אישור פנימי להצעה",           true, 110),
        new TaskTypeDefinition(111, TaskTypeCodes.ReviseQuote,                    "תיקון הצעת מחיר",             true, 111),
        new TaskTypeDefinition(112, TaskTypeCodes.SendQuoteToClient,              "שליחת הצעה ללקוח",            true, 112),
        new TaskTypeDefinition(113, TaskTypeCodes.FollowQuoteApproval,            "מעקב אישור הצעה",             true, 113),
        new TaskTypeDefinition(114, TaskTypeCodes.FollowWorkOrder,                "מעקב הזמנת עבודה",            true, 114),
        new TaskTypeDefinition(115, TaskTypeCodes.FileWorkOrder,                  "תיוק הזמנת עבודה",            true, 115),
        new TaskTypeDefinition(116, TaskTypeCodes.ActivateProject,                "הפעלת הפרויקט",                true, 116),

        // Execution material & planning package
        new TaskTypeDefinition(120, TaskTypeCodes.CheckExecutionMaterialCompleteness, "בדיקת שלמות חומר ביצוע",  true, 120),
        new TaskTypeDefinition(121, TaskTypeCodes.FileExecutionMaterial,           "תיוק חומר ביצוע",            true, 121),
        new TaskTypeDefinition(122, TaskTypeCodes.OpenPlanningWorkPackage,         "פתיחת חבילת תכנון",          true, 122),
        new TaskTypeDefinition(123, TaskTypeCodes.AssignPlanningTasks,             "חלוקת משימות תכנון",         true, 123),

        // Disciplines
        new TaskTypeDefinition(130, TaskTypeCodes.GeneralPlanning,                 "תכנון כללי",                  true, 130),
        new TaskTypeDefinition(131, TaskTypeCodes.TrafficPlanning,                 "תכנון תנועה",                 true, 131),
        new TaskTypeDefinition(132, TaskTypeCodes.DrainagePlanning,                "תכנון ניקוז",                 true, 132),
        new TaskTypeDefinition(133, TaskTypeCodes.PhysicalPlanning,                "תכנון פיזי",                  true, 133),
        new TaskTypeDefinition(134, TaskTypeCodes.ExternalPlannerCoordination,     "תיאום מתכנן חיצוני",          true, 134),

        // Design progression
        new TaskTypeDefinition(140, TaskTypeCodes.PrepareDraftPlans,               "הכנת תוכניות טיוטה",          true, 140),
        new TaskTypeDefinition(141, TaskTypeCodes.PreparePreliminaryDesign,        "הכנת תכנון מוקדם",            true, 141),
        new TaskTypeDefinition(142, TaskTypeCodes.PrepareDetailedDesign,           "הכנת תכנון מפורט",            true, 142),
        new TaskTypeDefinition(143, TaskTypeCodes.InternalPlanReview,              "בדיקה פנימית",                true, 143),
        new TaskTypeDefinition(144, TaskTypeCodes.HandleReviewComments,            "טיפול בהערות בדיקה",          true, 144),

        // Authority approval
        new TaskTypeDefinition(150, TaskTypeCodes.PrepareSubmissionSet,            "הכנת חבילת הגשה",             true, 150),
        new TaskTypeDefinition(151, TaskTypeCodes.SubmitForApproval,               "הגשה לאישור",                  true, 151),
        new TaskTypeDefinition(152, TaskTypeCodes.FollowAuthorityApproval,         "מעקב אישור רשות",             true, 152),
        new TaskTypeDefinition(153, TaskTypeCodes.HandleAuthorityComments,         "טיפול בהערות רשות",           true, 153),

        // Work plans / delivery
        new TaskTypeDefinition(160, TaskTypeCodes.PrepareWorkPlans,                "הכנת תוכניות עבודה",          true, 160),
        new TaskTypeDefinition(161, TaskTypeCodes.FinalPlanReview,                 "בדיקה סופית",                  true, 161),
        new TaskTypeDefinition(162, TaskTypeCodes.DeliverWorkPlans,                "מסירת תוכניות עבודה",         true, 162),

        // Billing & close
        new TaskTypeDefinition(170, TaskTypeCodes.CheckBillingMilestone,           "בדיקת אבן דרך לחיוב",         true, 170),
        new TaskTypeDefinition(171, TaskTypeCodes.PrepareBill,                     "הכנת חשבון",                   true, 171),
        new TaskTypeDefinition(172, TaskTypeCodes.SubmitBill,                      "הגשת חשבון",                   true, 172),
        new TaskTypeDefinition(173, TaskTypeCodes.FollowBillApproval,              "מעקב אישור חשבון",            true, 173),
        new TaskTypeDefinition(174, TaskTypeCodes.CloseBillingBalance,             "סגירת יתרת חיוב",             true, 174),
        new TaskTypeDefinition(175, TaskTypeCodes.CloseProject,                    "סגירת פרויקט",                true, 175),

        // ─── Review workflow task types ────────────────────────────────────
        new TaskTypeDefinition(200, TaskTypeCodes.RequestMunicipalityInvitation,   "בקשת פנייה רשמית מהרשות",     true, 200),
        new TaskTypeDefinition(201, TaskTypeCodes.TrackMunicipalityInvitation,     "מעקב פנייה מהרשות",            true, 201),
        new TaskTypeDefinition(202, TaskTypeCodes.OpenReviewProject,               "פתיחת בדיקה חדשה",             true, 202),
        new TaskTypeDefinition(203, TaskTypeCodes.OpenProject,                     "פתיחת פרויקט",                 true, 203),
         new TaskTypeDefinition(204, TaskTypeCodes.FileInitialMaterials,            "תיוק חומר ראשוני",             true, 204),
        // REV.Intake classification-only task. Modeled after IdentifyQuoteRequest
        // (id 100): ProjectWork host, WorkflowResultRecorded policy. Required by
        // ReviewTaskInteractionRegistry — must appear in TaskTypeSeedData.
        new TaskTypeDefinition(205, TaskTypeCodes.ClassifyRequestSource,            "סיווג מקור הפנייה",            true, 205),
        // Note: "בדיקת שלמות חומר" is shared with Quote (id 104,
        // TaskTypeCodes.CheckQuoteMaterialCompleteness). Review reuses that
        // existing TaskType — no separate row is seeded here.
        new TaskTypeDefinition(206, TaskTypeCodes.RequestMissingMaterial,          "בקשה להשלמת חומר חסר",         true, 206),
        new TaskTypeDefinition(207, TaskTypeCodes.TrackMissingMaterial,            "מעקב חומר חסר",                true, 207),
        new TaskTypeDefinition(208, TaskTypeCodes.FileCorrectedMaterials,          "תיוק חומר מתוקן",              true, 208),
        new TaskTypeDefinition(209, TaskTypeCodes.PerformProfessionalReview,       "ביצוע בדיקה מקצועית",          true, 209),
        new TaskTypeDefinition(210, TaskTypeCodes.FixReportPerManager,             "תיקון דוח לפי מנהל",           true, 210),
        new TaskTypeDefinition(211, TaskTypeCodes.ApproveReviewReport,             "אישור דוח בדיקה",              true, 211),
        new TaskTypeDefinition(212, TaskTypeCodes.ResubmitToManager,               "הגשה חוזרת למנהל",             true, 212),
        new TaskTypeDefinition(213, TaskTypeCodes.SendInternalApproval,            "שליחת אישור פנימי",            true, 213),
        new TaskTypeDefinition(214, TaskTypeCodes.SendReportToPlanner,             "שליחת דוח למתכנן",             true, 214),
        new TaskTypeDefinition(215, TaskTypeCodes.TrackPlannerCorrections,         "מעקב תיקוני מתכנן",            true, 215),
        new TaskTypeDefinition(216, TaskTypeCodes.RecheckPlan,                     "בדיקה חוזרת",                  true, 216),
        new TaskTypeDefinition(217, TaskTypeCodes.IssueApproval,                   "הוצאת אישור",                  true, 217),
        new TaskTypeDefinition(218, TaskTypeCodes.PreparePoliceSubmission,         "הכנת הגשה למשטרה",             true, 218),
        new TaskTypeDefinition(219, TaskTypeCodes.SubmitToPolice,                  "הגשה למשטרה",                  true, 219),
        new TaskTypeDefinition(220, TaskTypeCodes.TrackPoliceApproval,             "מעקב אישור משטרה",             true, 220),
        new TaskTypeDefinition(221, TaskTypeCodes.ForwardPoliceCommentsToPlanner,  "העברת הערות משטרה למתכנן",     true, 221),
        new TaskTypeDefinition(222, TaskTypeCodes.FileFinalApprovals,              "תיוק אישורים סופיים",          true, 222),
        new TaskTypeDefinition(223, TaskTypeCodes.CloseProjectTask,                "סגירת פרויקט בדיקה",            true, 223),

        // ─── Opinion workflow task types (OPN.*) ─────────────────────────
        // OPN.ReceiveMaterial reuses FileInitialMaterials (id 204).
        // OPN.RequestMissingMaterial reuses RequestMissingMaterial (id 206).
        new TaskTypeDefinition(230, TaskTypeCodes.AnalyzeOpinionMaterials,         "ניתוח חומר לחוות דעת",         true, 230),
        new TaskTypeDefinition(231, TaskTypeCodes.PrepareOpinionDraft,             "הכנת טיוטת חוות דעת",          true, 231),
        new TaskTypeDefinition(232, TaskTypeCodes.ReviewOpinionInternal,           "בדיקה / אישור פנימי",          true, 232),
        new TaskTypeDefinition(233, TaskTypeCodes.UpdateOpinionDraft,              "עדכון טיוטת חוות דעת",         true, 233),
        new TaskTypeDefinition(234, TaskTypeCodes.SendOpinion,                     "שליחת חוות דעת",                true, 234),
    };

    /// <summary>
    /// Represents a TaskType seed definition.
    /// </summary>
    public record TaskTypeDefinition(int Id, string Code, string Name, bool IsActive, int SortOrder);
}
