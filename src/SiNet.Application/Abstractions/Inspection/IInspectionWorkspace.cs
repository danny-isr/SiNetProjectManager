namespace SiNet.Application.Abstractions.Inspection;

/// <summary>
/// UI-agnostic port for the new Inspection screen. This is the future seam through which the
/// rebuilt WPF Inspection UI will reach reusable inspection logic (currently embodied by the
/// legacy-extracted services/builders such as <c>IInspectionDrawingManagementService</c>,
/// <c>InspectionReviewedPlanBuilder</c>, etc.). A LegacyBridge adapter will implement this in a
/// later phase; nothing references the legacy stack from the new app yet. No WPF types here.
/// </summary>
public interface IInspectionWorkspace
{
    /// <summary>
    /// Loads the inspection series available for a project, newest first. Intentionally minimal
    /// for the foundation; richer operations (drawings, reviewed plans, notes, report) are added
    /// as each sub-area is migrated off the legacy window.
    /// </summary>
    Task<IReadOnlyList<InspectionSeriesSummary>> GetSeriesAsync(
        int projectId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Lightweight, UI-agnostic projection of an inspection series for the new screen's tree/header.
/// </summary>
/// <param name="SeriesId">The series identifier.</param>
/// <param name="Name">Display name of the series.</param>
public readonly record struct InspectionSeriesSummary(int SeriesId, string Name);
