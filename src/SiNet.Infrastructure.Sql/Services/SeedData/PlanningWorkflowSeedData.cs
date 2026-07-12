using SiNet.Infrastructure.Sql.Constants;

namespace SiNet.Infrastructure.Sql.Services.SeedData;

/// <summary>
/// Clean baseline seed for the new PlanningWorkflow taxonomy
/// (<see cref="WorkflowCodes.PlanningWorkflow"/>).
/// <para>
/// The legacy Design / Proposal / Review / Opinion / Intake / ScopeExpansion workflows
/// are intentionally NOT modified by this seed. They remain as-is for any code that
/// still references them; new planning work should target <c>PlanningWorkflow</c>.
/// </para>
/// <para>
/// Stages use <c>PLN.*</c> codes from <see cref="PlanningStageCodes"/>. Transitions are
/// expressed as <see cref="StageTransitionDefinition"/> records and consumed by
/// <c>WorkflowSeedService</c> to create <c>WorkflowTransitionRule</c> rows
/// (with optional <c>WorkflowTransitionAction</c> rows).
/// </para>
/// </summary>
public static class PlanningWorkflowSeedData
{
    public const string Code = WorkflowCodes.PlanningWorkflow;
    public const string Name = "תהליך תכנון פרויקט";
    public const string Description = "Planning workflow taxonomy (PLN.*) — clean separation of ProjectStatus, WorkflowStage, TaskStatus and TaskResult.";

    /// <summary>
    /// All <c>PLN.*</c> stages, in their canonical SortOrder.
    /// <see cref="PlanningStageCodes.Intake"/> is initial; <see cref="PlanningStageCodes.Close"/> is final.
    /// </summary>
    public static readonly StageDefinition[] Stages = new[]
    {
        // LEGACY DISABLED 2026-05-20: Initial quote stages (Intake + Quote*) moved to
        // Proposal workflow (PRP.*, see ProposalWorkflowSeedData). Removed from the
        // active PlanningWorkflow seed to stop them from being an active misleading
        // business path. Existing DB rows from previous seeds are left untouched
        // (the seeder is additive). Candidate for full deletion after validation.
        new StageDefinition(PlanningStageCodes.WorkOrder,                "קבלת הזמנת עבודה",                      SortOrder: 80, IsInitial: true,  IsFinal: false),
        new StageDefinition(PlanningStageCodes.ExecutionMaterialCheck,   "בדיקת חומר לתחילת עבודה",               SortOrder: 90),
        new StageDefinition(PlanningStageCodes.PlanningStart,            "פתיחת עבודת תכנון",                     SortOrder: 100),
        new StageDefinition(PlanningStageCodes.DesignDraft,              "טיוטה / תוכניות ראשוניות",              SortOrder: 110),
        new StageDefinition(PlanningStageCodes.DesignPreliminary,        "תכנון מוקדם",                          SortOrder: 120),
        new StageDefinition(PlanningStageCodes.DesignDetailed,           "תכנון מפורט",                          SortOrder: 130),
        new StageDefinition(PlanningStageCodes.ApprovalSubmission,       "הגשה לאישור",                          SortOrder: 140),
        new StageDefinition(PlanningStageCodes.ApprovalComments,         "טיפול בהערות גורם מאשר",                SortOrder: 150),
        new StageDefinition(PlanningStageCodes.ApprovalAuthorityApproved,"אושר על ידי גורם מאשר",                 SortOrder: 160),
        new StageDefinition(PlanningStageCodes.DesignWorkPlans,          "תוכניות עבודה",                         SortOrder: 170),
        new StageDefinition(PlanningStageCodes.BillingCheckMilestone,    "בדיקת אבן דרך לחשבון",                  SortOrder: 180),
        new StageDefinition(PlanningStageCodes.Close,                    "סגירת פרויקט",                          SortOrder: 190, IsInitial: false, IsFinal: true),
    };

    /// <summary>
    /// Stage-task templates so the orchestrator creates a real task with a valid
    /// <c>TaskTypeId</c> on workflow start and on stage transitions, instead of
    /// falling through to the legacy group-only fallback (which trips start-preflight).
    /// Reuses existing task types — no new task types are introduced.
    /// </summary>
    public static readonly StageTaskDefinition[] StageTasks = new[]
    {
        new StageTaskDefinition(
            StageCode: PlanningStageCodes.WorkOrder,
            TaskTypeCode: TaskTypeCodes.FollowWorkOrder,
            AssignedGroupCode: UserGroupCodes.OfficeManagement),
        // PLN.Execution.MaterialCheck has no template — it is a SubWorkflow node
        // whose tasks come from the MaterialIntake (MAT.*) sub-workflow itself.
        new StageTaskDefinition(
            StageCode: PlanningStageCodes.PlanningStart,
            TaskTypeCode: TaskTypeCodes.OpenPlanningWorkPackage,
            AssignedGroupCode: UserGroupCodes.SeniorManagement),
        new StageTaskDefinition(
            StageCode: PlanningStageCodes.DesignDraft,
            TaskTypeCode: TaskTypeCodes.PrepareDraftPlans,
            AssignedGroupCode: UserGroupCodes.Planners),
        new StageTaskDefinition(
            StageCode: PlanningStageCodes.DesignPreliminary,
            TaskTypeCode: TaskTypeCodes.PreparePreliminaryDesign,
            AssignedGroupCode: UserGroupCodes.Planners),
        new StageTaskDefinition(
            StageCode: PlanningStageCodes.DesignDetailed,
            TaskTypeCode: TaskTypeCodes.PrepareDetailedDesign,
            AssignedGroupCode: UserGroupCodes.Planners),
        new StageTaskDefinition(
            StageCode: PlanningStageCodes.ApprovalSubmission,
            TaskTypeCode: TaskTypeCodes.SubmitForApproval,
            AssignedGroupCode: UserGroupCodes.OfficeManagement),
        new StageTaskDefinition(
            StageCode: PlanningStageCodes.ApprovalComments,
            TaskTypeCode: TaskTypeCodes.HandleAuthorityComments,
            AssignedGroupCode: UserGroupCodes.Planners),
        new StageTaskDefinition(
            StageCode: PlanningStageCodes.ApprovalAuthorityApproved,
            TaskTypeCode: TaskTypeCodes.FollowAuthorityApproval,
            AssignedGroupCode: UserGroupCodes.OfficeManagement),
        new StageTaskDefinition(
            StageCode: PlanningStageCodes.DesignWorkPlans,
            TaskTypeCode: TaskTypeCodes.PrepareWorkPlans,
            AssignedGroupCode: UserGroupCodes.Planners),
        new StageTaskDefinition(
            StageCode: PlanningStageCodes.BillingCheckMilestone,
            TaskTypeCode: TaskTypeCodes.CheckBillingMilestone,
            AssignedGroupCode: UserGroupCodes.OfficeManagement),
    };

    /// <summary>
    /// Default per-stage group assignment, mirroring <see cref="StageTasks"/> so
    /// the start-preflight has an assigned group even on stages that don't yet
    /// declare an explicit template.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> StageGroupAssignments =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PlanningStageCodes.WorkOrder]                 = UserGroupCodes.OfficeManagement,
            [PlanningStageCodes.ExecutionMaterialCheck]    = UserGroupCodes.OfficeManagement,
            [PlanningStageCodes.PlanningStart]             = UserGroupCodes.SeniorManagement,
            [PlanningStageCodes.DesignDraft]               = UserGroupCodes.Planners,
            [PlanningStageCodes.DesignPreliminary]         = UserGroupCodes.Planners,
            [PlanningStageCodes.DesignDetailed]            = UserGroupCodes.Planners,
            [PlanningStageCodes.ApprovalSubmission]        = UserGroupCodes.OfficeManagement,
            [PlanningStageCodes.ApprovalComments]          = UserGroupCodes.Planners,
            [PlanningStageCodes.ApprovalAuthorityApproved] = UserGroupCodes.OfficeManagement,
            [PlanningStageCodes.DesignWorkPlans]           = UserGroupCodes.Planners,
            [PlanningStageCodes.BillingCheckMilestone]     = UserGroupCodes.OfficeManagement,
        };

    /// <summary>
    /// Transitions between PLN.* stages. The executor reads <see cref="StageActionDefinition"/>s
    /// to materialize <c>WorkflowTransitionAction</c> rows.
    /// </summary>
    public static readonly StageTransitionDefinition[] Transitions = new[]
    {
        // LEGACY DISABLED 2026-05-20: Quote-phase transitions (Intake → … →
        // QuoteSentFollowUp → WorkOrder) were moved to ProposalWorkflowSeedData
        // (PRP.*). PlanningWorkflow now starts at WorkOrder, which is reached
        // either from the Proposal workflow on QuoteApprovedByClient + project
        // creation, or directly when a project is created with an existing
        // work order. Do not re-add quote stages here as the active path.
        // PLN.WorkOrder → PLN.Execution.MaterialCheck: explicit AUTO transition
        // (approved). Fires automatically when FollowWorkOrder is closed with
        // result=WorkOrderReceived, sets ProjectStatus=Active, and starts the
        // hosted MaterialIntake sub-workflow. This is a per-transition opt-in;
        // the global Linear/Conditional defaults remain Manual.
        new StageTransitionDefinition(
            PlanningStageCodes.WorkOrder,
            PlanningStageCodes.ExecutionMaterialCheck,
            TaskResultCode: TaskResultCodes.WorkOrderReceived,
            Actions: new[]
            {
                new StageActionDefinition(SeedActionType.SetProjectStatus, ProjectStatusCodes.Active),
                new StageActionDefinition(SeedActionType.StartSubWorkflow),
            })
        {
            TriggerType = SiNetSQL.Models.WorkflowTransitionTriggerType.AllRequiredTasksClosed,
            ConditionType = SiNetSQL.Models.WorkflowTransitionConditionType.TaskResultEquals,
            EvaluationMode = SiNetSQL.Models.WorkflowEvaluationMode.Auto,
        },

        // PLN.Execution.MaterialCheck is a SubWorkflow node hosting MaterialIntake
        // (MAT.*). When the sub-workflow completes successfully we auto-advance
        // to PlanningStart via the SubWorkflowCompleted trigger.
        SubWorkflowSucceeded(PlanningStageCodes.ExecutionMaterialCheck, PlanningStageCodes.PlanningStart),

        // If the MAT.* sub-workflow fails or is cancelled, route to PLN.Close
        // so the project can be closed cleanly rather than getting stuck.
        SubWorkflowFailed(PlanningStageCodes.ExecutionMaterialCheck, PlanningStageCodes.Close,
            actions: new[]
            {
                new StageActionDefinition(SeedActionType.CloseProject),
            }),

        // ProjectTypeWorkflowStage decides which design stage is the "first active" one.
        // Transitions cover all common adjacencies; inactive stages will simply be skipped
        // by the workflow engine.
        Linear(PlanningStageCodes.PlanningStart,         PlanningStageCodes.DesignDraft),
        Linear(PlanningStageCodes.PlanningStart,         PlanningStageCodes.DesignPreliminary),
        Linear(PlanningStageCodes.PlanningStart,         PlanningStageCodes.DesignDetailed),
        Linear(PlanningStageCodes.PlanningStart,         PlanningStageCodes.ApprovalSubmission),

        Linear(PlanningStageCodes.DesignDraft,           PlanningStageCodes.DesignPreliminary),
        Linear(PlanningStageCodes.DesignDraft,           PlanningStageCodes.DesignDetailed),
        Linear(PlanningStageCodes.DesignPreliminary,     PlanningStageCodes.DesignDetailed),
        Linear(PlanningStageCodes.DesignDetailed,        PlanningStageCodes.ApprovalSubmission),

        // ── Authority approval loop ─────────────────────────────────────────
        Conditional(PlanningStageCodes.ApprovalSubmission, PlanningStageCodes.ApprovalComments,
            taskResultCode: TaskResultCodes.AuthorityCommentsReceived,
            actions: SetStatus(ProjectStatusCodes.WaitingForAuthority)),

        Conditional(PlanningStageCodes.ApprovalSubmission, PlanningStageCodes.ApprovalAuthorityApproved,
            taskResultCode: TaskResultCodes.AuthorityApproved),

        Conditional(PlanningStageCodes.ApprovalComments, PlanningStageCodes.ApprovalSubmission,
            taskResultCode: TaskResultCodes.CorrectionsCompleted),

        // ── Approved → work plans (optional) → billing → close ─────────────
        Linear(PlanningStageCodes.ApprovalAuthorityApproved, PlanningStageCodes.DesignWorkPlans),
        Linear(PlanningStageCodes.ApprovalAuthorityApproved, PlanningStageCodes.BillingCheckMilestone),
        Linear(PlanningStageCodes.DesignWorkPlans,         PlanningStageCodes.BillingCheckMilestone),

        Linear(PlanningStageCodes.BillingCheckMilestone,   PlanningStageCodes.Close,
            actions: new[]
            {
                new StageActionDefinition(SeedActionType.SetBillingPending),
                new StageActionDefinition(SeedActionType.CloseProject),
            }),
    };

    // ────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────

    private static StageTransitionDefinition Linear(string from, string to, StageActionDefinition[]? actions = null)
        => new(from, to, TaskResultCode: null, actions ?? Array.Empty<StageActionDefinition>());

    private static StageTransitionDefinition Conditional(string from, string to, string taskResultCode, StageActionDefinition[]? actions = null)
        => new(from, to, taskResultCode, actions ?? Array.Empty<StageActionDefinition>());

    private static StageActionDefinition[] SetStatus(string projectStatusCode)
        => new[] { new StageActionDefinition(SeedActionType.SetProjectStatus, projectStatusCode) };

    /// <summary>
    /// Builds an auto-evaluated transition that fires when the sub-workflow hosted
    /// on <paramref name="from"/> completes successfully. Mirrors the runtime hook
    /// in <c>WorkflowTaskOrchestrator.NotifyParentOfSubWorkflowCompletionAsync</c>
    /// which emits <see cref="Models.WorkflowTransitionTriggerType.SubWorkflowCompleted"/>
    /// with the <c>SubWorkflowSucceeded</c> evaluation context flag.
    /// </summary>
    internal static StageTransitionDefinition SubWorkflowSucceeded(
        string from, string to, StageActionDefinition[]? actions = null)
        => new(from, to, TaskResultCode: null, actions ?? Array.Empty<StageActionDefinition>())
        {
            TriggerType = SiNetSQL.Models.WorkflowTransitionTriggerType.SubWorkflowCompleted,
            ConditionType = SiNetSQL.Models.WorkflowTransitionConditionType.SubWorkflowSucceeded,
            EvaluationMode = SiNetSQL.Models.WorkflowEvaluationMode.Auto,
        };

    /// <summary>
    /// Builds an auto-evaluated transition that fires when the sub-workflow hosted
    /// on <paramref name="from"/> completes with a failure / cancellation. Mirrors
    /// <see cref="SubWorkflowSucceeded"/> but uses
    /// <see cref="Models.WorkflowTransitionConditionType.SubWorkflowFailed"/>.
    /// </summary>
    internal static StageTransitionDefinition SubWorkflowFailed(
        string from, string to, StageActionDefinition[]? actions = null)
        => new(from, to, TaskResultCode: null, actions ?? Array.Empty<StageActionDefinition>())
        {
            TriggerType = SiNetSQL.Models.WorkflowTransitionTriggerType.SubWorkflowCompleted,
            ConditionType = SiNetSQL.Models.WorkflowTransitionConditionType.SubWorkflowFailed,
            EvaluationMode = SiNetSQL.Models.WorkflowEvaluationMode.Auto,
        };

    // ────────────────────────────────────────────────────────────────────────
    // DTOs
    // ────────────────────────────────────────────────────────────────────────

    public record StageDefinition(
        string Code,
        string Name,
        int SortOrder,
        bool IsInitial = false,
        bool IsFinal = false)
    {
        /// <summary>
        /// Optional visual/runtime node type (Stage, Start, End, SubWorkflow, …).
        /// When null, the seeder does not overwrite an existing NodeType (inserts default to Stage).
        /// Never used to downgrade an existing SubWorkflow node.
        /// </summary>
        public string? NodeType { get; init; }

        /// <summary>Optional canvas X. When null, seeder leaves existing / 0 on insert.</summary>
        public double? CanvasX { get; init; }

        /// <summary>Optional canvas Y. When null, seeder leaves existing / 0 on insert.</summary>
        public double? CanvasY { get; init; }
    }

    /// <summary>Shared linear canvas spacing for seeded chain workflows (matches New System viewer fallback).</summary>
    public static class LinearCanvasLayout
    {
        public const double StartX = 40;
        public const double RowY = 120;
        public const double HorizontalGap = 220;

        public static (double X, double Y) At(int indexInSortOrder) =>
            (StartX + indexInSortOrder * HorizontalGap, RowY);
    }

    public record StageTransitionDefinition(
        string FromStageCode,
        string ToStageCode,
        string? TaskResultCode,
        StageActionDefinition[] Actions)
    {
        /// <summary>
        /// Optional explicit trigger type. When <c>null</c>, <see cref="Workflow.WorkflowSeedService"/>
        /// keeps the legacy default (<see cref="Models.WorkflowTransitionTriggerType.Manual"/>).
        /// </summary>
        public SiNetSQL.Models.WorkflowTransitionTriggerType? TriggerType { get; init; }

        /// <summary>
        /// Optional explicit condition type. When <c>null</c>, the legacy default
        /// (<see cref="Models.WorkflowTransitionConditionType.Always"/> or
        /// <see cref="Models.WorkflowTransitionConditionType.TaskResultEquals"/>) is used.
        /// </summary>
        public SiNetSQL.Models.WorkflowTransitionConditionType? ConditionType { get; init; }

        /// <summary>
        /// Optional explicit condition JSON. When <c>null</c>, the seed service synthesises one
        /// from <see cref="TaskResultCode"/> for backwards compatibility.
        /// </summary>
        public string? ConditionJson { get; init; }

        /// <summary>
        /// Optional explicit evaluation mode. When <c>null</c>, the legacy default
        /// (<see cref="Models.WorkflowEvaluationMode.Manual"/>) is used.
        /// </summary>
        public SiNetSQL.Models.WorkflowEvaluationMode? EvaluationMode { get; init; }

        /// <summary>Optional explicit priority. When <c>null</c>, the entity default (0) is used.</summary>
        public int? Priority { get; init; }

        /// <summary>Optional explicit rule name. When <c>null</c>, the seed service synthesises one.</summary>
        public string? Name { get; init; }
    }

    public record StageActionDefinition(
        SeedActionType ActionType,
        string? Payload = null);

    /// <summary>
    /// Declares a <see cref="Models.WorkflowStageTask"/> template that the
    /// <see cref="Workflow.WorkflowTaskOrchestrator"/> uses to materialize a
    /// <see cref="Models.ProjectAssignment"/> when the workflow enters
    /// <paramref name="StageCode"/>.
    /// <para>
    /// When at least one template exists for a stage the orchestrator stops
    /// falling back to group-based creation, so the seed only declares
    /// templates for stages that need explicit task semantics.
    /// </para>
    /// </summary>
    public record StageTaskDefinition(
        string StageCode,
        string TaskTypeCode,
        string AssignedGroupCode,
        bool IsRequired = true,
        int SortOrder = 1,
        string? Notes = null);

    public enum SeedActionType
    {
        SetProjectStatus = 0,
        RecordTaskResult = 1,
        SetBillingPending = 2,
        CloseProject = 3,

        /// <summary>
        /// Starts the sub-workflow linked to the transition's target stage
        /// (via <see cref="Models.WorkflowStageDefinition.SubWorkflowDefinitionId"/>).
        /// The child workflow is persisted with an explicit
        /// <see cref="Models.WorkflowInstance.ParentWorkflowInstanceId"/>, and the
        /// parent is notified on child completion via
        /// <see cref="Models.WorkflowTransitionTriggerType.SubWorkflowCompleted"/>.
        /// </summary>
        StartSubWorkflow = 4,
    }
}
