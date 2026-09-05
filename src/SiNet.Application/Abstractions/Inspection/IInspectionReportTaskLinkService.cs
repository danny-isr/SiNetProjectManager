namespace SiNet.Application.Abstractions.Inspection;

/// <summary>
/// Idempotently links a review task to the concrete <c>InspectionReport</c> it operates on
/// via the existing polymorphic <c>TaskLink</c> table (no new link table).
/// </summary>
public interface IInspectionReportTaskLinkService
{
    /// <summary>
    /// Ensures a single InspectionReport work-target link exists for
    /// <paramref name="taskId"/> → <paramref name="reportId"/> (Role=Related, IsWorkTarget=true).
    /// </summary>
    /// <returns>The TaskLink id of the existing or newly created row.</returns>
    ValueTask<int> EnsureReportWorkTargetLinkAsync(
        int taskId,
        int reportId,
        int userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the work-target TaskLink id between task and report, or null when absent.
    /// </summary>
    ValueTask<int?> TryGetReportWorkTargetLinkIdAsync(
        int taskId,
        int reportId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotent repair for report-based tasks: ensure InspectionReport Related/IsWorkTarget,
    /// keep trigger Email as Source only, and demote incorrect Email work targets.
    /// </summary>
    ValueTask RepairReportTaskWorkTargetsAsync(
        int taskId,
        int reportId,
        int? emailSourceEntityId,
        int userId,
        CancellationToken cancellationToken = default);
}
