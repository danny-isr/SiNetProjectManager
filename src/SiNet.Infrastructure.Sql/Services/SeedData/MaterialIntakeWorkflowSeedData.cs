using SiNet.Infrastructure.Sql.Constants;

namespace SiNet.Infrastructure.Sql.Services.SeedData;

/// <summary>
/// Reusable material-intake subworkflow (<see cref="WorkflowCodes.MaterialIntake"/>).
/// Started by any parent workflow stage that needs material gathering + completeness check.
/// </summary>
public static class MaterialIntakeWorkflowSeedData
{
    public const string Code = WorkflowCodes.MaterialIntake;
    public const string Name = "תהליך קליטת חומר";
    public const string Description = "Reusable subworkflow (MAT.*) — receive, file, check, complete material.";

    public static readonly PlanningWorkflowSeedData.StageDefinition[] Stages = new[]
    {
        Stage(MaterialStageCodes.Receive,            "קבלת חומר",              SortOrder: 10, index: 0, IsInitial: true),
        Stage(MaterialStageCodes.File,               "תיוק חומר",              SortOrder: 20, index: 1),
        Stage(MaterialStageCodes.Check,              "בדיקת שלמות חומר",       SortOrder: 30, index: 2),
        Stage(MaterialStageCodes.AwaitingCompletion, "ממתין להשלמת חומר חסר",  SortOrder: 40, index: 3),
        Stage(MaterialStageCodes.Complete,           "חומר הושלם",             SortOrder: 50, index: 4, IsFinal: true),
    };

    /// <summary>
    /// Explicit Stage + linear canvas. No empty Start node — Receive keeps IsInitial + tasks
    /// so StartAsync / AutoAdvance stay aligned with runtime.
    /// </summary>
    private static PlanningWorkflowSeedData.StageDefinition Stage(
        string code, string name, int SortOrder, int index, bool IsInitial = false, bool IsFinal = false)
    {
        var (x, y) = PlanningWorkflowSeedData.LinearCanvasLayout.At(index);
        return new PlanningWorkflowSeedData.StageDefinition(code, name, SortOrder, IsInitial, IsFinal)
        {
            NodeType = "Stage",
            CanvasX = x,
            CanvasY = y,
        };
    }

    /// <summary>
    /// Stage-task templates so the orchestrator creates real tasks (with a valid
    /// <c>TaskTypeId</c>) on workflow start and on every stage transition,
    /// instead of falling through to the legacy group-only fallback.
    /// Reuses existing task types — no new task types are introduced.
    /// </summary>
    public static readonly PlanningWorkflowSeedData.StageTaskDefinition[] StageTasks = new[]
    {
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: MaterialStageCodes.Receive,
            TaskTypeCode: TaskTypeCodes.FileInitialMaterials,
            AssignedGroupCode: UserGroupCodes.OfficeManagement),
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: MaterialStageCodes.File,
            TaskTypeCode: TaskTypeCodes.FileInitialMaterials,
            AssignedGroupCode: UserGroupCodes.OfficeManagement),
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: MaterialStageCodes.Check,
            TaskTypeCode: TaskTypeCodes.CheckQuoteMaterialCompleteness,
            AssignedGroupCode: UserGroupCodes.SeniorManagement),
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: MaterialStageCodes.AwaitingCompletion,
            TaskTypeCode: TaskTypeCodes.RequestMissingMaterial,
            AssignedGroupCode: UserGroupCodes.OfficeManagement),
        // TrackMissingMaterial closes the missing-material loop: it is the only
        // task type allowed to emit MissingMaterialReceived, which drives the
        // AwaitingCompletion → Check auto-transition. Without it the loop is a
        // dead end (RequestMissingMaterial can only emit MissingMaterialRequestSent).
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: MaterialStageCodes.AwaitingCompletion,
            TaskTypeCode: TaskTypeCodes.TrackMissingMaterial,
            AssignedGroupCode: UserGroupCodes.OfficeManagement),
    };

    /// <summary>
    /// Default per-stage group assignment, mirroring <see cref="StageTasks"/>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> StageGroupAssignments =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MaterialStageCodes.Receive]            = UserGroupCodes.OfficeManagement,
            [MaterialStageCodes.File]               = UserGroupCodes.OfficeManagement,
            [MaterialStageCodes.Check]              = UserGroupCodes.SeniorManagement,
            [MaterialStageCodes.AwaitingCompletion] = UserGroupCodes.OfficeManagement,
        };

    public static readonly PlanningWorkflowSeedData.StageTransitionDefinition[] Transitions = new[]
    {
        AutoLinear(MaterialStageCodes.Receive, MaterialStageCodes.File),
        AutoLinear(MaterialStageCodes.File,    MaterialStageCodes.Check),

        // Explicit auto transitions (TaskStatusChanged + TaskResultEquals + Auto)
        // so the orchestrator's CheckAndAutoAdvanceAsync drives the workflow as
        // soon as the MAT.Check / MAT.AwaitingCompletion tasks record a result.
        AutoOnTaskResult(MaterialStageCodes.Check, MaterialStageCodes.Complete,
            taskResult: TaskResultCodes.MaterialComplete),
        AutoOnTaskResult(MaterialStageCodes.Check, MaterialStageCodes.AwaitingCompletion,
            taskResult: TaskResultCodes.MaterialMissing,
            actions: SetStatus(ProjectStatusCodes.WaitingForMaterial)),

        AutoOnTaskResult(MaterialStageCodes.AwaitingCompletion, MaterialStageCodes.AwaitingCompletion,
            taskResult: TaskResultCodes.MissingMaterialRequestSent),
        AutoOnTaskResult(MaterialStageCodes.AwaitingCompletion, MaterialStageCodes.Check,
            taskResult: TaskResultCodes.MissingMaterialReceived),
    };

    /// <summary>
    /// Auto-evaluated unconditional transition. Fires via
    /// <see cref="Models.WorkflowTransitionTriggerType.AllRequiredTasksClosed"/> +
    /// <see cref="Models.WorkflowTransitionConditionType.AllTasksComplete"/> in
    /// <see cref="Models.WorkflowEvaluationMode.Auto"/>. Used when the stage's
    /// required task is closed by the coordinator without recording a task
    /// result (e.g. <c>FileInitialMaterials</c> closed by
    /// <c>Review.MaterialFiled</c>) and the transition is business-approved
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

    private static PlanningWorkflowSeedData.StageTransitionDefinition Conditional(
        string from, string to, string taskResult, PlanningWorkflowSeedData.StageActionDefinition[]? actions = null)
        => new(from, to, taskResult, actions ?? Array.Empty<PlanningWorkflowSeedData.StageActionDefinition>());

    /// <summary>
    /// Explicit auto-evaluated transition keyed by a task result. Mirrors the
    /// approved Proposal/Review precedent: <see cref="Models.WorkflowTransitionTriggerType.TaskStatusChanged"/>
    /// + <see cref="Models.WorkflowTransitionConditionType.TaskResultEquals"/> +
    /// <see cref="Models.WorkflowEvaluationMode.Auto"/>. Required so the
    /// orchestrator's <c>CheckAndAutoAdvanceAsync</c> drives the workflow
    /// instead of relying on default (manual) metadata synthesis.
    /// </summary>
    private static PlanningWorkflowSeedData.StageTransitionDefinition AutoOnTaskResult(
        string from, string to, string taskResult,
        PlanningWorkflowSeedData.StageActionDefinition[]? actions = null)
        => new(from, to, taskResult, actions ?? Array.Empty<PlanningWorkflowSeedData.StageActionDefinition>())
        {
            TriggerType = SiNetSQL.Models.WorkflowTransitionTriggerType.TaskStatusChanged,
            ConditionType = SiNetSQL.Models.WorkflowTransitionConditionType.TaskResultEquals,
            EvaluationMode = SiNetSQL.Models.WorkflowEvaluationMode.Auto,
        };

    private static PlanningWorkflowSeedData.StageActionDefinition[] SetStatus(string projectStatusCode)
        => new[]
        {
            new PlanningWorkflowSeedData.StageActionDefinition(
                PlanningWorkflowSeedData.SeedActionType.SetProjectStatus, projectStatusCode),
        };
}
