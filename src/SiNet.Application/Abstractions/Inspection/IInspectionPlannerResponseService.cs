namespace SiNet.Application.Abstractions.Inspection;

/// <summary>
/// Pulls planner responses from the previously exported Google Sheet and persists them.
/// Reuses the legacy Column-A import contract (no second pipeline).
/// </summary>
public interface IInspectionPlannerResponseService
{
    Task<InspectionPlannerResponsePullResult> PullAndPersistAsync(
        int reportId,
        bool isRepull,
        CancellationToken cancellationToken = default);
}

public sealed record InspectionPlannerResponsePullResult(
    bool Succeeded,
    string? ErrorMessage = null,
    int MatchedCount = 0,
    int SavedCount = 0)
{
    public static InspectionPlannerResponsePullResult Ok(int matched, int saved) =>
        new(true, MatchedCount: matched, SavedCount: saved);

    public static InspectionPlannerResponsePullResult Fail(string message) =>
        new(false, message);
}
