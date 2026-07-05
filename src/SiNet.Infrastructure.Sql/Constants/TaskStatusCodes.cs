namespace SiNet.Infrastructure.Sql.Constants;

/// <summary>
/// Stable codes for <see cref="Models.ProjectAssignmentStatus.Code"/>.
/// Generic execution statuses only — professional outcomes belong in
/// <see cref="TaskResultCodes"/>.
/// </summary>
public static class TaskStatusCodes
{
    public const string Open = "Open";
    public const string InProgress = "InProgress";
    public const string WaitingExternal = "WaitingExternal";
    public const string WaitingAssignment = "WaitingAssignment";
    public const string Blocked = "Blocked";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
    public const string Skipped = "Skipped";
}
