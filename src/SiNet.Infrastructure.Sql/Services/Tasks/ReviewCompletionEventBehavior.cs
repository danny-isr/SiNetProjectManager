using SiNet.Infrastructure.Sql.Constants;

namespace SiNet.Infrastructure.Sql.Services.Tasks;

/// <summary>
/// Declarative table mapping <see cref="ReviewCompletionEvents"/> codes to the
/// concrete behavior the <see cref="TaskCompletionCoordinator"/> must perform.
/// <para>
/// The coordinator is the single decision point — UI components only emit
/// event codes; this table tells the coordinator which task type the event
/// applies to, which results are allowed, what project-status to set, and
/// whether the workflow should be auto-advanced afterwards.
/// </para>
/// <para>
/// Project statuses MUST come from the broad <see cref="ProjectStatusCodes"/>
/// list — no Review-stage-specific values.
/// </para>
/// </summary>
public sealed record ReviewCompletionBehavior(
    string EventCode,
    IReadOnlyList<string> ApplicableTaskTypeCodes,
    IReadOnlyList<string> AllowedTaskResultCodes,
    string? NewProjectStatusCode,
    bool RequestWorkflowAdvance,
    // When true, the coordinator MUST close the associated task on this event
    // regardless of the interaction's AutoCloseOnCompletion flag and regardless
    // of WorkTarget completion. This is the per-event escape hatch for events
    // that semantically mean "the work this task represents is done" (e.g.
    // ReviewMaterialFiled raised by MoveToProjectProcessActionHandler after a
    // successful filing). It is intentionally per-event so partial-fill events
    // on the same TaskType still respect the conservative default.
    bool ClosesAssociatedTask = false);

public static class ReviewCompletionEventBehavior
{
    private static readonly IReadOnlyDictionary<string, ReviewCompletionBehavior> _byEvent = Build();

    public static IReadOnlyCollection<ReviewCompletionBehavior> All => (IReadOnlyCollection<ReviewCompletionBehavior>)_byEvent.Values;

    public static ReviewCompletionBehavior? TryGet(string eventCode) =>
        _byEvent.TryGetValue(eventCode, out var b) ? b : null;

    /// <summary>
    /// Reverse lookup: resolves the single completion-event code for a task type, but <b>only when it
    /// is unambiguous</b>. Returns the code when <b>exactly one</b> <see cref="ReviewCompletionBehavior"/>
    /// lists <paramref name="taskTypeCode"/> in its <see cref="ReviewCompletionBehavior.ApplicableTaskTypeCodes"/>;
    /// returns <see langword="null"/> when zero or more than one behavior applies.
    /// <para>
    /// This is the no-guessing rule used by the host to auto-fill the completion event for a Work
    /// Surface: task types whose <em>result</em> selects between several events (e.g.
    /// <c>RecheckPlan</c>, which maps to both <c>ReviewRecheckPassed</c> and
    /// <c>ReviewRecheckRequiresMoreCorrections</c>) intentionally return <see langword="null"/> here so
    /// the caller keeps asking for an explicit event rather than picking one arbitrarily. Pure and
    /// read-only — it never mutates state.
    /// </para>
    /// </summary>
    public static string? TryResolveUniqueEventCodeForTaskType(string? taskTypeCode)
    {
        if (string.IsNullOrWhiteSpace(taskTypeCode))
            return null;

        string? found = null;
        foreach (var b in _byEvent.Values)
        {
            if (!b.ApplicableTaskTypeCodes.Contains(taskTypeCode, StringComparer.Ordinal))
                continue;

            if (found is not null)
                return null; // more than one event applies — ambiguous, do not guess.

            found = b.EventCode;
        }

        return found;
    }

    /// <summary>
    /// Reverse lookup keyed on the <b>(task type, result)</b> pair. Resolves the completion-event
    /// code when <b>exactly one</b> <see cref="ReviewCompletionBehavior"/> both lists
    /// <paramref name="taskTypeCode"/> in its <see cref="ReviewCompletionBehavior.ApplicableTaskTypeCodes"/>
    /// and allows <paramref name="taskResultCode"/> in its
    /// <see cref="ReviewCompletionBehavior.AllowedTaskResultCodes"/>; returns <see langword="false"/>
    /// (with <paramref name="completionEventCode"/> set to <see langword="null"/>) when zero or more than
    /// one behavior matches.
    /// <para>
    /// This is the safe branch-selector for task types whose <em>result</em> chooses between several
    /// events (e.g. <c>RecheckPlan</c> → <c>ReviewRecheckPassed</c> / <c>ReviewRecheckRequiresMoreCorrections</c>).
    /// It reuses the same declarative table the coordinator validates against — no new mapping source and
    /// no guessing. When <paramref name="taskResultCode"/> is <see langword="null"/>/whitespace it falls back
    /// to the unique-by-task-type rule via <see cref="TryResolveUniqueEventCodeForTaskType"/>, so callers can
    /// use one entry point for both the unambiguous and the branching cases. Pure and read-only — it never
    /// mutates state.
    /// </para>
    /// </summary>
    public static bool TryResolveEventCodeForTaskTypeAndResult(
        string? taskTypeCode,
        string? taskResultCode,
        out string? completionEventCode)
    {
        completionEventCode = null;

        if (string.IsNullOrWhiteSpace(taskTypeCode))
            return false;

        // No result selected yet: only resolve when the task type maps to a single event.
        if (string.IsNullOrWhiteSpace(taskResultCode))
        {
            completionEventCode = TryResolveUniqueEventCodeForTaskType(taskTypeCode);
            return completionEventCode is not null;
        }

        string? found = null;
        foreach (var b in _byEvent.Values)
        {
            if (!b.ApplicableTaskTypeCodes.Contains(taskTypeCode, StringComparer.Ordinal))
                continue;

            if (!b.AllowedTaskResultCodes.Contains(taskResultCode, StringComparer.Ordinal))
                continue;

            if (found is not null)
            {
                // More than one event allows this exact (task type, result) pair — ambiguous, do not guess.
                completionEventCode = null;
                return false;
            }

            found = b.EventCode;
        }

        completionEventCode = found;
        return found is not null;
    }

    private static IReadOnlyDictionary<string, ReviewCompletionBehavior> Build()
    {
        var rows = new ReviewCompletionBehavior[]
        {
            // ReviewProjectCreated is the canonical "project created from email"
            // event raised by the project-creation dialog after the new project
            // has been persisted. It closes any *OpenXProject task type whose
            // TaskInteractionDefinition is ProjectCreationFromEmail and whose
            // allowed result is ProjectOpened. The name is historical ("Review."
            // prefix on all coordinator events) but the semantics are generic;
            // do NOT split this into per-workflow events.
            //
            // Currently applies to:
            //   • OpenReviewProject  (Review workflow — REV.Intake → REV.MaterialIntake)
            //   • OpenQuoteProject   (Proposal workflow — PRP.ProjectSetup → PRP.FileMaterial)
            //
            // The Proposal stage transition PRP.ProjectSetup → PRP.FileMaterial
            // is keyed on TaskResultEquals("ProjectOpened"); without this
            // mapping the coordinator returns Success=false for OpenQuoteProject
            // and LastTaskResultId never gets set, so CheckAndAutoAdvanceAsync
            // reports "no transition" at runtime.
            new(ReviewCompletionEvents.ReviewProjectCreated,
                new[] { TaskTypeCodes.OpenReviewProject, TaskTypeCodes.OpenQuoteProject },
                new[] { TaskResultCodes.ProjectOpened },
                ProjectStatusCodes.Active,
                RequestWorkflowAdvance: true,
                ClosesAssociatedTask: true),

            new(ReviewCompletionEvents.ReviewMunicipalityInvitationReceived,
                new[] { TaskTypeCodes.TrackMunicipalityInvitation },
                new[] { TaskResultCodes.MunicipalityRequestReceived },
                ProjectStatusCodes.LeadReceived,
                RequestWorkflowAdvance: true),

            // IdentifyQuoteRequest — Intake classification-only completion.
            // ClosesAssociatedTask: classification IS the work (CreatePriceQuote /
            // RejectPriceQuote already chose the verdict). Without this flag, a
            // Pending email WorkTarget link blocks close and auto-advance
            // (taskClosed=False / willAutoAdvance=False) — seen in manual tests
            // when the intake task was reused across Proposal instances on the
            // office project.
            // Also applies to OpenQuoteProject when the operator declines at
            // ProjectSetup (CreatePriceQuote skips Intake) — NotQuoteRequest only;
            // QuoteRequestDetected is blocked by OpenQuoteProject's interaction allow-list.
            new(ReviewCompletionEvents.ReviewQuoteRequestClassified,
                new[] { TaskTypeCodes.IdentifyQuoteRequest, TaskTypeCodes.OpenQuoteProject },
                new[] { TaskResultCodes.QuoteRequestDetected, TaskResultCodes.NotQuoteRequest },
                NewProjectStatusCode: null,
                RequestWorkflowAdvance: true,
                ClosesAssociatedTask: true),

            // ReviewMaterialFiled is the canonical event raised by
            // MoveToProjectProcessActionHandler after a successful filing run.
            // It applies to all FileXMaterials task types. Per WorkflowDecisions
            // (round: FileInitialMaterials closure) this event SEMANTICALLY means
            // "the filing work this task represents is complete", so the coordinator
            // closes the task via the per-event ClosesAssociatedTask override even
            // when the interaction's AutoCloseOnCompletion is false (the safe
            // default for FileInitialMaterials, which keeps partial-fill UI events
            // from prematurely closing the task). RequestWorkflowAdvance is also
            // true so the orchestrator runs the next transition right after closure.
            new(ReviewCompletionEvents.ReviewMaterialFiled,
                new[]
                {
                    TaskTypeCodes.FileInitialMaterials,
                    TaskTypeCodes.FileCorrectedMaterials,
                    TaskTypeCodes.FileQuoteMaterial,
                },
                Array.Empty<string>(),
                NewProjectStatusCode: null,
                RequestWorkflowAdvance: true,
                ClosesAssociatedTask: true),

            // MaterialCheck: dual outcome — the operator's Completeness decision IS the work.
            // ClosesAssociatedTask: without it, a Pending MaterialChecklist WorkTarget blocks
            // close (taskClosed=False → willAutoAdvance=False) even though AutoCloseOnCompletion
            // is true — seen in manual ProjectWork completion (result recorded, workflow stuck).
            new(ReviewCompletionEvents.ReviewMaterialCheckCompleted,
                new[] { TaskTypeCodes.CheckQuoteMaterialCompleteness },
                new[] { TaskResultCodes.MaterialComplete, TaskResultCodes.MaterialMissing },
                NewProjectStatusCode: null, // resolved by result branch
                RequestWorkflowAdvance: true,
                ClosesAssociatedTask: true),

            // ClosesAssociatedTask: same lesson as ReviewMaterialCheckCompleted — AutoCloseOnCompletion
            // alone stalls when a stray non-report IsWorkTarget remains Pending (e.g. inherited email link).
            new(ReviewCompletionEvents.ReviewProfessionalReviewCompleted,
                new[] { TaskTypeCodes.PerformProfessionalReview, TaskTypeCodes.FixReportPerManager },
                new[] { TaskResultCodes.ProfessionalReviewCompleted },
                NewProjectStatusCode: null,
                RequestWorkflowAdvance: true,
                ClosesAssociatedTask: true),

            new(ReviewCompletionEvents.ReviewManagerApproved,
                new[] { TaskTypeCodes.ApproveReviewReport, TaskTypeCodes.ResubmitToManager },
                new[] { TaskResultCodes.ManagerApproved },
                NewProjectStatusCode: null,
                RequestWorkflowAdvance: true),

            new(ReviewCompletionEvents.ReviewManagerRequestedChanges,
                new[] { TaskTypeCodes.ApproveReviewReport, TaskTypeCodes.ResubmitToManager },
                new[] { TaskResultCodes.ManagerRequestedChanges },
                NewProjectStatusCode: null,
                RequestWorkflowAdvance: true),

            new(ReviewCompletionEvents.ReviewCommentsSentToPlanner,
                new[] { TaskTypeCodes.SendReportToPlanner, TaskTypeCodes.ForwardPoliceCommentsToPlanner },
                new[] { TaskResultCodes.CommentsSentToPlanner },
                ProjectStatusCodes.WaitingForClient,
                RequestWorkflowAdvance: true),

            new(ReviewCompletionEvents.ReviewPlannerCorrectionsReceived,
                new[] { TaskTypeCodes.TrackPlannerCorrections },
                new[] { TaskResultCodes.PlannerCorrectionsReceived },
                ProjectStatusCodes.Active,
                RequestWorkflowAdvance: true),

            new(ReviewCompletionEvents.ReviewRecheckPassed,
                new[] { TaskTypeCodes.RecheckPlan },
                new[] { TaskResultCodes.RecheckPassed },
                NewProjectStatusCode: null,
                RequestWorkflowAdvance: true),

            new(ReviewCompletionEvents.ReviewRecheckRequiresMoreCorrections,
                new[] { TaskTypeCodes.RecheckPlan },
                new[] { TaskResultCodes.RecheckRequiresMoreCorrections },
                ProjectStatusCodes.WaitingForClient,
                RequestWorkflowAdvance: true),

            // ReviewPoliceRequirementDecided — REV.PoliceApprovalDecision.
            // Required → police path (PoliceSubmission); NotRequired → Close.
            // Both outcomes advance the workflow; project-status is driven by
            // downstream stage transitions.
            new(ReviewCompletionEvents.ReviewPoliceRequirementDecided,
                new[] { TaskTypeCodes.DeterminePoliceApprovalRequirement },
                new[] { TaskResultCodes.PoliceApprovalRequired, TaskResultCodes.PoliceApprovalNotRequired },
                NewProjectStatusCode: null,
                RequestWorkflowAdvance: true),

            new(ReviewCompletionEvents.ReviewPrincipallyApproved,
                new[] { TaskTypeCodes.IssueApproval, TaskTypeCodes.SendInternalApproval },
                new[] { TaskResultCodes.PrincipallyApproved },
                NewProjectStatusCode: null,
                RequestWorkflowAdvance: true),

            new(ReviewCompletionEvents.ReviewSubmittedToPolice,
                new[] { TaskTypeCodes.SubmitToPolice, TaskTypeCodes.PreparePoliceSubmission },
                new[] { TaskResultCodes.SubmittedToPolice },
                ProjectStatusCodes.WaitingForAuthority,
                RequestWorkflowAdvance: true),

            new(ReviewCompletionEvents.ReviewPoliceApproved,
                new[] { TaskTypeCodes.TrackPoliceApproval },
                new[] { TaskResultCodes.PoliceApproved },
                ProjectStatusCodes.Active,
                RequestWorkflowAdvance: true),

            new(ReviewCompletionEvents.ReviewPoliceCommentsReceived,
                new[] { TaskTypeCodes.TrackPoliceApproval },
                new[] { TaskResultCodes.PoliceCommentsReceived },
                ProjectStatusCodes.WaitingForClient,
                RequestWorkflowAdvance: true),

            new(ReviewCompletionEvents.ReviewPoliceCorrectionsReceived,
                new[] { TaskTypeCodes.ForwardPoliceCommentsToPlanner, TaskTypeCodes.TrackPlannerCorrections },
                new[] { TaskResultCodes.PoliceCorrectionsReceived },
                ProjectStatusCodes.Active,
                RequestWorkflowAdvance: true),

            new(ReviewCompletionEvents.ReviewProjectClosed,
                new[] { TaskTypeCodes.CloseProjectTask },
                new[] { TaskResultCodes.ReviewProjectClosed },
                ProjectStatusCodes.Closed,
                RequestWorkflowAdvance: true),

            // ───────────────────────────────────────────────────────────────
            // Generic, workflow-agnostic completion events (per WorkflowDecisions
            // §1). Each row maps only to (TaskType, TaskResult) pairs that are
            // already allowed by the corresponding ReviewTaskInteractionRegistry
            // entry — no new task types, no new result codes. Project-status
            // updates are intentionally left to the workflow transition
            // SetProjectStatus actions, so the broad-status source of truth
            // remains the seed.
            //
            // The following generic events are intentionally NOT registered yet
            // because they require an explicit business decision and adding
            // them blindly would either fail validation against the existing
            // interaction registry or require new task types / result codes:
            //   • DraftUpdated — UpdateOpinionDraft already uses
            //     OpinionDraftPrepared, which is covered by DraftPrepared.
            //     Splitting the event needs an explicit decision.
            //   • BillingMilestoneChecked — CheckBillingMilestone has no allowed
            //     results in the registry; choosing one requires a decision.
            // ───────────────────────────────────────────────────────────────

            // RequestSourceClassified — REV.Intake classification closure.
            // Approved (round: ClassifyRequestSource). Dual outcome:
            // RequestFromPlanner / RequestFromMunicipality; both are existing
            // result codes and both are listed on the dedicated
            // ClassifyRequestSource interaction. The workflow engine resolves
            // the actual REV.Intake transition from the recorded result; no
            // project-status update belongs here.
            new(ReviewCompletionEvents.RequestSourceClassified,
                new[] { TaskTypeCodes.ClassifyRequestSource, TaskTypeCodes.RequestMunicipalityInvitation },
                new[] { TaskResultCodes.RequestFromPlanner, TaskResultCodes.RequestFromMunicipality },
                NewProjectStatusCode: null,
                RequestWorkflowAdvance: true),

            // WorkOrderReceived — PlanningWorkflow PLN.WorkOrder → ExecutionMaterialCheck.
            // ClosesAssociatedTask: FollowWorkOrder EmailFiling/EmailThread work targets stay
            // Pending when cert/UI records the result without completing the link — same
            // soft-close trap as Opinion/PRP ProjectWork events.
            new(ReviewCompletionEvents.WorkOrderReceived,
                new[] { TaskTypeCodes.FollowWorkOrder },
                new[] { TaskResultCodes.WorkOrderReceived },
                NewProjectStatusCode: null,
                RequestWorkflowAdvance: true,
                ClosesAssociatedTask: true),

            // MissingMaterialUpdated — covers both the "sent" and "received"
            // sides of the missing-material loop. Applies to the same task
            // types whose interaction registry already lists these results.
            new(ReviewCompletionEvents.MissingMaterialUpdated,
                new[] { TaskTypeCodes.RequestMissingMaterial, TaskTypeCodes.TrackMissingMaterial },
                new[] { TaskResultCodes.MissingMaterialRequestSent, TaskResultCodes.MissingMaterialReceived },
                NewProjectStatusCode: null,
                RequestWorkflowAdvance: true),

            // AnalysisCompleted — Opinion AnalyzeDocuments closure. Dual outcome:
            // OpinionAnalysisCompleted → PrepareDraft; MaterialMissing →
            // RequestMissingMaterial (status handled by seed transition actions).
            // ClosesAssociatedTask: ProjectWork Related target stays Pending when the
            // cert/UI path records a result without completing the link — same soft-close
            // trap as QuoteCalculationCompleted (taskClosed=false / work-targets-pending).
            new(ReviewCompletionEvents.AnalysisCompleted,
                new[] { TaskTypeCodes.AnalyzeOpinionMaterials },
                new[] { TaskResultCodes.OpinionAnalysisCompleted, TaskResultCodes.MaterialMissing },
                NewProjectStatusCode: null,
                RequestWorkflowAdvance: true,
                ClosesAssociatedTask: true),

            // DraftPrepared — Opinion draft authoring / update closure. Both
            // PrepareOpinionDraft and UpdateOpinionDraft record the same
            // OpinionDraftPrepared result by registry contract.
            new(ReviewCompletionEvents.DraftPrepared,
                new[] { TaskTypeCodes.PrepareOpinionDraft, TaskTypeCodes.UpdateOpinionDraft },
                new[] { TaskResultCodes.OpinionDraftPrepared },
                NewProjectStatusCode: null,
                RequestWorkflowAdvance: true,
                ClosesAssociatedTask: true),

            // InternalReviewCompleted — Opinion internal review dual outcome.
            new(ReviewCompletionEvents.InternalReviewCompleted,
                new[] { TaskTypeCodes.ReviewOpinionInternal },
                new[] { TaskResultCodes.OpinionApprovedInternally, TaskResultCodes.OpinionRequiresRevision },
                NewProjectStatusCode: null,
                RequestWorkflowAdvance: true,
                ClosesAssociatedTask: true),

            // DocumentSent — Opinion SendOpinion closure. Project-status to
            // Closed is set by the seed transition action.
            new(ReviewCompletionEvents.DocumentSent,
                new[] { TaskTypeCodes.SendOpinion },
                new[] { TaskResultCodes.OpinionSent },
                NewProjectStatusCode: null,
                RequestWorkflowAdvance: true,
                ClosesAssociatedTask: true),

            // ── Proposal (PRP.*) quote task completion events ───────────────
            // Calculation → Preparation → InternalApproval → SentFollowUp →
            // Approved/Rejected. Each row maps to the exact results allowed by
            // the matching ReviewTaskInteractionRegistry entry. Project-status
            // updates are driven by the seed transition SetStatus actions, so
            // NewProjectStatusCode stays null here.
            // QuoteCalculationCompleted — operator finished PrepareQuoteCalculation in ProjectWork.
            // ClosesAssociatedTask: without it, a Pending ProjectWork WorkTarget blocks close
            // (taskClosed=False / willAutoAdvance=False / closureReason=work-targets-pending) —
            // seen in manual test 2026-07-26 task #19.
            new(ReviewCompletionEvents.QuoteCalculationCompleted,
                new[] { TaskTypeCodes.PrepareQuoteCalculation },
                new[] { TaskResultCodes.QuoteCalculationCompleted },
                NewProjectStatusCode: null,
                RequestWorkflowAdvance: true,
                ClosesAssociatedTask: true),

            new(ReviewCompletionEvents.QuoteDocumentPrepared,
                new[] { TaskTypeCodes.PrepareQuoteDocument },
                new[] { TaskResultCodes.QuotePrepared },
                NewProjectStatusCode: null,
                RequestWorkflowAdvance: true,
                ClosesAssociatedTask: true),

            new(ReviewCompletionEvents.QuoteInternallyApproved,
                new[] { TaskTypeCodes.ApproveQuoteInternal },
                new[] { TaskResultCodes.QuoteApprovedInternally, TaskResultCodes.QuoteRequiresRevision },
                NewProjectStatusCode: null,
                RequestWorkflowAdvance: true,
                ClosesAssociatedTask: true),

            new(ReviewCompletionEvents.QuoteSentToClient,
                new[] { TaskTypeCodes.SendQuoteToClient },
                new[] { TaskResultCodes.QuoteSent },
                NewProjectStatusCode: null,
                RequestWorkflowAdvance: true,
                ClosesAssociatedTask: true),

            new(ReviewCompletionEvents.QuoteApprovalTracked,
                new[] { TaskTypeCodes.FollowQuoteApproval },
                new[]
                {
                    TaskResultCodes.QuoteApprovedByClient,
                    TaskResultCodes.QuoteRejectedByClient,
                    TaskResultCodes.QuoteCancelledNoResponse,
                },
                NewProjectStatusCode: null,
                RequestWorkflowAdvance: true,
                ClosesAssociatedTask: true),

            // ProjectCloseDecided — generic CloseProject task (REV.Close /
            // PLN.Close). Distinct from CloseProjectTask/ReviewProjectClosed.
            // Approved advances to the terminal stage (whose transition records
            // ProjectClosed + runs the CloseProject action); Rejected /
            // NeedsMoreInfo self-loop so the user can retry. Project-status is
            // driven by the seed transition actions.
            new(ReviewCompletionEvents.ProjectCloseDecided,
                new[] { TaskTypeCodes.CloseProject },
                new[]
                {
                    TaskResultCodes.ProjectCloseApproved,
                    TaskResultCodes.ProjectCloseRejected,
                    TaskResultCodes.ProjectCloseNeedsMoreInfo,
                },
                NewProjectStatusCode: null,
                RequestWorkflowAdvance: true),

            // Outsourcing (OUT.*) — no TaskResult; ClosesAssociatedTask is the
            // declaration that the driving work is done. Seed transitions stay
            // AllRequiredTasksClosed + AllTasksComplete (not TaskResultEquals).
            new(ReviewCompletionEvents.OutsourceQuoteReceived,
                new[] { TaskTypeCodes.ReceiveOutsourceQuote },
                Array.Empty<string>(),
                NewProjectStatusCode: null,
                RequestWorkflowAdvance: true,
                ClosesAssociatedTask: true),

            new(ReviewCompletionEvents.OutsourceOfferApproved,
                new[] { TaskTypeCodes.ApproveOutsourceQuote },
                Array.Empty<string>(),
                NewProjectStatusCode: null,
                RequestWorkflowAdvance: true,
                ClosesAssociatedTask: true),

            new(ReviewCompletionEvents.OutsourcePaymentsCompleted,
                new[] { TaskTypeCodes.MonitorOutsourcePayments },
                Array.Empty<string>(),
                NewProjectStatusCode: null,
                RequestWorkflowAdvance: true,
                ClosesAssociatedTask: true),
        };

        var d = new Dictionary<string, ReviewCompletionBehavior>(StringComparer.Ordinal);
        foreach (var r in rows) d[r.EventCode] = r;
        return d;
    }

    /// <summary>
    /// For events where the project-status depends on the recorded task result
    /// (e.g. <see cref="ReviewCompletionEvents.ReviewMaterialCheckCompleted"/>).
    /// </summary>
    public static string? ResolveResultDependentProjectStatus(string eventCode, string? taskResultCode)
    {
        if (eventCode == ReviewCompletionEvents.ReviewMaterialCheckCompleted)
        {
            return taskResultCode switch
            {
                TaskResultCodes.MaterialComplete => ProjectStatusCodes.Active,
                TaskResultCodes.MaterialMissing  => ProjectStatusCodes.WaitingForMaterial,
                _ => null,
            };
        }
        return null;
    }
}
