namespace SiNet.Infrastructure.Sql.Services.Tasks;

/// <summary>
/// How a task should be opened by the UI shell when the user activates it.
/// The actual UI control is selected by <see cref="TaskInteractionDefinition.ComponentKey"/>;
/// this enum describes the interaction "mode" so navigation/back-navigation/state
/// preservation can be uniform.
/// </summary>
public enum TaskOpenMode
{
    /// <summary>Default; opens the project work area for the task's project.</summary>
    ProjectWork = 0,

    /// <summary>Opens the project-creation flow seeded from an inbox email.</summary>
    ProjectCreationFromEmail = 1,

    /// <summary>Opens the email-filing component focused on the linked message/attachments.</summary>
    EmailFiling = 2,

    /// <summary>Opens the material completeness checklist for the project.</summary>
    MaterialCompletenessCheck = 3,

    /// <summary>Opens the InspectionReport component (create or load).</summary>
    InspectionReport = 4,

    /// <summary>Opens the manager-approval view of an InspectionReport.</summary>
    ManagerReviewApproval = 5,

    /// <summary>Opens the email-compose flow to send a review report to the planner.</summary>
    EmailSendToPlanner = 6,

    /// <summary>Opens the police submission preparation/send flow.</summary>
    PoliceSubmission = 7,

    /// <summary>Opens the billing-check view (only when billing is relevant).</summary>
    BillingCheck = 8,

    /// <summary>Generic task — no specialized component.</summary>
    GenericTask = 9,
}

/// <summary>
/// Stable component identifiers the UI shell maps to concrete views.
/// Keep these strings stable — they are part of the task-interaction contract.
/// </summary>
public static class TaskComponentKeys
{
    public const string ProjectCreationFromEmail = "Component.ProjectCreationFromEmail";
    public const string EmailFiling              = "Component.EmailFiling";
    public const string MaterialChecklist        = "Component.MaterialChecklist";
    public const string InspectionReport         = "Component.InspectionReport";
    public const string ManagerReviewApproval    = "Component.ManagerReviewApproval";
    public const string EmailComposeToPlanner    = "Component.EmailComposeToPlanner";
    public const string PoliceSubmission         = "Component.PoliceSubmission";
    public const string BillingCheck             = "Component.BillingCheck";
    public const string ProjectWork              = "Component.ProjectWork";
    public const string GenericTask              = "Component.GenericTask";
    public const string ReviewProjectSetupFromEmail = "Component.ReviewProjectSetupFromEmail";
}

/// <summary>
/// The kind of entity a task primarily operates on (its "work target").
/// Mirrors <see cref="Models.TaskLinkEntityType"/> but adds a few logical targets
/// that aren't single rows (checklists, exported packages).
/// </summary>
public enum TaskWorkTargetEntityType
{
    None = 0,
    EmailInboxMessage = 1,
    EmailInboxAttachment = 2,
    ProjectFile = 3,
    InspectionReport = 4,
    InspectionNote = 5,
    EmailThread = 6,
    MaterialChecklist = 7,
    ApprovalPackage = 8,
    Project = 9,
}

/// <summary>
/// How task completion is decided.
/// </summary>
public enum TaskCompletionPolicy
{
    /// <summary>Completed when a workflow TaskResult is recorded (one of <see cref="TaskInteractionDefinition.AllowedTaskResultCodes"/>).</summary>
    WorkflowResultRecorded = 0,

    /// <summary>Completed when a project is created from this task.</summary>
    ProjectCreated = 1,

    /// <summary>Completed when an inspection report reaches a "done" state.</summary>
    InspectionReportCompleted = 2,

    /// <summary>Completed when an output file (e.g. exported report) is produced.</summary>
    OutputFileCreated = 3,

    /// <summary>Completed when an email is sent.</summary>
    EmailSent = 4,

    /// <summary>Completed when an output is submitted to an external authority.</summary>
    OutputSubmitted = 5,

    /// <summary>Completed when material/correction is received against the work target.</summary>
    WorkTargetReceived = 6,

    /// <summary>Completed by closing the project (final task).</summary>
    CloseProject = 7,

    /// <summary>
    /// Completed when an explicit completion event closes the task — no TaskResult is recorded.
    /// Used by OUT.* tasks whose workflow advances on AllRequiredTasksClosed / AllTasksComplete.
    /// </summary>
    ExplicitCompletionEvent = 8,
}

/// <summary>
/// Declarative interaction metadata for a single TaskType.
/// One row per executable TaskType.
/// </summary>
public sealed record TaskInteractionDefinition(
    string TaskTypeCode,
    TaskOpenMode OpenMode,
    string ComponentKey,
    TaskWorkTargetEntityType PrimaryWorkTargetEntityType,
    SiNetSQL.Models.TaskLinkRole RequiredTaskLinkRole,
    TaskCompletionPolicy CompletionPolicy,
    IReadOnlyList<string> AllowedTaskResultCodes,
    bool AutoCloseOnCompletion,
    bool RequiresUserConfirmation);
