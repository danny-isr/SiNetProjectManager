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

    /// <summary>Post-recheck decision on whether police/authority approval is required (REV.PoliceApprovalDecision).</summary>
    public const string ReviewPoliceRequirementDecided        = "Review.PoliceRequirementDecided";

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

    /// <summary>Generic: a quote calculation was completed (Proposal PRP.Calculation).</summary>
    public const string QuoteCalculationCompleted             = "Review.QuoteCalculationCompleted";

    /// <summary>Generic: a quote document was prepared (Proposal PRP.Preparation).</summary>
    public const string QuoteDocumentPrepared                 = "Review.QuoteDocumentPrepared";

    /// <summary>Generic: a quote was internally approved or sent back for revision (Proposal PRP.InternalApproval).</summary>
    public const string QuoteInternallyApproved               = "Review.QuoteInternallyApproved";

    /// <summary>Generic: a quote was sent to the client (Proposal PRP.SendQuote).</summary>
    public const string QuoteSentToClient                     = "Review.QuoteSentToClient";

    /// <summary>Generic: a sent quote's client decision was tracked — approved/rejected/cancelled (Proposal PRP.SentFollowUp).</summary>
    public const string QuoteApprovalTracked                  = "Review.QuoteApprovalTracked";

    /// <summary>Generic: an administrative project-close decision was made — approved/rejected/needs-more-info (REV.Close / PLN.Close CloseProject task).</summary>
    public const string ProjectCloseDecided                   = "Review.ProjectCloseDecided";

    // ───────────────────────────────────────────────────────────────────
    // Outsourcing (OUT.*) — explicit completion events with no TaskResult.
    // Workflow transitions remain AllRequiredTasksClosed + AllTasksComplete;
    // these events only close the associated driving task so auto-advance can run.
    // ───────────────────────────────────────────────────────────────────

    /// <summary>OUT.ReceiveOffer — outsource quote received; closes ReceiveOutsourceQuote.</summary>
    public const string OutsourceQuoteReceived                = "Review.OutsourceQuoteReceived";

    /// <summary>OUT.ApproveOffer — outsource offer approved; closes ApproveOutsourceQuote.</summary>
    public const string OutsourceOfferApproved                = "Review.OutsourceOfferApproved";

    /// <summary>OUT.MonitorPayments — outsource payments completed; closes MonitorOutsourcePayments.</summary>
    public const string OutsourcePaymentsCompleted            = "Review.OutsourcePaymentsCompleted";
}
