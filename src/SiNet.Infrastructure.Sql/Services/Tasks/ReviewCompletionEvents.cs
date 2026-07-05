namespace SiNet.Infrastructure.Sql.Services.Tasks;

/// <summary>
/// Stable event codes reported by UI components back to the task layer
/// when meaningful work happens. Consumed by completion-policy evaluators
/// to translate UI events into <c>TaskResult</c> records and stage advancement.
/// </summary>
public static class ReviewCompletionEvents
{
    public const string ReviewProjectCreated                  = "Review.ProjectCreated";
    public const string ReviewMunicipalityInvitationReceived  = "Review.MunicipalityInvitationReceived";
    public const string ReviewQuoteRequestClassified          = "Review.QuoteRequestClassified";
    public const string ReviewMaterialFiled                   = "Review.MaterialFiled";
    public const string ReviewMaterialCheckCompleted          = "Review.MaterialCheckCompleted";
    public const string ReviewProfessionalReviewCompleted     = "Review.ProfessionalReviewCompleted";
    public const string ReviewManagerApproved                 = "Review.ManagerApproved";
    public const string ReviewManagerRequestedChanges         = "Review.ManagerRequestedChanges";
    public const string ReviewCommentsSentToPlanner           = "Review.CommentsSentToPlanner";
    public const string ReviewPlannerCorrectionsReceived      = "Review.PlannerCorrectionsReceived";
    public const string ReviewRecheckPassed                   = "Review.RecheckPassed";
    public const string ReviewRecheckRequiresMoreCorrections  = "Review.RecheckRequiresMoreCorrections";
    public const string ReviewPrincipallyApproved             = "Review.PrincipallyApproved";
    public const string ReviewSubmittedToPolice               = "Review.SubmittedToPolice";
    public const string ReviewPoliceApproved                  = "Review.PoliceApproved";
    public const string ReviewPoliceCommentsReceived          = "Review.PoliceCommentsReceived";
    public const string ReviewPoliceCorrectionsReceived       = "Review.PoliceCorrectionsReceived";
    public const string ReviewProjectClosed                   = "Review.ProjectClosed";

    // ───────────────────────────────────────────────────────────────────
    // Generic, workflow-agnostic completion events (per WorkflowDecisions
    // §1 — Completion Events must be generic and reusable). The "Review."
    // prefix is historical; this catalog is shared across Proposal,
    // Planning, Opinion, Review, and MaterialIntake workflows.
    // ───────────────────────────────────────────────────────────────────

    /// <summary>Generic: a request source (e.g. planner / municipality) was classified.</summary>
    public const string RequestSourceClassified               = "Review.RequestSourceClassified";

    /// <summary>Generic: a work order was received and recorded.</summary>
    public const string WorkOrderReceived                     = "Review.WorkOrderReceived";

    /// <summary>Generic: a missing-material follow-up was updated (sent or received).</summary>
    public const string MissingMaterialUpdated                = "Review.MissingMaterialUpdated";

    /// <summary>Generic: analysis of received materials was completed (or flagged missing material).</summary>
    public const string AnalysisCompleted                     = "Review.AnalysisCompleted";

    /// <summary>Generic: a draft document was prepared.</summary>
    public const string DraftPrepared                         = "Review.DraftPrepared";

    /// <summary>Generic: an internal review of a draft was completed.</summary>
    public const string InternalReviewCompleted               = "Review.InternalReviewCompleted";

    /// <summary>Generic: a draft document was updated after internal review.</summary>
    public const string DraftUpdated                          = "Review.DraftUpdated";

    /// <summary>Generic: a finalized document was sent to its recipient.</summary>
    public const string DocumentSent                          = "Review.DocumentSent";

    /// <summary>Generic: a billing milestone was evaluated.</summary>
    public const string BillingMilestoneChecked               = "Review.BillingMilestoneChecked";
}
