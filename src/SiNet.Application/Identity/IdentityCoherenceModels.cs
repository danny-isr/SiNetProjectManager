namespace SiNet.Application.Identity;

/// <summary>Overall identity coherence state for UI and operation guards.</summary>
public enum IdentityCoherenceStatus
{
    Checking = 0,
    Match = 1,
    PendingApproval = 2,
    IncompleteSiUser = 3,
    NotConnected = 4,
    Mismatch = 5,
    Blocked = 6,
    /// <summary>Authorized + Google match, but ACC membership for the active project was not verified.</summary>
    AccUnverified = 7,
}

/// <summary>Auth mode reported for ACC Data Management (not the human member).</summary>
public enum AccAuthMode
{
    Unknown = 0,
    ApplicationTwoLegged = 1,
    UserThreeLegged = 2,
}

/// <summary>
/// Snapshot for shell status + service guards. Never includes tokens or secrets.
/// See <c>docs/IDENTITY_SIUSER_GATE.md</c>.
/// </summary>
public sealed record IdentityCoherenceSnapshot(
    IdentityCoherenceStatus Status,
    int? SiUserId,
    string? SiUserName,
    string? SiUserLoginName,
    string? SiUserEmail,
    bool GoogleAuthenticated,
    string? GoogleEmail,
    bool? GoogleMatch,
    bool? GmailMatch,
    bool? DriveMatch,
    bool? SheetsMatch,
    AccAuthMode AccAuthMode,
    string? AccMembershipEmail,
    bool? AccMembershipMatch,
    string? AutodeskThreeLeggedEmail,
    bool? AutodeskThreeLeggedMatch,
    string? FailureReason,
    string? AccAccessLevel = null,
    int? SiProjectId = null,
    string? AccProjectId = null,
    bool AccRelevant = false)
{
    public static IdentityCoherenceSnapshot Checking() =>
        new(
            Status: IdentityCoherenceStatus.Checking,
            SiUserId: null,
            SiUserName: null,
            SiUserLoginName: null,
            SiUserEmail: null,
            GoogleAuthenticated: false,
            GoogleEmail: null,
            GoogleMatch: null,
            GmailMatch: null,
            DriveMatch: null,
            SheetsMatch: null,
            AccAuthMode: AccAuthMode.Unknown,
            AccMembershipEmail: null,
            AccMembershipMatch: null,
            AutodeskThreeLeggedEmail: null,
            AutodeskThreeLeggedMatch: null,
            FailureReason: null);
}

/// <summary>Operations gated by identity coherence before external side effects.</summary>
public enum IdentityOperationKind
{
    GmailWrite = 1,
    GoogleDriveWrite = 2,
    GoogleSheetsWrite = 3,
    AccProjectMembershipWrite = 4,
    AccFileWrite = 5,
    AutodeskThreeLeggedWrite = 6,
    WorkflowMutate = 7,
    ProjectMutate = 8,
    AdminSettingsWrite = 9,
    CrossSystemWorkflow = 10,
}
