namespace SiNet.Infrastructure.Sql.Constants;

/// <summary>Stable codes for <see cref="Models.TaskResultDefinition.Code"/>.</summary>
public static class TaskResultCodes
{
    // Lead / Quote intake
    public const string QuoteRequestDetected = "QuoteRequestDetected";
    public const string NotQuoteRequest = "NotQuoteRequest";

    // Quote material
    public const string QuoteMaterialComplete = "QuoteMaterialComplete";
    public const string QuoteMaterialMissing = "QuoteMaterialMissing";

    // Quote preparation
    public const string QuoteCalculationCompleted = "QuoteCalculationCompleted";
    public const string QuotePrepared = "QuotePrepared";
    public const string QuoteApprovedInternally = "QuoteApprovedInternally";
    public const string QuoteRequiresRevision = "QuoteRequiresRevision";
    public const string QuoteSent = "QuoteSent";
    public const string QuoteApprovedByClient = "QuoteApprovedByClient";
    public const string QuoteRejectedByClient = "QuoteRejectedByClient";

    /// <summary>Client follow-up closed without a response (cancel / no reply).</summary>
    public const string QuoteCancelledNoResponse = "QuoteCancelledNoResponse";

    // Work order
    public const string WorkOrderReceived = "WorkOrderReceived";
    public const string WorkOrderFiled = "WorkOrderFiled";

    // Execution material
    public const string MaterialComplete = "MaterialComplete";
    public const string MaterialMissing = "MaterialMissing";
    public const string MissingMaterialRequestSent = "MissingMaterialRequestSent";
    public const string MissingMaterialReceived = "MissingMaterialReceived";

    // Design
    public const string DesignDraftCompleted = "DesignDraftCompleted";
    public const string PreliminaryDesignCompleted = "PreliminaryDesignCompleted";
    public const string DetailedDesignCompleted = "DetailedDesignCompleted";

    // Authority approval
    public const string SubmittedForApproval = "SubmittedForApproval";
    public const string AuthorityCommentsReceived = "AuthorityCommentsReceived";
    public const string AuthorityApproved = "AuthorityApproved";
    public const string CorrectionsRequired = "CorrectionsRequired";
    public const string CorrectionsCompleted = "CorrectionsCompleted";

    // Work plans
    public const string WorkPlansCompleted = "WorkPlansCompleted";
    public const string WorkPlansDelivered = "WorkPlansDelivered";

    // Billing
    public const string BillingMilestoneReached = "BillingMilestoneReached";
    public const string BillRequired = "BillRequired";
    public const string BillNotRequired = "BillNotRequired";
    public const string BillPrepared = "BillPrepared";
    public const string BillSubmitted = "BillSubmitted";
    public const string BillApproved = "BillApproved";

    // Close
    public const string ProjectClosed = "ProjectClosed";

    // ─── Generic project close decision (issued by OfficeManagement) ───────
    /// <summary>העובד המנהלי אישר את סגירת הפרויקט.</summary>
    public const string ProjectCloseApproved      = "ProjectCloseApproved";
    /// <summary>העובד המנהלי דחה את סגירת הפרויקט.</summary>
    public const string ProjectCloseRejected      = "ProjectCloseRejected";
    /// <summary>חסר מידע לפני שניתן להחליט על סגירת הפרויקט.</summary>
    public const string ProjectCloseNeedsMoreInfo = "ProjectCloseNeedsMoreInfo";

    // ─── Review workflow (REV.*) ────────────────────────────────────────────
    public const string RequestFromMunicipality        = "RequestFromMunicipality";
    public const string RequestFromPlanner             = "RequestFromPlanner";
    public const string MunicipalityRequestReceived    = "MunicipalityRequestReceived";
    public const string ProjectOpened                  = "ProjectOpened";
    public const string ProfessionalReviewCompleted    = "ProfessionalReviewCompleted";
    public const string ManagerApproved                = "ManagerApproved";
    public const string ManagerRequestedChanges        = "ManagerRequestedChanges";
    public const string CommentsSentToPlanner          = "CommentsSentToPlanner";
    public const string PlannerCorrectionsReceived     = "PlannerCorrectionsReceived";
    public const string RecheckPassed                  = "RecheckPassed";
    public const string RecheckRequiresMoreCorrections = "RecheckRequiresMoreCorrections";
    public const string PrincipallyApproved            = "PrincipallyApproved";
    public const string PoliceApprovalRequired         = "PoliceApprovalRequired";
    public const string PoliceApprovalNotRequired      = "PoliceApprovalNotRequired";
    public const string SubmittedToPolice              = "SubmittedToPolice";
    public const string PoliceApproved                 = "PoliceApproved";
    public const string PoliceCommentsReceived         = "PoliceCommentsReceived";
    public const string PoliceCorrectionsReceived      = "PoliceCorrectionsReceived";
    public const string ReviewProjectClosed            = "ReviewProjectClosed";

    // ─── Opinion workflow (OPN.*) ──────────────────────────────────────────
    /// <summary>ניתוח המסמכים הסתיים — מעבר להכנת טיוטה.</summary>
    public const string OpinionAnalysisCompleted   = "OpinionAnalysisCompleted";
    /// <summary>טיוטת חוות הדעת מוכנה — מעבר לבדיקה פנימית.</summary>
    public const string OpinionDraftPrepared       = "OpinionDraftPrepared";
    /// <summary>הבדיקה הפנימית דורשת תיקונים — חזרה לעדכון חוות הדעת.</summary>
    public const string OpinionRequiresRevision    = "OpinionRequiresRevision";
    /// <summary>חוות הדעת אושרה פנימית — מוכנה לשליחה.</summary>
    public const string OpinionApprovedInternally  = "OpinionApprovedInternally";
    /// <summary>חוות הדעת נשלחה — מעבר לסגירה.</summary>
    public const string OpinionSent                = "OpinionSent";
}
