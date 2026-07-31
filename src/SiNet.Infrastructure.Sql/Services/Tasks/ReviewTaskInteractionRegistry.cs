using SiNet.Infrastructure.Sql.Constants;
using SiNetSQL.Models;

namespace SiNet.Infrastructure.Sql.Services.Tasks;

/// <summary>
/// Canonical task-type → interaction mapping for the New System.
/// <para>
/// SOURCE OF TRUTH for native surfaces. The SiNetSQL copy
/// (<c>SiNetSQL.Services.Tasks.ReviewTaskInteractionRegistry</c>) is a TEMPORARY duplicate for
/// legacy UI; keep keys/codes aligned. REMOVAL WHEN: legacy UI consumes this registry (or a shared
/// Application catalog) and the SiNetSQL copy is deleted.
/// </para>
/// </summary>
public static class ReviewTaskInteractionRegistry
{
    private static readonly IReadOnlyDictionary<string, TaskInteractionDefinition> _byCode = Build();

    /// <summary>All Review interaction definitions.</summary>
    public static IReadOnlyCollection<TaskInteractionDefinition> All => (IReadOnlyCollection<TaskInteractionDefinition>)_byCode.Values;

    /// <summary>Returns the interaction definition for <paramref name="taskTypeCode"/>, or <c>null</c> when not Review-mapped.</summary>
    public static TaskInteractionDefinition? TryGet(string taskTypeCode) =>
        _byCode.TryGetValue(taskTypeCode, out var def) ? def : null;

    private static IReadOnlyDictionary<string, TaskInteractionDefinition> Build()
    {
        var defs = new TaskInteractionDefinition[]
        {
            // OpenReviewProject — parent intake task on the parent project from email.
            // This is Review-specific and must NOT be confused with generic project opening.
            new(
                TaskTypeCodes.OpenReviewProject,
                TaskOpenMode.ProjectCreationFromEmail,
                TaskComponentKeys.ReviewProjectSetupFromEmail,
                TaskWorkTargetEntityType.EmailInboxMessage,
                TaskLinkRole.Trigger,
                TaskCompletionPolicy.ProjectCreated,
                new[] { TaskResultCodes.ProjectOpened },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            // OpenProject — open project from inbox email (generic, kept for backward compatibility).
            new(
                TaskTypeCodes.OpenProject,
                TaskOpenMode.ProjectCreationFromEmail,
                TaskComponentKeys.ProjectCreationFromEmail,
                TaskWorkTargetEntityType.EmailInboxMessage,
                TaskLinkRole.Trigger,
                TaskCompletionPolicy.ProjectCreated,
                new[] { TaskResultCodes.ProjectOpened },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            new(
                TaskTypeCodes.FileInitialMaterials,
                TaskOpenMode.EmailFiling,
                TaskComponentKeys.EmailFiling,
                TaskWorkTargetEntityType.EmailInboxMessage,
                TaskLinkRole.Related,
                TaskCompletionPolicy.WorkTargetReceived,
                new[] { TaskResultCodes.MaterialComplete, TaskResultCodes.MaterialMissing },
                AutoCloseOnCompletion: false,
                RequiresUserConfirmation: false),

            // ClassifyRequestSource — REV.Intake classification-only task.
            // Mirrors IdentifyQuoteRequest: ProjectWork host (reuses the existing
            // result-picker UI), WorkflowResultRecorded policy, dual allowed
            // results. No new picker window is introduced.
            new(
                TaskTypeCodes.ClassifyRequestSource,
                TaskOpenMode.ProjectWork,
                TaskComponentKeys.ProjectWork,
                TaskWorkTargetEntityType.EmailInboxMessage,
                TaskLinkRole.Source,
                TaskCompletionPolicy.WorkflowResultRecorded,
                new[] { TaskResultCodes.RequestFromPlanner, TaskResultCodes.RequestFromMunicipality },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: true),

            new(
                TaskTypeCodes.CheckQuoteMaterialCompleteness,
                TaskOpenMode.MaterialCompletenessCheck,
                TaskComponentKeys.MaterialChecklist,
                TaskWorkTargetEntityType.MaterialChecklist,
                TaskLinkRole.Related,
                TaskCompletionPolicy.WorkflowResultRecorded,
                new[] { TaskResultCodes.MaterialComplete, TaskResultCodes.MaterialMissing },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            new(
                TaskTypeCodes.RequestMissingMaterial,
                TaskOpenMode.EmailSendToPlanner,
                TaskComponentKeys.EmailComposeToPlanner,
                TaskWorkTargetEntityType.EmailThread,
                TaskLinkRole.FollowUp,
                TaskCompletionPolicy.EmailSent,
                new[] { TaskResultCodes.MissingMaterialRequestSent },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            new(
                TaskTypeCodes.TrackMissingMaterial,
                TaskOpenMode.EmailFiling,
                TaskComponentKeys.EmailFiling,
                TaskWorkTargetEntityType.EmailThread,
                TaskLinkRole.FollowUp,
                TaskCompletionPolicy.WorkTargetReceived,
                new[] { TaskResultCodes.MissingMaterialReceived },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            new(
                TaskTypeCodes.FileCorrectedMaterials,
                TaskOpenMode.EmailFiling,
                TaskComponentKeys.EmailFiling,
                TaskWorkTargetEntityType.EmailInboxAttachment,
                TaskLinkRole.Related,
                TaskCompletionPolicy.WorkTargetReceived,
                new[] { TaskResultCodes.PlannerCorrectionsReceived },
                AutoCloseOnCompletion: false,
                RequiresUserConfirmation: false),

            // PerformProfessionalReview — opens InspectionReport.
            new(
                TaskTypeCodes.PerformProfessionalReview,
                TaskOpenMode.InspectionReport,
                TaskComponentKeys.InspectionReport,
                TaskWorkTargetEntityType.InspectionReport,
                TaskLinkRole.Related,
                TaskCompletionPolicy.InspectionReportCompleted,
                new[] { TaskResultCodes.ProfessionalReviewCompleted },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            new(
                TaskTypeCodes.FixReportPerManager,
                TaskOpenMode.InspectionReport,
                TaskComponentKeys.InspectionReport,
                TaskWorkTargetEntityType.InspectionReport,
                TaskLinkRole.Related,
                TaskCompletionPolicy.InspectionReportCompleted,
                new[] { TaskResultCodes.ProfessionalReviewCompleted },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            // ApproveReviewReport — manager approval view.
            new(
                TaskTypeCodes.ApproveReviewReport,
                TaskOpenMode.ManagerReviewApproval,
                TaskComponentKeys.ManagerReviewApproval,
                TaskWorkTargetEntityType.InspectionReport,
                TaskLinkRole.Related,
                TaskCompletionPolicy.WorkflowResultRecorded,
                new[] { TaskResultCodes.ManagerApproved, TaskResultCodes.ManagerRequestedChanges },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: true),

            new(
                TaskTypeCodes.ResubmitToManager,
                TaskOpenMode.ManagerReviewApproval,
                TaskComponentKeys.ManagerReviewApproval,
                TaskWorkTargetEntityType.InspectionReport,
                TaskLinkRole.FollowUp,
                TaskCompletionPolicy.WorkflowResultRecorded,
                new[] { TaskResultCodes.ManagerApproved, TaskResultCodes.ManagerRequestedChanges },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: true),

            new(
                TaskTypeCodes.SendInternalApproval,
                TaskOpenMode.EmailSendToPlanner,
                TaskComponentKeys.EmailComposeToPlanner,
                TaskWorkTargetEntityType.InspectionReport,
                TaskLinkRole.Related,
                TaskCompletionPolicy.EmailSent,
                Array.Empty<string>(),
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            // SendReportToPlanner — email compose using exported report.
            new(
                TaskTypeCodes.SendReportToPlanner,
                TaskOpenMode.EmailSendToPlanner,
                TaskComponentKeys.EmailComposeToPlanner,
                TaskWorkTargetEntityType.InspectionReport,
                TaskLinkRole.Related,
                TaskCompletionPolicy.EmailSent,
                new[] { TaskResultCodes.CommentsSentToPlanner },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            // TrackPlannerCorrections — email filing of incoming corrections.
            new(
                TaskTypeCodes.TrackPlannerCorrections,
                TaskOpenMode.EmailFiling,
                TaskComponentKeys.EmailFiling,
                TaskWorkTargetEntityType.EmailThread,
                TaskLinkRole.FollowUp,
                TaskCompletionPolicy.WorkTargetReceived,
                new[] { TaskResultCodes.PlannerCorrectionsReceived },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            // RecheckPlan — re-opens InspectionReport for the recheck round.
            new(
                TaskTypeCodes.RecheckPlan,
                TaskOpenMode.InspectionReport,
                TaskComponentKeys.InspectionReport,
                TaskWorkTargetEntityType.InspectionReport,
                TaskLinkRole.Related,
                TaskCompletionPolicy.InspectionReportCompleted,
                new[] { TaskResultCodes.RecheckPassed, TaskResultCodes.RecheckRequiresMoreCorrections },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            // DeterminePoliceApprovalRequirement — post-recheck decision on
            // whether the plan needs police/authority approval. Internal
            // project-work decision (no dedicated UI yet), reuses the ProjectWork
            // component like the other decision-only tasks.
            new(
                TaskTypeCodes.DeterminePoliceApprovalRequirement,
                TaskOpenMode.ProjectWork,
                TaskComponentKeys.ProjectWork,
                TaskWorkTargetEntityType.Project,
                TaskLinkRole.Related,
                TaskCompletionPolicy.WorkflowResultRecorded,
                new[] { TaskResultCodes.PoliceApprovalRequired, TaskResultCodes.PoliceApprovalNotRequired },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: true),

            new(
                TaskTypeCodes.IssueApproval,
                TaskOpenMode.ProjectWork,
                TaskComponentKeys.ProjectWork,
                TaskWorkTargetEntityType.ApprovalPackage,
                TaskLinkRole.Related,
                TaskCompletionPolicy.OutputFileCreated,
                new[] { TaskResultCodes.PrincipallyApproved },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            // PreparePoliceSubmission — prep package; optional pre-step to SubmitToPolice.
            new(
                TaskTypeCodes.PreparePoliceSubmission,
                TaskOpenMode.PoliceSubmission,
                TaskComponentKeys.PoliceSubmission,
                TaskWorkTargetEntityType.ApprovalPackage,
                TaskLinkRole.Related,
                TaskCompletionPolicy.OutputFileCreated,
                Array.Empty<string>(),
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            // SubmitToPolice — actually sends; closes on EmailSent / OutputSubmitted.
            new(
                TaskTypeCodes.SubmitToPolice,
                TaskOpenMode.PoliceSubmission,
                TaskComponentKeys.PoliceSubmission,
                TaskWorkTargetEntityType.ApprovalPackage,
                TaskLinkRole.Related,
                TaskCompletionPolicy.OutputSubmitted,
                new[] { TaskResultCodes.SubmittedToPolice },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: true),

            // TrackPoliceApproval — wait for approval/comments.
            new(
                TaskTypeCodes.TrackPoliceApproval,
                TaskOpenMode.EmailFiling,
                TaskComponentKeys.EmailFiling,
                TaskWorkTargetEntityType.EmailThread,
                TaskLinkRole.FollowUp,
                TaskCompletionPolicy.WorkflowResultRecorded,
                new[] { TaskResultCodes.PoliceApproved, TaskResultCodes.PoliceCommentsReceived },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            new(
                TaskTypeCodes.ForwardPoliceCommentsToPlanner,
                TaskOpenMode.EmailSendToPlanner,
                TaskComponentKeys.EmailComposeToPlanner,
                TaskWorkTargetEntityType.EmailThread,
                TaskLinkRole.FollowUp,
                TaskCompletionPolicy.EmailSent,
                new[] { TaskResultCodes.CommentsSentToPlanner, TaskResultCodes.PoliceCorrectionsReceived },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            new(
                TaskTypeCodes.FileFinalApprovals,
                TaskOpenMode.EmailFiling,
                TaskComponentKeys.EmailFiling,
                TaskWorkTargetEntityType.ProjectFile,
                TaskLinkRole.Related,
                TaskCompletionPolicy.OutputFileCreated,
                Array.Empty<string>(),
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            new(
                TaskTypeCodes.RequestMunicipalityInvitation,
                TaskOpenMode.EmailSendToPlanner,
                TaskComponentKeys.EmailComposeToPlanner,
                TaskWorkTargetEntityType.EmailThread,
                TaskLinkRole.Related,
                TaskCompletionPolicy.EmailSent,
                new[] { TaskResultCodes.RequestFromMunicipality, TaskResultCodes.RequestFromPlanner },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            new(
                TaskTypeCodes.TrackMunicipalityInvitation,
                TaskOpenMode.EmailFiling,
                TaskComponentKeys.EmailFiling,
                TaskWorkTargetEntityType.EmailThread,
                TaskLinkRole.FollowUp,
                TaskCompletionPolicy.WorkflowResultRecorded,
                new[] { TaskResultCodes.MunicipalityRequestReceived },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            // CloseProjectTask — terminal task.
            new(
                TaskTypeCodes.CloseProjectTask,
                TaskOpenMode.ProjectWork,
                TaskComponentKeys.ProjectWork,
                TaskWorkTargetEntityType.Project,
                TaskLinkRole.Related,
                TaskCompletionPolicy.CloseProject,
                new[] { TaskResultCodes.ReviewProjectClosed },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: true),

            // ─────────────────────────────────────────────────────────────
            // Proposal (PRP.*) interactions.
            // Added so Proposal stage tasks (seeded via ProposalWorkflowSeedData.StageTasks)
            // resolve through TaskNavigationResolver to a concrete ComponentKey
            // instead of falling back to the legacy stage-code OpenWorkflowTask route.
            // Reuses existing TaskComponentKeys — no new components introduced.
            // ─────────────────────────────────────────────────────────────

            // IdentifyQuoteRequest — classification-only Intake task. Opens the
            // email preview/work area so the operator can decide whether the
            // message is a quote request. Closure is by recorded task result
            // (QuoteRequestDetected advances; NotQuoteRequest ends the proposal
            // lifecycle outside this workflow). No project is created here and
            // no filing happens here — those belong to OpenQuoteProject /
            // FileQuoteMaterial in later PRP.* stages.
            // UI: FloatingProjectTasksView intercepts ComponentKey=ProjectWork for
            // this task type and opens QuoteClassificationDialog (not ProjectWork).
            new(
                TaskTypeCodes.IdentifyQuoteRequest,
                TaskOpenMode.ProjectWork,
                TaskComponentKeys.ProjectWork,
                TaskWorkTargetEntityType.EmailInboxMessage,
                TaskLinkRole.Source,
                TaskCompletionPolicy.WorkflowResultRecorded,
                new[] { TaskResultCodes.QuoteRequestDetected, TaskResultCodes.NotQuoteRequest },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: true),

            // OpenQuoteProject — native decision host (email + open project OR
            // not-a-quote). Project creation closes via ProjectOpened; decline
            // closes via NotQuoteRequest → PRP.Rejected. Filing of attachments
            // runs later as FileQuoteMaterial. Uses TaskLinkRole.Source because
            // the orchestrator propagates the originating email as a Source link.
            new(
                TaskTypeCodes.OpenQuoteProject,
                TaskOpenMode.ProjectCreationFromEmail,
                TaskComponentKeys.ProjectCreationFromEmail,
                TaskWorkTargetEntityType.EmailInboxMessage,
                TaskLinkRole.Source,
                TaskCompletionPolicy.ProjectCreated,
                new[] { TaskResultCodes.ProjectOpened, TaskResultCodes.NotQuoteRequest },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: true),

            // FileQuoteMaterial — dedicated PRP.FileMaterial task. Reuses the
            // existing email-filing host and the MoveToProject pipeline; the
            // task closes via the canonical ReviewMaterialFiled event raised
            // by MoveToProjectProcessActionHandler after a successful filing.
            // Uses TaskLinkRole.Source for the same reason as OpenQuoteProject —
            // the workflow-originating email is propagated as a Source link.
            new(
                TaskTypeCodes.FileQuoteMaterial,
                TaskOpenMode.EmailFiling,
                TaskComponentKeys.EmailFiling,
                TaskWorkTargetEntityType.EmailInboxMessage,
                TaskLinkRole.Source,
                TaskCompletionPolicy.WorkTargetReceived,
                Array.Empty<string>(),
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            // PrepareQuoteCalculation — internal project work (no dedicated UI yet).
            new(
                TaskTypeCodes.PrepareQuoteCalculation,
                TaskOpenMode.ProjectWork,
                TaskComponentKeys.ProjectWork,
                TaskWorkTargetEntityType.Project,
                TaskLinkRole.Related,
                TaskCompletionPolicy.WorkflowResultRecorded,
                new[] { TaskResultCodes.QuoteCalculationCompleted },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            // PrepareQuoteDocument — internal project work.
            new(
                TaskTypeCodes.PrepareQuoteDocument,
                TaskOpenMode.ProjectWork,
                TaskComponentKeys.ProjectWork,
                TaskWorkTargetEntityType.Project,
                TaskLinkRole.Related,
                TaskCompletionPolicy.WorkflowResultRecorded,
                new[] { TaskResultCodes.QuotePrepared },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            // ApproveQuoteInternal — manager approval before sending.
            new(
                TaskTypeCodes.ApproveQuoteInternal,
                TaskOpenMode.ProjectWork,
                TaskComponentKeys.ProjectWork,
                TaskWorkTargetEntityType.Project,
                TaskLinkRole.Related,
                TaskCompletionPolicy.WorkflowResultRecorded,
                new[] { TaskResultCodes.QuoteApprovedInternally, TaskResultCodes.QuoteRequiresRevision },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: true),

            // SendQuoteToClient — internal compose + IEmailSender MessageId proof (or admin override).
            new(
                TaskTypeCodes.SendQuoteToClient,
                TaskOpenMode.ProjectWork,
                TaskComponentKeys.ProjectWork,
                TaskWorkTargetEntityType.Project,
                TaskLinkRole.Related,
                TaskCompletionPolicy.WorkflowResultRecorded,
                new[] { TaskResultCodes.QuoteSent },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            // FollowQuoteApproval — client decision on ProjectWork (PDF approve / reject / cancel).
            new(
                TaskTypeCodes.FollowQuoteApproval,
                TaskOpenMode.ProjectWork,
                TaskComponentKeys.ProjectWork,
                TaskWorkTargetEntityType.Project,
                TaskLinkRole.Related,
                TaskCompletionPolicy.WorkflowResultRecorded,
                new[]
                {
                    TaskResultCodes.QuoteApprovedByClient,
                    TaskResultCodes.QuoteRejectedByClient,
                    TaskResultCodes.QuoteCancelledNoResponse,
                },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            // ─────────────────────────────────────────────────────────────
            // PlanningWorkflow (PLN.*) interactions.
            // Mappings so every non-terminal Planning stage task resolves via
            // TaskNavigationResolver to a concrete ComponentKey instead of
            // failing the registry check. Reuses existing components — no new
            // UI components or task types introduced.
            // ─────────────────────────────────────────────────────────────

            // FollowWorkOrder — office-management waits for the signed work order email.
            new(
                TaskTypeCodes.FollowWorkOrder,
                TaskOpenMode.EmailFiling,
                TaskComponentKeys.EmailFiling,
                TaskWorkTargetEntityType.EmailThread,
                TaskLinkRole.FollowUp,
                TaskCompletionPolicy.WorkflowResultRecorded,
                new[] { TaskResultCodes.WorkOrderReceived },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            // CheckExecutionMaterialCompleteness — verify execution material is complete.
            new(
                TaskTypeCodes.CheckExecutionMaterialCompleteness,
                TaskOpenMode.ProjectWork,
                TaskComponentKeys.ProjectWork,
                TaskWorkTargetEntityType.Project,
                TaskLinkRole.Related,
                TaskCompletionPolicy.WorkflowResultRecorded,
                new[] { TaskResultCodes.MaterialComplete },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            // OpenPlanningWorkPackage — open the planning work package for the project.
            new(
                TaskTypeCodes.OpenPlanningWorkPackage,
                TaskOpenMode.ProjectWork,
                TaskComponentKeys.ProjectWork,
                TaskWorkTargetEntityType.Project,
                TaskLinkRole.Related,
                TaskCompletionPolicy.WorkflowResultRecorded,
                Array.Empty<string>(),
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            // SubmitForApproval — submit planning package to authority.
            new(
                TaskTypeCodes.SubmitForApproval,
                TaskOpenMode.ProjectWork,
                TaskComponentKeys.ProjectWork,
                TaskWorkTargetEntityType.Project,
                TaskLinkRole.Related,
                TaskCompletionPolicy.WorkflowResultRecorded,
                Array.Empty<string>(),
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            // HandleAuthorityComments — incorporate authority comments back into design.
            new(
                TaskTypeCodes.HandleAuthorityComments,
                TaskOpenMode.ProjectWork,
                TaskComponentKeys.ProjectWork,
                TaskWorkTargetEntityType.Project,
                TaskLinkRole.Related,
                TaskCompletionPolicy.WorkflowResultRecorded,
                new[] { TaskResultCodes.AuthorityCommentsReceived },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            // CheckBillingMilestone — administrative billing-milestone check.
            new(
                TaskTypeCodes.CheckBillingMilestone,
                TaskOpenMode.ProjectWork,
                TaskComponentKeys.ProjectWork,
                TaskWorkTargetEntityType.Project,
                TaskLinkRole.Related,
                TaskCompletionPolicy.WorkflowResultRecorded,
                Array.Empty<string>(),
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            // ─────────────────────────────────────────────────────────────
            // Opinion (OPN.*) interactions.
            // Added so every non-terminal Opinion stage task resolves via
            // TaskNavigationResolver to a concrete ComponentKey instead of
            // falling back to the legacy stage-code OpenWorkflowTask route.
            // All five Opinion-specific task types reuse the existing
            // ProjectWork component — no new UI components introduced.
            // OPN.ReceiveMaterial and OPN.RequestMissingMaterial already
            // resolve via FileInitialMaterials / RequestMissingMaterial
            // definitions above and are not duplicated here.
            // ─────────────────────────────────────────────────────────────

            // AnalyzeOpinionMaterials — internal analysis of received materials.
            new(
                TaskTypeCodes.AnalyzeOpinionMaterials,
                TaskOpenMode.ProjectWork,
                TaskComponentKeys.ProjectWork,
                TaskWorkTargetEntityType.Project,
                TaskLinkRole.Related,
                TaskCompletionPolicy.WorkflowResultRecorded,
                new[] { TaskResultCodes.OpinionAnalysisCompleted, TaskResultCodes.MaterialMissing },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            // PrepareOpinionDraft — draft authoring of the opinion document.
            new(
                TaskTypeCodes.PrepareOpinionDraft,
                TaskOpenMode.ProjectWork,
                TaskComponentKeys.ProjectWork,
                TaskWorkTargetEntityType.Project,
                TaskLinkRole.Related,
                TaskCompletionPolicy.WorkflowResultRecorded,
                new[] { TaskResultCodes.OpinionDraftPrepared },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            // ReviewOpinionInternal — internal manager review of the draft.
            new(
                TaskTypeCodes.ReviewOpinionInternal,
                TaskOpenMode.ProjectWork,
                TaskComponentKeys.ProjectWork,
                TaskWorkTargetEntityType.Project,
                TaskLinkRole.Related,
                TaskCompletionPolicy.WorkflowResultRecorded,
                new[] { TaskResultCodes.OpinionApprovedInternally, TaskResultCodes.OpinionRequiresRevision },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: true),

            // UpdateOpinionDraft — revise the draft after internal review.
            new(
                TaskTypeCodes.UpdateOpinionDraft,
                TaskOpenMode.ProjectWork,
                TaskComponentKeys.ProjectWork,
                TaskWorkTargetEntityType.Project,
                TaskLinkRole.Related,
                TaskCompletionPolicy.WorkflowResultRecorded,
                new[] { TaskResultCodes.OpinionDraftPrepared },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            // SendOpinion — dispatch the approved opinion to the client.
            new(
                TaskTypeCodes.SendOpinion,
                TaskOpenMode.ProjectWork,
                TaskComponentKeys.ProjectWork,
                TaskWorkTargetEntityType.Project,
                TaskLinkRole.Related,
                TaskCompletionPolicy.WorkflowResultRecorded,
                new[] { TaskResultCodes.OpinionSent },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: true),

            // ─────────────────────────────────────────────────────────────
            // Planning (PLN.*) design / approval / close interactions.
            // Added so every PLN stage template seeded via
            // PlanningWorkflowSeedData.StageTasks resolves through
            // TaskNavigationResolver to a concrete ComponentKey instead of
            // failing the registry health test. All reuse the existing
            // ProjectWork component — no new UI components introduced.
            // ─────────────────────────────────────────────────────────────

            new(
                TaskTypeCodes.PrepareDraftPlans,
                TaskOpenMode.ProjectWork,
                TaskComponentKeys.ProjectWork,
                TaskWorkTargetEntityType.Project,
                TaskLinkRole.Related,
                TaskCompletionPolicy.WorkflowResultRecorded,
                Array.Empty<string>(),
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            new(
                TaskTypeCodes.PreparePreliminaryDesign,
                TaskOpenMode.ProjectWork,
                TaskComponentKeys.ProjectWork,
                TaskWorkTargetEntityType.Project,
                TaskLinkRole.Related,
                TaskCompletionPolicy.WorkflowResultRecorded,
                Array.Empty<string>(),
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            new(
                TaskTypeCodes.PrepareDetailedDesign,
                TaskOpenMode.ProjectWork,
                TaskComponentKeys.ProjectWork,
                TaskWorkTargetEntityType.Project,
                TaskLinkRole.Related,
                TaskCompletionPolicy.WorkflowResultRecorded,
                Array.Empty<string>(),
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            new(
                TaskTypeCodes.FollowAuthorityApproval,
                TaskOpenMode.ProjectWork,
                TaskComponentKeys.ProjectWork,
                TaskWorkTargetEntityType.Project,
                TaskLinkRole.Related,
                TaskCompletionPolicy.WorkflowResultRecorded,
                new[] { TaskResultCodes.AuthorityApproved, TaskResultCodes.AuthorityCommentsReceived },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            new(
                TaskTypeCodes.PrepareWorkPlans,
                TaskOpenMode.ProjectWork,
                TaskComponentKeys.ProjectWork,
                TaskWorkTargetEntityType.Project,
                TaskLinkRole.Related,
                TaskCompletionPolicy.WorkflowResultRecorded,
                Array.Empty<string>(),
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: false),

            // CloseProject — generic project-closure task used by both
            // PlanningWorkflow (PLN.Close) and Review (REV.Close). Distinct
            // from CloseProjectTask: this is the Review/Planning seed-driven
            // close task, decided by the OfficeManagement employee.
            new(
                TaskTypeCodes.CloseProject,
                TaskOpenMode.ProjectWork,
                TaskComponentKeys.ProjectWork,
                TaskWorkTargetEntityType.Project,
                TaskLinkRole.Related,
                TaskCompletionPolicy.CloseProject,
                new[]
                {
                    TaskResultCodes.ProjectCloseApproved,
                    TaskResultCodes.ProjectCloseRejected,
                    TaskResultCodes.ProjectCloseNeedsMoreInfo,
                },
                AutoCloseOnCompletion: true,
                RequiresUserConfirmation: true),
        };

        var byCode = new Dictionary<string, TaskInteractionDefinition>(StringComparer.Ordinal);
        foreach (var d in defs)
            byCode[d.TaskTypeCode] = d;
        return byCode;
    }
}
