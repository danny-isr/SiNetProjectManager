using SiNet.Infrastructure.Sql.Constants;

namespace SiNet.Infrastructure.Sql.Services.SeedData;

/// <summary>
/// Seed for the standalone <see cref="WorkflowCodes.Proposal"/> workflow (PRP.*).
/// <para>
/// Proposal is project-independent: it is started from an incoming email
/// (<c>SuggestedActionType.CreatePriceQuote</c>) before any real project exists.
/// During the workflow the user selects the future project type; on client
/// approval a project is created and a continuation workflow is resolved
/// through <c>ProjectTypeWorkflowDefinition</c> /
/// <c>ProjectWorkflowPolicyService</c>.
/// </para>
/// <para>
/// Stages mirror the first seven quote stages of PlanningWorkflow (PLN.Intake +
/// PLN.Quote.*) with a fresh <c>PRP.*</c> code prefix to avoid clashing with the
/// legacy stages still living in PlanningWorkflow. The legacy PLN.* quote
/// stages are intentionally left in place for backwards compatibility and will
/// be removed only after Proposal has been validated in production.
/// </para>
/// <para>
/// Reuses existing <see cref="TaskResultCodes"/> (Quote*) and the existing
/// <see cref="PlanningWorkflowSeedData.SeedActionType"/> action types — no new
/// task results or action types are introduced.
/// </para>
/// </summary>
public static class ProposalWorkflowSeedData
{
    public const string Code = WorkflowCodes.Proposal;
    public const string Name = "תהליך הצעת מחיר";
    public const string Description =
        "Proposal workflow (PRP.*) — independent price-quote lifecycle from incoming email " +
        "to client approval/rejection. On approval a project is created and a continuation " +
        "workflow is resolved by ProjectType.";

    /// <summary>
    /// PRP.* stages in canonical SortOrder. <see cref="ProposalStageCodes.Intake"/>
    /// is initial; <see cref="ProposalStageCodes.Approved"/> and
    /// <see cref="ProposalStageCodes.Rejected"/> are both terminal.
    /// </summary>
    public static readonly PlanningWorkflowSeedData.StageDefinition[] Stages = new[]
    {
        new PlanningWorkflowSeedData.StageDefinition(ProposalStageCodes.Intake,           "קליטת פנייה",                          SortOrder: 10,  IsInitial: true,  IsFinal: false),
        new PlanningWorkflowSeedData.StageDefinition(ProposalStageCodes.ProjectSetup,     "בחירת סוג פרויקט עתידי",                SortOrder: 20),
        new PlanningWorkflowSeedData.StageDefinition(ProposalStageCodes.FileMaterial,     "תיוק חומר להצעת מחיר",                  SortOrder: 25),
        new PlanningWorkflowSeedData.StageDefinition(ProposalStageCodes.MaterialCheck,    "בדיקת חומר להצעת מחיר",                 SortOrder: 30),
        new PlanningWorkflowSeedData.StageDefinition(ProposalStageCodes.Calculation,      "הכנת תחשיב להצעת מחיר",                SortOrder: 40),
        new PlanningWorkflowSeedData.StageDefinition(ProposalStageCodes.Preparation,      "הכנת הצעת מחיר",                       SortOrder: 50),
        new PlanningWorkflowSeedData.StageDefinition(ProposalStageCodes.InternalApproval, "אישור פנימי להצעת מחיר",                SortOrder: 60),
        new PlanningWorkflowSeedData.StageDefinition(ProposalStageCodes.SentFollowUp,     "שליחה ומעקב אחר הצעת מחיר",            SortOrder: 70),
        new PlanningWorkflowSeedData.StageDefinition(ProposalStageCodes.Approved,         "הצעה אושרה",                            SortOrder: 80, IsInitial: false, IsFinal: true),
        new PlanningWorkflowSeedData.StageDefinition(ProposalStageCodes.Rejected,         "הצעה נדחתה",                            SortOrder: 90, IsInitial: false, IsFinal: true),
    };

    /// <summary>
    /// Transitions between PRP.* stages. Reuses existing Quote* task results.
    /// </summary>
    public static readonly PlanningWorkflowSeedData.StageTransitionDefinition[] Transitions = new[]
    {
        // ── Linear happy path through the quote preparation ─────────────────
        // Intake: classification only — the operator decides whether this email
        // is a quote request. QuoteRequestDetected advances to ProjectSetup;
        // NotQuoteRequest terminates the proposal as Rejected. Both transitions
        // are auto-fired by the orchestrator on task result so the workflow
        // engine drives the next task instead of relying on UI plumbing.
        AutoOnTaskResult(ProposalStageCodes.Intake, ProposalStageCodes.ProjectSetup,
            taskResult: TaskResultCodes.QuoteRequestDetected,
            actions: SetStatus(ProjectStatusCodes.LeadReceived)),

        AutoOnTaskResult(ProposalStageCodes.Intake, ProposalStageCodes.Rejected,
            taskResult: TaskResultCodes.NotQuoteRequest,
            actions: SetStatus(ProjectStatusCodes.ClosedLost)),

        // Project created (OpenQuoteProject closes on ProjectCreated policy
        // which emits TaskResultCodes.ProjectOpened) → auto-advance the same
        // Proposal workflow to FileMaterial. Continuation workflows by
        // ProjectType are NOT started here; they only start after final
        // quote approval (see Approved/SentFollowUp stages).
        AutoOnTaskResult(ProposalStageCodes.ProjectSetup, ProposalStageCodes.FileMaterial,
            taskResult: TaskResultCodes.ProjectOpened,
            actions: SetStatus(ProjectStatusCodes.QuotePreparation)),

        // Operator can still decline at ProjectSetup (CreatePriceQuote skips Intake):
        // "לא הצעת מחיר" on OpenQuoteProject terminates the proposal.
        AutoOnTaskResult(ProposalStageCodes.ProjectSetup, ProposalStageCodes.Rejected,
            taskResult: TaskResultCodes.NotQuoteRequest,
            actions: SetStatus(ProjectStatusCodes.ClosedLost)),

        // Filing is closed by the existing MoveToProject pipeline emitting
        // ReviewMaterialFiled against FileQuoteMaterial (see
        // ReviewCompletionEventBehavior). No task result is recorded, so the
        // transition fires on AllRequiredTasksClosed once the FileQuoteMaterial
        // task is closed by the coordinator. EvaluationMode=Auto keeps it on
        // the orchestrator's auto-advance path (parity with AutoOnTaskResult).
        AutoLinear(ProposalStageCodes.FileMaterial, ProposalStageCodes.MaterialCheck),

        // MaterialCheck uses the shared CheckQuoteMaterialCompleteness task type,
        // whose registry + ReviewMaterialCheckCompleted behavior emit the generic
        // MaterialComplete / MaterialMissing result codes (same as MAT.Check).
        Conditional(ProposalStageCodes.MaterialCheck, ProposalStageCodes.Calculation,
            taskResult: TaskResultCodes.MaterialComplete),

        // Missing material loop — stays in MaterialCheck until material is complete.
        Conditional(ProposalStageCodes.MaterialCheck, ProposalStageCodes.MaterialCheck,
            taskResult: TaskResultCodes.MaterialMissing),

        Conditional(ProposalStageCodes.Calculation,  ProposalStageCodes.Preparation,
            taskResult: TaskResultCodes.QuoteCalculationCompleted),

        Conditional(ProposalStageCodes.Preparation,  ProposalStageCodes.InternalApproval,
            taskResult: TaskResultCodes.QuotePrepared),

        // ── Internal approval ───────────────────────────────────────────────
        Conditional(ProposalStageCodes.InternalApproval, ProposalStageCodes.SentFollowUp,
            taskResult: TaskResultCodes.QuoteApprovedInternally,
            actions: SetStatus(ProjectStatusCodes.WaitingForQuoteApproval)),

        // Internal reviewer asks for revisions → back to Preparation.
        Conditional(ProposalStageCodes.InternalApproval, ProposalStageCodes.Preparation,
            taskResult: TaskResultCodes.QuoteRequiresRevision),

        // ── Terminal outcomes ───────────────────────────────────────────────
        // Client approved → final Approved stage. The continuation workflow
        // is started AFTER a real project is created (resolved by ProjectType
        // via ProjectTypeWorkflowDefinition / ProjectWorkflowPolicyService).
        Conditional(ProposalStageCodes.SentFollowUp, ProposalStageCodes.Approved,
            taskResult: TaskResultCodes.QuoteApprovedByClient,
            actions: SetStatus(ProjectStatusCodes.WaitingForWorkOrder)),

        // Client rejected → final Rejected stage.
        Conditional(ProposalStageCodes.SentFollowUp, ProposalStageCodes.Rejected,
            taskResult: TaskResultCodes.QuoteRejectedByClient,
            actions: SetStatus(ProjectStatusCodes.ClosedLost)),
    };

    /// <summary>
    /// Explicit <c>WorkflowStageTask</c> templates per non-terminal PRP.* stage.
    /// Consumed by <c>WorkflowSeedService.SeedStageTaskTemplatesAsync</c> so that
    /// tasks created by <c>WorkflowTaskOrchestrator</c> carry a real <c>TaskTypeId</c>
    /// (resolvable by <c>TaskNavigationResolver</c> via
    /// <c>ReviewTaskInteractionRegistry</c>) instead of falling back to the
    /// legacy group-based path (which produced tasks without TaskTypeId and forced
    /// the legacy stage-code open route).
    /// <para>
    /// Reuses existing Quote* <see cref="TaskTypeCodes"/> — no new task types are
    /// introduced. Component routing is supplied by the interaction registry.
    /// </para>
    /// </summary>
    public static readonly PlanningWorkflowSeedData.StageTaskDefinition[] StageTasks = new[]
    {
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: ProposalStageCodes.Intake,
            TaskTypeCode: TaskTypeCodes.IdentifyQuoteRequest,
            AssignedGroupCode: UserGroupCodes.OfficeManagement),
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: ProposalStageCodes.ProjectSetup,
            TaskTypeCode: TaskTypeCodes.OpenQuoteProject,
            AssignedGroupCode: UserGroupCodes.OfficeManagement),
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: ProposalStageCodes.FileMaterial,
            TaskTypeCode: TaskTypeCodes.FileQuoteMaterial,
            AssignedGroupCode: UserGroupCodes.OfficeManagement),
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: ProposalStageCodes.MaterialCheck,
            TaskTypeCode: TaskTypeCodes.CheckQuoteMaterialCompleteness,
            AssignedGroupCode: UserGroupCodes.SeniorManagement),
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: ProposalStageCodes.Calculation,
            TaskTypeCode: TaskTypeCodes.PrepareQuoteCalculation,
            AssignedGroupCode: UserGroupCodes.Planners),
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: ProposalStageCodes.Preparation,
            TaskTypeCode: TaskTypeCodes.PrepareQuoteDocument,
            AssignedGroupCode: UserGroupCodes.Planners),
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: ProposalStageCodes.InternalApproval,
            TaskTypeCode: TaskTypeCodes.ApproveQuoteInternal,
            AssignedGroupCode: UserGroupCodes.SeniorManagement),
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: ProposalStageCodes.SentFollowUp,
            TaskTypeCode: TaskTypeCodes.FollowQuoteApproval,
            AssignedGroupCode: UserGroupCodes.OfficeManagement),
    };

    /// <summary>
    /// Default assigned user group for each PRP.* non-terminal stage. Consumed by
    /// <c>WorkflowSeedService</c> to populate <c>WorkflowStageDefinition.AssignedGroupId</c>,
    /// which enables the group-based task fallback in <c>WorkflowTaskOrchestrator</c>.
    /// Terminal stages (Approved/Rejected) intentionally have no group assignment.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> StageGroupAssignments =
        new Dictionary<string, string>
        {
            [ProposalStageCodes.Intake]           = UserGroupCodes.OfficeManagement,
            [ProposalStageCodes.ProjectSetup]     = UserGroupCodes.OfficeManagement,
            [ProposalStageCodes.FileMaterial]     = UserGroupCodes.OfficeManagement,
            [ProposalStageCodes.MaterialCheck]    = UserGroupCodes.SeniorManagement,
            [ProposalStageCodes.Calculation]      = UserGroupCodes.Planners,
            [ProposalStageCodes.Preparation]      = UserGroupCodes.Planners,
            [ProposalStageCodes.InternalApproval] = UserGroupCodes.SeniorManagement,
            [ProposalStageCodes.SentFollowUp]     = UserGroupCodes.OfficeManagement,
        };

    // ────────────────────────────────────────────────────────────────────────
    // Helpers (mirror PlanningWorkflowSeedData helpers)
    // ────────────────────────────────────────────────────────────────────────

    private static PlanningWorkflowSeedData.StageTransitionDefinition Linear(
        string from, string to, PlanningWorkflowSeedData.StageActionDefinition[]? actions = null)
        => new(from, to, TaskResultCode: null, actions ?? Array.Empty<PlanningWorkflowSeedData.StageActionDefinition>());

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

    /// <summary>
    /// Auto-evaluated result-driven transition. Mirrors the Review workflow
    /// pattern (<see cref="ReviewWorkflowSeedData"/>): the orchestrator's
    /// <c>CheckAndAutoAdvanceAsync</c> picks the matching rule using
    /// <see cref="Models.WorkflowTransitionTriggerType.TaskStatusChanged"/> +
    /// <see cref="Models.WorkflowTransitionConditionType.TaskResultEquals"/> +
    /// <see cref="Models.WorkflowEvaluationMode.Auto"/>.
    /// </summary>
    private static PlanningWorkflowSeedData.StageTransitionDefinition AutoOnTaskResult(
        string from, string to, string taskResult,
        PlanningWorkflowSeedData.StageActionDefinition[]? actions = null)
        => new(from, to, taskResult, actions ?? Array.Empty<PlanningWorkflowSeedData.StageActionDefinition>())
        {
            TriggerType = SiNetSQL.Models.WorkflowTransitionTriggerType.TaskStatusChanged,
            EvaluationMode = SiNetSQL.Models.WorkflowEvaluationMode.Auto,
        };

    /// <summary>
    /// Auto-evaluated unconditional transition. Used when a stage's required
    /// task is closed by the coordinator without recording a task result
    /// (e.g. <c>FileQuoteMaterial</c> closed by <c>Review.MaterialFiled</c>).
    /// Fires via <see cref="Models.WorkflowTransitionTriggerType.AllRequiredTasksClosed"/>
    /// + <see cref="Models.WorkflowTransitionConditionType.AllTasksComplete"/>
    /// in <see cref="Models.WorkflowEvaluationMode.Auto"/>.
    /// </summary>
    private static PlanningWorkflowSeedData.StageTransitionDefinition AutoLinear(
        string from, string to,
        PlanningWorkflowSeedData.StageActionDefinition[]? actions = null)
        => new(from, to, TaskResultCode: null, actions ?? Array.Empty<PlanningWorkflowSeedData.StageActionDefinition>())
        {
            TriggerType = SiNetSQL.Models.WorkflowTransitionTriggerType.AllRequiredTasksClosed,
            ConditionType = SiNetSQL.Models.WorkflowTransitionConditionType.AllTasksComplete,
            EvaluationMode = SiNetSQL.Models.WorkflowEvaluationMode.Auto,
        };

    private static PlanningWorkflowSeedData.StageActionDefinition[] SetStatus(string projectStatusCode)
        => new[]
        {
            new PlanningWorkflowSeedData.StageActionDefinition(
                PlanningWorkflowSeedData.SeedActionType.SetProjectStatus, projectStatusCode),
        };
}
