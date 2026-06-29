namespace SiNet.LegacyBridge.Inspection;

/// <summary>
/// Legacy-host seam over the legacy inspection report service. Mirrors only the single read the
/// new Inspection screen's tree/series area needs, expressed in terms of the bridge-local
/// <see cref="LegacyInspectionSeriesDto"/> so this assembly has no dependency on <c>SiNetSQL</c>.
/// <para>
/// The new app host (<c>SiNet.App.Wpf</c>) leaves this seam unbound, so the adapter degrades to an
/// empty series list. The legacy WPF host (<c>SiNetProjectManagerV2</c>) — which already references
/// both worlds — binds a concrete implementation that adapts <c>IInspectionReportService</c> and
/// projects <c>InspectionSeries</c> into <see cref="LegacyInspectionSeriesDto"/>. Remove this seam
/// once a native infrastructure inspection source replaces it.
/// </para>
/// </summary>
public interface ILegacyInspectionSource
{
    /// <summary>
    /// Returns the inspection series for <paramref name="projectId"/>, newest first.
    /// Implementations should return an empty list (not throw) when data is unavailable.
    /// </summary>
    Task<IReadOnlyList<LegacyInspectionSeriesDto>> GetSeriesForProjectAsync(
        int projectId,
        CancellationToken cancellationToken = default);
}
