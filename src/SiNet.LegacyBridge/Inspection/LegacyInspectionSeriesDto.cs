namespace SiNet.LegacyBridge.Inspection;

/// <summary>
/// Bridge-local projection of a legacy inspection series, carrying only the fields the new
/// Inspection screen's tree/series list needs.
/// <para>
/// This DTO exists so <c>SiNet.LegacyBridge</c> never references the legacy <c>SiNetSQL</c>
/// assembly directly: the legacy WPF host (which already references both worlds) projects the
/// EF <c>InspectionSeries</c> entity into this shape when it implements
/// <see cref="ILegacyInspectionSource"/>. Retained only while the Inspection screen is migrated.
/// </para>
/// </summary>
/// <param name="SeriesId">The series identifier.</param>
/// <param name="DisplayName">A ready-to-show series name (already falls back to a synthesized label).</param>
public sealed record LegacyInspectionSeriesDto(int SeriesId, string DisplayName);
