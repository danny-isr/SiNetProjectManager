namespace SiNet.Infrastructure.Sql.Constants;

/// <summary>Stable codes for <see cref="Models.TaskType.Code"/>.</summary>
public static class TaskTypeCodes
{
    public const string IdentifyQuoteRequest = "IdentifyQuoteRequest";
    public const string OpenQuoteProject = "OpenQuoteProject";
    public const string FileInitialInquiry = "FileInitialInquiry";
    public const string FileQuoteMaterial = "FileQuoteMaterial";
    public const string CheckQuoteMaterialCompleteness = "CheckQuoteMaterialCompleteness";
    public const string PrepareMissingMaterialList = "PrepareMissingMaterialList";
    public const string SendMissingMaterialRequest = "SendMissingMaterialRequest";
    public const string FollowMissingMaterial = "FollowMissingMaterial";
    public const string PrepareQuoteCalculation = "PrepareQuoteCalculation";
    public const string PrepareQuoteDocument = "PrepareQuoteDocument";
    public const string ApproveQuoteInternal = "ApproveQuoteInternal";
    public const string ReviseQuote = "ReviseQuote";
    public const string SendQuoteToClient = "SendQuoteToClient";
    public const string FollowQuoteApproval = "FollowQuoteApproval";
    public const string FollowWorkOrder = "FollowWorkOrder";
    public const string FileWorkOrder = "FileWorkOrder";
    public const string ActivateProject = "ActivateProject";
    public const string CheckExecutionMaterialCompleteness = "CheckExecutionMaterialCompleteness";
    public const string FileExecutionMaterial = "FileExecutionMaterial";
    public const string OpenPlanningWorkPackage = "OpenPlanningWorkPackage";
    public const string AssignPlanningTasks = "AssignPlanningTasks";
    public const string GeneralPlanning = "GeneralPlanning";
    public const string TrafficPlanning = "TrafficPlanning";
    public const string DrainagePlanning = "DrainagePlanning";
    public const string PhysicalPlanning = "PhysicalPlanning";
    public const string ExternalPlannerCoordination = "ExternalPlannerCoordination";
    public const string PrepareDraftPlans = "PrepareDraftPlans";
    public const string PreparePreliminaryDesign = "PreparePreliminaryDesign";
    public const string PrepareDetailedDesign = "PrepareDetailedDesign";
    public const string InternalPlanReview = "InternalPlanReview";
    public const string HandleReviewComments = "HandleReviewComments";
    public const string PrepareSubmissionSet = "PrepareSubmissionSet";
    public const string SubmitForApproval = "SubmitForApproval";
    public const string FollowAuthorityApproval = "FollowAuthorityApproval";
    public const string HandleAuthorityComments = "HandleAuthorityComments";
    public const string PrepareWorkPlans = "PrepareWorkPlans";
    public const string FinalPlanReview = "FinalPlanReview";
    public const string DeliverWorkPlans = "DeliverWorkPlans";
    public const string CheckBillingMilestone = "CheckBillingMilestone";
    public const string PrepareBill = "PrepareBill";
    public const string SubmitBill = "SubmitBill";
    public const string FollowBillApproval = "FollowBillApproval";
    public const string CloseBillingBalance = "CloseBillingBalance";
    public const string CloseProject = "CloseProject";

    // ─── Review workflow task types ────────────────────────────────────────
    public const string RequestMunicipalityInvitation = "RequestMunicipalityInvitation";
    public const string TrackMunicipalityInvitation   = "TrackMunicipalityInvitation";
    public const string OpenReviewProject             = "OpenReviewProject";
    public const string OpenProject                   = "OpenProject";
    // Classification-only intake task for REV.Intake: the operator records
    // whether the request originated from a planner or from the municipality.
    // Modeled after IdentifyQuoteRequest (ProjectWork host, WorkflowResultRecorded
    // policy, dual TaskResults). No new TaskResults are introduced — reuses
    // RequestFromPlanner / RequestFromMunicipality.
    public const string ClassifyRequestSource         = "ClassifyRequestSource";
    public const string FileInitialMaterials          = "FileInitialMaterials";
    // Note: "בדיקת שלמות חומר" is a shared general task type used by both
    // Quote and Review workflows. The single canonical code is
    // CheckQuoteMaterialCompleteness (defined above). Do not introduce a
    // separate Review-only code/seed for the same business concept.
    public const string RequestMissingMaterial        = "RequestMissingMaterial";
    public const string TrackMissingMaterial          = "TrackMissingMaterial";
    public const string FileCorrectedMaterials        = "FileCorrectedMaterials";
    public const string PerformProfessionalReview     = "PerformProfessionalReview";
    public const string FixReportPerManager           = "FixReportPerManager";
    public const string ApproveReviewReport           = "ApproveReviewReport";
    public const string ResubmitToManager             = "ResubmitToManager";
    public const string SendInternalApproval          = "SendInternalApproval";
    public const string SendReportToPlanner           = "SendReportToPlanner";
    public const string TrackPlannerCorrections       = "TrackPlannerCorrections";
    public const string RecheckPlan                   = "RecheckPlan";
    // Post-recheck police-requirement decision (REV.PoliceApprovalDecision).
    // Emits PoliceApprovalRequired / PoliceApprovalNotRequired.
    public const string DeterminePoliceApprovalRequirement = "DeterminePoliceApprovalRequirement";
    public const string IssueApproval                 = "IssueApproval";
    public const string PreparePoliceSubmission       = "PreparePoliceSubmission";
    public const string SubmitToPolice                = "SubmitToPolice";
    public const string TrackPoliceApproval           = "TrackPoliceApproval";
    public const string ForwardPoliceCommentsToPlanner = "ForwardPoliceCommentsToPlanner";
    public const string FileFinalApprovals            = "FileFinalApprovals";
    public const string CloseProjectTask              = "CloseProjectTask";

    // ─── Opinion workflow task types (OPN.*) ───────────────────────────────
    // Internal opinion-lifecycle tasks. ReceiveMaterial / RequestMissingMaterial
    // reuse existing FileInitialMaterials / RequestMissingMaterial codes —
    // do not duplicate.
    public const string AnalyzeOpinionMaterials       = "AnalyzeOpinionMaterials";
    public const string PrepareOpinionDraft           = "PrepareOpinionDraft";
    public const string ReviewOpinionInternal         = "ReviewOpinionInternal";
    public const string UpdateOpinionDraft            = "UpdateOpinionDraft";
    public const string SendOpinion                   = "SendOpinion";

    // ─── Outsourcing (OUT.*) — manual quote / approve / payment monitor ───
    public const string ReceiveOutsourceQuote = "ReceiveOutsourceQuote";
    public const string ApproveOutsourceQuote = "ApproveOutsourceQuote";
    public const string MonitorOutsourcePayments = "MonitorOutsourcePayments";
}
