using SiNet.Infrastructure.Sql.Constants;

namespace SiNet.Infrastructure.Sql.Services.SeedData;

/// <summary>
/// Clean baseline seed for the Review (בדיקת תוכנית) workflow taxonomy
/// (<see cref="WorkflowCodes.Review"/>).
/// <para>
/// Stages use <c>REV.*</c> codes from <see cref="ReviewStageCodes"/>.
/// Per architecture rules: Review-specific states live in <c>WorkflowStage</c>
/// + <c>TaskResult</c>. Only the broad <see cref="ProjectStatusCodes"/> values
/// allowed for Review are referenced here.
/// </para>
/// <para>
/// Reuses the shape of <see cref="PlanningWorkflowSeedData"/> — the same DTOs
/// and <see cref="PlanningWorkflowSeedData.SeedActionType"/> are consumed by
/// <c>WorkflowSeedService</c>.
/// </para>
/// </summary>
public static class ReviewWorkflowSeedData
{
    public const string Code = WorkflowCodes.Review;
    public const string Name = "תהליך בדיקת תוכנית";
    public const string Description = "Plan review workflow (REV.*) — broad ProjectStatus only; review-specific outcomes are TaskResults.";

    /// <summary>
    /// All <c>REV.*</c> stages, in canonical SortOrder.
    /// <see cref="ReviewStageCodes.AwaitingMunicipalityRequest"/> is initial;
    /// <see cref="ReviewStageCodes.Completed"/> is final.
    /// Optional stages are included in the definition but activated per-ProjectType
    /// via <c>ProjectTypeWorkflowStage</c>.
    /// <para>
    /// TODO(review-intake): the documented target design (SiNetSQL/docs/WorkflowDecisions.md
    /// §2 "Stage Sequencing and Classification Flow", 2026-06-15) inserts a
    /// <see cref="ReviewStageCodes.Intake"/> classification stage (a
    /// <c>ClassifyRequestSource</c> task) between <see cref="ReviewStageCodes.ProjectSetup"/>
    /// and <see cref="ReviewStageCodes.MaterialIntake"/>, branching
    /// RequestFromPlanner -&gt; <see cref="ReviewStageCodes.AwaitingMunicipalityRequest"/> (a
    /// holding stage, NOT project creation) and RequestFromMunicipality -&gt;
    /// <see cref="ReviewStageCodes.MaterialIntake"/>. That stage is intentionally NOT seeded
    /// yet: the classification infrastructure (task type, registry, behavior, result-picker UI)
    /// exists, but wiring REV.Intake into the live stages/transitions is deferred to a dedicated
    /// intake-migration effort that must also reconcile the seed (initial =
    /// AwaitingMunicipalityRequest) with the runtime start (REV.MaterialIntake). Do NOT remove
    /// <c>ClassifyRequestSource</c> — it is retained for that migration. The pre-workflow
    /// "planner request, no project yet" case is already handled at the email layer by the
    /// <c>RequestAuthorityInvitation</c> suggested action (a standalone
    /// <c>RequestMunicipalityInvitation</c> tracking task with optional/null ProjectId), which
    /// does not force project creation.
    /// </para>
    /// </summary>
    public static readonly PlanningWorkflowSeedData.StageDefinition[] Stages = new[]
    {
        new PlanningWorkflowSeedData.StageDefinition(ReviewStageCodes.AwaitingMunicipalityRequest,  "המתנה לפנייה רשמית מהרשות",         SortOrder: 20,  IsInitial: true),
        new PlanningWorkflowSeedData.StageDefinition(ReviewStageCodes.ProjectSetup,                 "פתיחת פרויקט בדיקה",                 SortOrder: 30),
        new PlanningWorkflowSeedData.StageDefinition(ReviewStageCodes.MaterialIntake,               "קליטת חומר ובדיקת שלמות",            SortOrder: 40),
        new PlanningWorkflowSeedData.StageDefinition(ReviewStageCodes.ProfessionalReview,           "בדיקה מקצועית",                       SortOrder: 50),
        new PlanningWorkflowSeedData.StageDefinition(ReviewStageCodes.AwaitingManagerApproval,      "ממתין לאישור מנהל",                   SortOrder: 60),
        new PlanningWorkflowSeedData.StageDefinition(ReviewStageCodes.AwaitingPlannerCorrections,   "ממתין לתיקוני מתכנן",                 SortOrder: 70),
        new PlanningWorkflowSeedData.StageDefinition(ReviewStageCodes.RecheckRound,                 "סבב בדיקה חוזרת",                     SortOrder: 80),
        new PlanningWorkflowSeedData.StageDefinition(ReviewStageCodes.PoliceApprovalDecision,       "החלטה: נדרש אישור משטרה?",            SortOrder: 85),
        new PlanningWorkflowSeedData.StageDefinition(ReviewStageCodes.PoliceSubmission,             "הגשה למשטרה",                         SortOrder: 90),
        new PlanningWorkflowSeedData.StageDefinition(ReviewStageCodes.AwaitingPoliceApproval,       "ממתין לאישור משטרה",                  SortOrder: 100),
        new PlanningWorkflowSeedData.StageDefinition(ReviewStageCodes.AwaitingPoliceCorrections,    "ממתין לתיקונים בעקבות הערות משטרה",  SortOrder: 110),
        new PlanningWorkflowSeedData.StageDefinition(ReviewStageCodes.PoliceApproved,               "אושר ע״י משטרה",                       SortOrder: 120),
        new PlanningWorkflowSeedData.StageDefinition(ReviewStageCodes.Close,                        "סגירת פרויקט בדיקה",                   SortOrder: 130, IsInitial: false, IsFinal: false),
        new PlanningWorkflowSeedData.StageDefinition(ReviewStageCodes.Completed,                   "פרויקט בדיקה הושלם",                   SortOrder: 140, IsInitial: false, IsFinal: true),
    };

    /// <summary>
    /// Stages that are optional per-ProjectType — activated only when relevant
    /// (e.g. only when the file actually goes to police review).
    /// </summary>
    public static readonly HashSet<string> OptionalStageCodes = new(StringComparer.Ordinal)
    {
        ReviewStageCodes.AwaitingMunicipalityRequest,
        ReviewStageCodes.PoliceSubmission,
        ReviewStageCodes.AwaitingPoliceApproval,
        ReviewStageCodes.AwaitingPoliceCorrections,
        ReviewStageCodes.PoliceApproved,
    };

    /// <summary>
    /// Per-stage user group assignment (machine codes) per architecture rule §6.
    /// Mappings with multiple groups (e.g. AwaitingPoliceCorrections) use the
    /// primary owner here; secondary participation is modeled via task assignments.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> StageGroupAssignments =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ReviewStageCodes.AwaitingMunicipalityRequest] = ReviewUserGroupCodes.ReviewIntake,
            [ReviewStageCodes.ProjectSetup]                = ReviewUserGroupCodes.ProjectOpeners,
            [ReviewStageCodes.MaterialIntake]              = ReviewUserGroupCodes.Reviewers,
            [ReviewStageCodes.ProfessionalReview]          = ReviewUserGroupCodes.Reviewers,
            [ReviewStageCodes.AwaitingManagerApproval]     = ReviewUserGroupCodes.ReviewManagers,
            [ReviewStageCodes.AwaitingPlannerCorrections]  = ReviewUserGroupCodes.Reviewers,
            [ReviewStageCodes.RecheckRound]                = ReviewUserGroupCodes.Reviewers,
            [ReviewStageCodes.PoliceApprovalDecision]      = ReviewUserGroupCodes.Reviewers,
            [ReviewStageCodes.PoliceSubmission]            = ReviewUserGroupCodes.PoliceLiaison,
            [ReviewStageCodes.AwaitingPoliceApproval]      = ReviewUserGroupCodes.PoliceLiaison,
            [ReviewStageCodes.AwaitingPoliceCorrections]   = ReviewUserGroupCodes.PoliceLiaison,
            [ReviewStageCodes.PoliceApproved]              = ReviewUserGroupCodes.PoliceLiaison,
            [ReviewStageCodes.Close]                       = UserGroupCodes.OfficeManagement,
        };

    /// <summary>
    /// Stage-task templates for REV.* stages. Today only <see cref="ReviewStageCodes.Close"/>
    /// declares an explicit task — a generic project-closure task assigned to
    /// the office-management group. Other Review stages remain on the
    /// group-based fallback used by <c>WorkflowTaskOrchestrator</c>.
    /// </summary>
    public static readonly PlanningWorkflowSeedData.StageTaskDefinition[] StageTasks = new[]
    {
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: ReviewStageCodes.AwaitingMunicipalityRequest,
            TaskTypeCode: TaskTypeCodes.TrackMunicipalityInvitation,
            AssignedGroupCode: ReviewUserGroupCodes.ReviewIntake,
            IsRequired: true,
            SortOrder: 1,
            Notes: "מעקב אחרי קבלת פנייה רשמית מהרשות"),
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: ReviewStageCodes.ProjectSetup,
            TaskTypeCode: TaskTypeCodes.OpenReviewProject,
            AssignedGroupCode: ReviewUserGroupCodes.ProjectOpeners,
            IsRequired: true,
            SortOrder: 1,
            Notes: "פתיחת פרויקט בדיקה ע״י קבוצת פותחי הפרויקטים"),
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: ReviewStageCodes.ProfessionalReview,
            TaskTypeCode: TaskTypeCodes.PerformProfessionalReview,
            AssignedGroupCode: ReviewUserGroupCodes.Reviewers,
            IsRequired: true,
            SortOrder: 1,
            Notes: "ביצוע בדיקה מקצועית של החומר המוגש"),
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: ReviewStageCodes.AwaitingManagerApproval,
            TaskTypeCode: TaskTypeCodes.ApproveReviewReport,
            AssignedGroupCode: ReviewUserGroupCodes.ReviewManagers,
            IsRequired: true,
            SortOrder: 1,
            Notes: "אישור דו״ח הבדיקה ע״י מנהל"),
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: ReviewStageCodes.AwaitingPlannerCorrections,
            TaskTypeCode: TaskTypeCodes.TrackPlannerCorrections,
            AssignedGroupCode: ReviewUserGroupCodes.Reviewers,
            IsRequired: true,
            SortOrder: 1,
            Notes: "מעקב אחרי תיקוני המתכנן"),
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: ReviewStageCodes.RecheckRound,
            TaskTypeCode: TaskTypeCodes.RecheckPlan,
            AssignedGroupCode: ReviewUserGroupCodes.Reviewers,
            IsRequired: true,
            SortOrder: 1,
            Notes: "בדיקה חוזרת של התוכנית לאחר תיקונים"),
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: ReviewStageCodes.PoliceApprovalDecision,
            TaskTypeCode: TaskTypeCodes.DeterminePoliceApprovalRequirement,
            AssignedGroupCode: ReviewUserGroupCodes.Reviewers,
            IsRequired: true,
            SortOrder: 1,
            Notes: "החלטה האם התוכנית מחייבת אישור משטרה לפני סגירה"),
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: ReviewStageCodes.PoliceSubmission,
            TaskTypeCode: TaskTypeCodes.SubmitToPolice,
            AssignedGroupCode: ReviewUserGroupCodes.PoliceLiaison,
            IsRequired: true,
            SortOrder: 1,
            Notes: "הגשת התוכנית לבדיקת המשטרה"),
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: ReviewStageCodes.AwaitingPoliceApproval,
            TaskTypeCode: TaskTypeCodes.TrackPoliceApproval,
            AssignedGroupCode: ReviewUserGroupCodes.PoliceLiaison,
            IsRequired: true,
            SortOrder: 1,
            Notes: "מעקב אחרי החלטת המשטרה"),
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: ReviewStageCodes.AwaitingPoliceCorrections,
            TaskTypeCode: TaskTypeCodes.ForwardPoliceCommentsToPlanner,
            AssignedGroupCode: ReviewUserGroupCodes.PoliceLiaison,
            IsRequired: true,
            SortOrder: 1,
            Notes: "העברת הערות המשטרה למתכנן לטיפול"),
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: ReviewStageCodes.PoliceApproved,
            TaskTypeCode: TaskTypeCodes.FileFinalApprovals,
            AssignedGroupCode: ReviewUserGroupCodes.PoliceLiaison,
            IsRequired: true,
            SortOrder: 1,
            Notes: "תיוק האישורים הסופיים לאחר אישור המשטרה"),
        new PlanningWorkflowSeedData.StageTaskDefinition(
            StageCode: ReviewStageCodes.Close,
            TaskTypeCode: TaskTypeCodes.CloseProject,
            AssignedGroupCode: UserGroupCodes.OfficeManagement,
            IsRequired: true,
            SortOrder: 1,
            Notes: "בדיקה מנהלתית ואישור סגירת הפרויקט"),
    };

    /// <summary>
    /// Stage that hosts the reusable <see cref="WorkflowCodes.MaterialIntake"/>
    /// subworkflow. The seeder will set <c>NodeType=SubWorkflow</c> and link
    /// <c>SubWorkflowDefinitionId</c> for this stage.
    /// </summary>
    public const string SubWorkflowStageCode = ReviewStageCodes.MaterialIntake;

    /// <summary>Transitions between REV.* stages.</summary>
    public static readonly PlanningWorkflowSeedData.StageTransitionDefinition[] Transitions = new[]
    {
        Conditional(ReviewStageCodes.AwaitingMunicipalityRequest, ReviewStageCodes.ProjectSetup,
            taskResult: TaskResultCodes.MunicipalityRequestReceived),

        // Project opened → start MaterialIntake sub-workflow on REV.MaterialIntake.
        // Explicit auto metadata so the orchestrator drives the workflow as
        // soon as the ProjectSetup task records ProjectOpened.
        AutoOnTaskResult(ReviewStageCodes.ProjectSetup, ReviewStageCodes.MaterialIntake,
            taskResult: TaskResultCodes.ProjectOpened,
            actions: new[]
            {
                new PlanningWorkflowSeedData.StageActionDefinition(
                    PlanningWorkflowSeedData.SeedActionType.SetProjectStatus, ProjectStatusCodes.Active),
                new PlanningWorkflowSeedData.StageActionDefinition(
                    PlanningWorkflowSeedData.SeedActionType.StartSubWorkflow),
            }),

        // MaterialIntake (sub-workflow) completion → ProfessionalReview.
        // Fired by WorkflowTaskOrchestrator.NotifyParentOfSubWorkflowCompletionAsync
        // when the child MAT.* workflow reaches MAT.Complete.
        PlanningWorkflowSeedData.SubWorkflowSucceeded(
            ReviewStageCodes.MaterialIntake, ReviewStageCodes.ProfessionalReview,
            actions: SetStatus(ProjectStatusCodes.Active)),

        // MaterialIntake failure fallback → Close.
        // If the MAT.* sub-workflow fails or is cancelled, route to Close
        // so the user can decide whether to retry or close the project.
        PlanningWorkflowSeedData.SubWorkflowFailed(
            ReviewStageCodes.MaterialIntake, ReviewStageCodes.Close),

        // Professional review → manager.
        Conditional(ReviewStageCodes.ProfessionalReview, ReviewStageCodes.AwaitingManagerApproval,
            taskResult: TaskResultCodes.ProfessionalReviewCompleted),

        // Manager.
        Conditional(ReviewStageCodes.AwaitingManagerApproval, ReviewStageCodes.AwaitingPlannerCorrections,
            taskResult: TaskResultCodes.ManagerApproved,
            actions: SetStatus(ProjectStatusCodes.WaitingForClient)),
        Conditional(ReviewStageCodes.AwaitingManagerApproval, ReviewStageCodes.ProfessionalReview,
            taskResult: TaskResultCodes.ManagerRequestedChanges),

        // Planner corrections received → recheck (back to Active).
        Conditional(ReviewStageCodes.AwaitingPlannerCorrections, ReviewStageCodes.RecheckRound,
            taskResult: TaskResultCodes.PlannerCorrectionsReceived,
            actions: SetStatus(ProjectStatusCodes.Active)),

        // Recheck outcomes.
        Conditional(ReviewStageCodes.RecheckRound, ReviewStageCodes.AwaitingPlannerCorrections,
            taskResult: TaskResultCodes.RecheckRequiresMoreCorrections,
            actions: SetStatus(ProjectStatusCodes.WaitingForClient)),
        // Recheck passed → decide whether police/authority approval is required.
        Conditional(ReviewStageCodes.RecheckRound, ReviewStageCodes.PoliceApprovalDecision,
            taskResult: TaskResultCodes.RecheckPassed),

        // Police-approval decision (DeterminePoliceApprovalRequirement task).
        // Required → enter the police path; NotRequired → straight to Close.
        Conditional(ReviewStageCodes.PoliceApprovalDecision, ReviewStageCodes.PoliceSubmission,
            taskResult: TaskResultCodes.PoliceApprovalRequired),
        Conditional(ReviewStageCodes.PoliceApprovalDecision, ReviewStageCodes.Close,
            taskResult: TaskResultCodes.PoliceApprovalNotRequired),

        // Police path.
        Conditional(ReviewStageCodes.PoliceSubmission, ReviewStageCodes.AwaitingPoliceApproval,
            taskResult: TaskResultCodes.SubmittedToPolice,
            actions: SetStatus(ProjectStatusCodes.WaitingForAuthority)),
        Conditional(ReviewStageCodes.AwaitingPoliceApproval, ReviewStageCodes.PoliceApproved,
            taskResult: TaskResultCodes.PoliceApproved),
        Conditional(ReviewStageCodes.AwaitingPoliceApproval, ReviewStageCodes.AwaitingPoliceCorrections,
            taskResult: TaskResultCodes.PoliceCommentsReceived,
            actions: SetStatus(ProjectStatusCodes.WaitingForClient)),
        Conditional(ReviewStageCodes.AwaitingPoliceCorrections, ReviewStageCodes.PoliceSubmission,
            taskResult: TaskResultCodes.PoliceCorrectionsReceived,
            actions: SetStatus(ProjectStatusCodes.Active)),

        Linear(ReviewStageCodes.PoliceApproved, ReviewStageCodes.Close),

        // Auto-advance PoliceApproved → Close when the ApproveOrClose action
        // completes successfully. Coexists with the Manual/Always rule above;
        // lower Priority value ⇒ evaluated first by WorkflowTransitionEvaluator.
        ActionCompleted(
            ReviewStageCodes.PoliceApproved,
            ReviewStageCodes.Close,
            actionCode: "ApproveOrClose",
            outcome: "Succeeded",
            name: "אושר / נסגר מתוך Action",
            priority: -100),

        // Close — decided by the OfficeManagement employee on the
        // generic CloseProject task (see ReviewWorkflowSeedData.StageTasks).
        //
        // Approved → RecordTaskResult(ProjectClosed) + CloseProject action,
        //   then advance to REV.Completed (terminal IsFinal stage) which
        //   completes the workflow and fires NotifyParentOfSubWorkflowCompletionAsync.
        // Rejected / NeedsMoreInfo → self-loop back to REV.Close; a new
        //   CloseProject task is created so the user can retry.
        //
        // Trigger is TaskStatusChanged + Auto so
        // CheckAndAutoAdvanceAsync picks these up via task-result evaluation.
        AutoOnTaskResult(ReviewStageCodes.Close, ReviewStageCodes.Completed,
            taskResult: TaskResultCodes.ProjectCloseApproved,
            actions: new[]
            {
                new PlanningWorkflowSeedData.StageActionDefinition(
                    PlanningWorkflowSeedData.SeedActionType.RecordTaskResult,
                    TaskResultCodes.ProjectClosed),
                new PlanningWorkflowSeedData.StageActionDefinition(
                    PlanningWorkflowSeedData.SeedActionType.CloseProject),
            }),
        AutoOnTaskResult(ReviewStageCodes.Close, ReviewStageCodes.Close,
            taskResult: TaskResultCodes.ProjectCloseRejected),
        AutoOnTaskResult(ReviewStageCodes.Close, ReviewStageCodes.Close,
            taskResult: TaskResultCodes.ProjectCloseNeedsMoreInfo),
    };

    private static PlanningWorkflowSeedData.StageTransitionDefinition Linear(
        string from, string to, PlanningWorkflowSeedData.StageActionDefinition[]? actions = null)
        => new(from, to, TaskResultCode: null, actions ?? Array.Empty<PlanningWorkflowSeedData.StageActionDefinition>());

    private static PlanningWorkflowSeedData.StageTransitionDefinition Conditional(
        string from, string to, string taskResult, PlanningWorkflowSeedData.StageActionDefinition[]? actions = null)
        => new(from, to, taskResult, actions ?? Array.Empty<PlanningWorkflowSeedData.StageActionDefinition>())
        {
            TriggerType = SiNetSQL.Models.WorkflowTransitionTriggerType.TaskStatusChanged,
            EvaluationMode = SiNetSQL.Models.WorkflowEvaluationMode.Auto,
        };

    /// <summary>
    /// Builds a self-loop / stage-transition that auto-fires when a task on the
    /// current stage records a specific <see cref="TaskResultCodes"/>. Uses
    /// <see cref="Models.WorkflowTransitionTriggerType.TaskStatusChanged"/> +
    /// <see cref="Models.WorkflowTransitionConditionType.TaskResultEquals"/> +
    /// <see cref="Models.WorkflowEvaluationMode.Auto"/> so the orchestrator's
    /// <c>CheckAndAutoAdvanceAsync</c> can pick it up after task completion.
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
    /// Builds an auto-evaluated transition that fires when a specific action completes
    /// (optionally with a required outcome). Used to bridge <c>ActionExecutor</c>
    /// lifecycle events into the workflow engine.
    /// </summary>
    private static PlanningWorkflowSeedData.StageTransitionDefinition ActionCompleted(
        string from,
        string to,
        string actionCode,
        string? outcome = null,
        string? name = null,
        int? priority = null,
        PlanningWorkflowSeedData.StageActionDefinition[]? actions = null)
    {
        var conditionJson = outcome is null
            ? $"{{\"ActionCode\":\"{actionCode}\"}}"
            : $"{{\"ActionCode\":\"{actionCode}\",\"Outcome\":\"{outcome}\"}}";

        return new PlanningWorkflowSeedData.StageTransitionDefinition(
            from,
            to,
            TaskResultCode: null,
            Actions: actions ?? Array.Empty<PlanningWorkflowSeedData.StageActionDefinition>())
        {
            TriggerType = SiNetSQL.Models.WorkflowTransitionTriggerType.ActionCompleted,
            ConditionType = SiNetSQL.Models.WorkflowTransitionConditionType.ActionCompleted,
            ConditionJson = conditionJson,
            EvaluationMode = SiNetSQL.Models.WorkflowEvaluationMode.Auto,
            Priority = priority,
            Name = name,
        };
    }

    private static PlanningWorkflowSeedData.StageActionDefinition[] SetStatus(string projectStatusCode)
        => new[]
        {
            new PlanningWorkflowSeedData.StageActionDefinition(
                PlanningWorkflowSeedData.SeedActionType.SetProjectStatus, projectStatusCode),
        };
}
