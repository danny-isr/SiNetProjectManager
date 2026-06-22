namespace SiNetProjectManagerV2.Services.Migration.Models;

public enum MigrationPreviewClassification
{
    CommitReady,
    CommitReadyWithWarning,
    AlreadyDone,
    ManagerReview,
    Conflict,
    NoMatch,
    MissingData,
    JsonMissing,
    ReviewerNotMapped,
    ReviewerGroupMismatch,
    ExistingWorkflowConflict,
    ExistingReportConflict,
    DuplicateProjectRow,
    BackwardMovement
}
