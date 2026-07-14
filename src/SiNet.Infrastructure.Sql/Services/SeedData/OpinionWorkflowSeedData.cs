using SiNet.Infrastructure.Sql.Constants;

namespace SiNet.Infrastructure.Sql.Services.SeedData;

/// <summary>
/// Seed for the standalone <see cref="WorkflowCodes.Opinion"/> workflow (OPN.*).
/// <para>
/// Opinion (חוות דעת) is started email-driven via
/// <c>SuggestedActionType.CreateOpinionProject</c>, which routes through
/// <c>ActionExecutor.StartWorkflowFromActionAsync("Opinion", ...)</c>. It is
/// not mapped to any <see cref="Models.ProjectTypeWorkflowDefinition"/>.
/// </para>
/// <para>
/// Reuses the shape of <see cref="ProposalWorkflowSeedData"/> — the same DTOs
/// and <see cref="PlanningWorkflowSeedData.SeedActionType"/> are consumed by
/// <c>WorkflowSeedService</c>. Reuses existing
/// <see cref="TaskResultCodes"/> (<c>MaterialMissing</c>,
/// <c>MissingMaterialReceived</c>, plus the new <c>Opinion*</c> results).
/// No new task types, schema, or action types are introduced.
/// </para>
/// </summary>
public static class OpinionWorkflowSeedData
{
    public const string Code = WorkflowCodes.Opinion;
    public const string Name = "תהליך חוות דעת";
    public const string Description =
        "Opinion workflow (OPN.*) — independent opinion lifecycle from incoming email " +
        "(material intake → analysis → draft → internal review → send → close).";

    /// <summary>
    /// OPN.* stages in canonical SortOrder. <see cref="OpinionStageCodes.ReceiveMaterial"/>
    /// is initial; <see cref="OpinionStageCodes.Close"/> is final.
    /// </summary>
    public static readonly PlanningWorkflowSeedData.StageDefinition[] Stages = new[]
    {
        new PlanningWorkflowSeedData.StageDefinition(OpinionStageCodes.ReceiveMaterial,        "קבלת חומר לחוות דעת",      SortOrder: 10, IsInitial: true,  IsFinal: false),
        new PlanningWorkflowSeedData.StageDefinition(OpinionStageCodes.AnalyzeDocuments,       "ניתוח מסמכים",              SortOrder: 20),
        new PlanningWorkflowSeedData.StageDefinition(OpinionStageCodes.RequestMissingMaterial, "בקשת חומר חסר",             SortOrder: 30),
        new PlanningWorkflowSeedData.StageDefinition(OpinionStageCodes.PrepareDraft,           "הכנת טיוטת חוות דעת",       SortOrder: 40),
        new PlanningWorkflowSeedData.StageDefinition(OpinionStageCodes.InternalReview,         "בדיקה / אישור פנימי",       SortOrder: 50),
        new PlanningWorkflowSeedData.StageDefinition(OpinionStageCodes.UpdateOpinion,          "עדכון חוות דעת",            SortOrder: 60),
        new PlanningWorkflowSeedData.StageDefinition(OpinionStageCodes.SendOpinion,            "שליחת חוות דעת",            SortOrder: 70),
        new PlanningWorkflowSeedData.StageDefinition(OpinionStageCodes.Close,                  "סגירת התהליך",              SortOrder: 80, IsInitial: false, IsFinal: true),
    };

    /// <summary>
    /// Transitions between OPN.* stages. Reuses existing generic material
    /// results (<c>MaterialMissing</c>, <c>MissingMaterialReceived</c>) and
    /// the new Opinion-specific results.
    /// </summary>
    public static readonly PlanningWorkflowSeedData.StageTransitionDefinition[] Transitions = new[]
    {
        // Material received and ready for analysis.
        // Explicit AUTO transition (approved): when FileInitialMaterials is
        // closed via ReviewMaterialFiled (ClosesAssociatedTask=true), this
        // transition fires automatically on AllRequiredTasksClosed.
        AutoLinear(OpinionStageCodes.ReceiveMaterial, OpinionStageCodes.AnalyzeDocuments,
            actions: SetStatus(ProjectStatusCodes.LeadReceived)),

        // Missing material loop ────────────────────────────────────────────
        Conditional(OpinionStageCodes.AnalyzeDocuments, OpinionStageCodes.RequestMissingMaterial,
            taskResult: TaskResultCodes.MaterialMissing,
            actions: SetStatus(ProjectStatusCodes.WaitingForMaterial)),

        Conditional(OpinionStageCodes.RequestMissingMaterial, OpinionStageCodes.AnalyzeDocuments,
            taskResult: TaskResultCodes.MissingMaterialReceived,
            actions: SetStatus(ProjectStatusCodes.Active)),

        // Analysis complete → prepare draft.
        Conditional(OpinionStageCodes.AnalyzeDocuments, OpinionStageCodes.PrepareDraft,
            taskResult: TaskResultCodes.OpinionAnalysisCompleted),

        // Draft ready → internal review.
        Conditional(OpinionStageCodes.PrepareDraft, OpinionStageCodes.InternalReview,
            taskResult: TaskResultCodes.OpinionDraftPrepared),

        // Internal review asks for revisions → update.
        Conditional(OpinionStageCodes.InternalReview, OpinionStageCodes.UpdateOpinion,
            taskResult: TaskResultCodes.OpinionRequiresRevision),

        // Update done → back to internal review. UpdateOpinionDraft records OpinionDraftPrepared
        // (registry contract) and requests auto-advance, so this must be a result-driven Auto
        // transition (mirrors PrepareDraft → InternalReview). A plain Linear defaults to Manual eval
        // and never fires on the auto-advance path, stalling the revision loop at OPN.UpdateOpinion.
        Conditional(OpinionStageCodes.UpdateOpinion, OpinionStageCodes.InternalReview,
            taskResult: TaskResultCodes.OpinionDraftPrepared),

        // Internal review approved → send opinion.
        Conditional(OpinionStageCodes.InternalReview, OpinionStageCodes.SendOpinion,
            taskResult: TaskResultCodes.OpinionApprovedInternally),

        // Sent → close.
        Conditional(OpinionStageCodes.SendOpinion, OpinionStageCodes.Close,
            taskResult: TaskResultCodes.OpinionSent,
            actions: SetStatus(ProjectStatusCodes.Closed)),
    };

    /// <summary>
    /// Explicit <c>WorkflowStageTask</c> templates for every non-terminal OPN.* stage.
    /// Each entry guarantees the generated <c>ProjectAssignment</c> carries a real
    /// <c>TaskTypeId</c>, so <c>TaskNavigationResolver</c> opens it via
    /// <c>TaskInteractionDefinition.ComponentKey</c> (registered in
    /// <c>ReviewTaskInteractionRegistry</c>) instead of falling back to the legacy
    /// stage-code <c>OpenWorkflowTask</c> route.
    /// <para>
    /// The five Opinion-specific task types map to the existing
    /// <c>TaskComponentKeys.ProjectWork</c> component (no new UI components introduced).
    /// </para>
    /// </summary>
    public static readonly PlanningWorkflowSeedData.StageTaskDefinition[] StageTasks = new[]
    {
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: OpinionStageCodes.ReceiveMaterial,
            TaskTypeCode: TaskTypeCodes.FileInitialMaterials,
            AssignedGroupCode: UserGroupCodes.OfficeManagement),
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: OpinionStageCodes.AnalyzeDocuments,
            TaskTypeCode: TaskTypeCodes.AnalyzeOpinionMaterials,
            AssignedGroupCode: UserGroupCodes.Planners),
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: OpinionStageCodes.RequestMissingMaterial,
            TaskTypeCode: TaskTypeCodes.RequestMissingMaterial,
            AssignedGroupCode: UserGroupCodes.OfficeManagement),
        // TrackMissingMaterial closes the missing-material loop: it is the only
        // task type allowed to emit MissingMaterialReceived, which drives the
        // RequestMissingMaterial → AnalyzeDocuments auto-transition. Without it
        // the loop is a dead end.
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: OpinionStageCodes.RequestMissingMaterial,
            TaskTypeCode: TaskTypeCodes.TrackMissingMaterial,
            AssignedGroupCode: UserGroupCodes.OfficeManagement),
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: OpinionStageCodes.PrepareDraft,
            TaskTypeCode: TaskTypeCodes.PrepareOpinionDraft,
            AssignedGroupCode: UserGroupCodes.Planners),
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: OpinionStageCodes.InternalReview,
            TaskTypeCode: TaskTypeCodes.ReviewOpinionInternal,
            AssignedGroupCode: UserGroupCodes.SeniorManagement),
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: OpinionStageCodes.UpdateOpinion,
            TaskTypeCode: TaskTypeCodes.UpdateOpinionDraft,
            AssignedGroupCode: UserGroupCodes.Planners),
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: OpinionStageCodes.SendOpinion,
            TaskTypeCode: TaskTypeCodes.SendOpinion,
            AssignedGroupCode: UserGroupCodes.OfficeManagement),
    };

    /// <summary>
    /// Default assigned user group for each OPN.* non-terminal stage. Consumed by
    /// <c>WorkflowSeedService</c> to populate <c>WorkflowStageDefinition.AssignedGroupId</c>,
    /// which enables the group-based task fallback in <c>WorkflowTaskOrchestrator</c>.
    /// Terminal stage (<see cref="OpinionStageCodes.Close"/>) intentionally has no group assignment.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> StageGroupAssignments =
        new Dictionary<string, string>
        {
            [OpinionStageCodes.ReceiveMaterial]        = UserGroupCodes.OfficeManagement,
            [OpinionStageCodes.AnalyzeDocuments]       = UserGroupCodes.Planners,
            [OpinionStageCodes.RequestMissingMaterial] = UserGroupCodes.OfficeManagement,
            [OpinionStageCodes.PrepareDraft]           = UserGroupCodes.Planners,
            [OpinionStageCodes.InternalReview]         = UserGroupCodes.SeniorManagement,
            [OpinionStageCodes.UpdateOpinion]          = UserGroupCodes.Planners,
            [OpinionStageCodes.SendOpinion]            = UserGroupCodes.OfficeManagement,
        };

    // ────────────────────────────────────────────────────────────────────────
    // Helpers (mirror ProposalWorkflowSeedData helpers)
    // ────────────────────────────────────────────────────────────────────────

    private static PlanningWorkflowSeedData.StageTransitionDefinition Linear(
        string from, string to, PlanningWorkflowSeedData.StageActionDefinition[]? actions = null)
        => new(from, to, TaskResultCode: null, actions ?? Array.Empty<PlanningWorkflowSeedData.StageActionDefinition>());

    /// <summary>
    /// Auto-evaluated unconditional transition. Mirrors
    /// <c>MaterialIntakeWorkflowSeedData.AutoLinear</c>. Used when the stage's
    /// required task is closed by the coordinator without recording a task
    /// result (e.g. <c>FileInitialMaterials</c> closed by
    /// <c>ReviewMaterialFiled</c>) and the transition is business-approved
    /// to advance automatically.
    /// </summary>
    private static PlanningWorkflowSeedData.StageTransitionDefinition AutoLinear(
        string from, string to, PlanningWorkflowSeedData.StageActionDefinition[]? actions = null)
        => new(from, to, TaskResultCode: null, actions ?? Array.Empty<PlanningWorkflowSeedData.StageActionDefinition>())
        {
            TriggerType = SiNetSQL.Models.WorkflowTransitionTriggerType.AllRequiredTasksClosed,
            ConditionType = SiNetSQL.Models.WorkflowTransitionConditionType.AllTasksComplete,
            EvaluationMode = SiNetSQL.Models.WorkflowEvaluationMode.Auto,
        };

    /// <summary>
    /// Result-driven transition on the orchestrator's auto-advance path.
    /// Sets <c>TaskStatusChanged</c> + <c>Auto</c> so <c>CheckAndAutoAdvanceAsync</c>
    /// fires the matching rule as soon as the stage task records its result
    /// (parity with Review's <c>Conditional</c>). Condition defaults to
    /// <c>TaskResultEquals</c> via the seed service.
    /// </summary>
    private static PlanningWorkflowSeedData.StageTransitionDefinition Conditional(
        string from, string to, string taskResult, PlanningWorkflowSeedData.StageActionDefinition[]? actions = null)
        => new(from, to, taskResult, actions ?? Array.Empty<PlanningWorkflowSeedData.StageActionDefinition>())
        {
            TriggerType = SiNetSQL.Models.WorkflowTransitionTriggerType.TaskStatusChanged,
            EvaluationMode = SiNetSQL.Models.WorkflowEvaluationMode.Auto,
        };

    private static PlanningWorkflowSeedData.StageActionDefinition[] SetStatus(string projectStatusCode)
        => new[]
        {
            new PlanningWorkflowSeedData.StageActionDefinition(
                PlanningWorkflowSeedData.SeedActionType.SetProjectStatus, projectStatusCode),
        };
}
