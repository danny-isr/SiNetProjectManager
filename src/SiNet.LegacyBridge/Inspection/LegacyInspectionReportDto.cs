namespace SiNet.LegacyBridge.Inspection;

/// <summary>
/// Bridge-local projection of a legacy inspection report row, carrying only the read-only fields the
/// new Inspection screen's series detail needs.
/// <para>
/// Mirrors <see cref="LegacyInspectionSeriesDto"/>: the legacy WPF host projects the EF
/// <c>InspectionReport</c> entity into this shape so <c>SiNet.LegacyBridge</c> never references
/// <c>SiNetSQL</c>. Read-only — no editing/generation/sent-locked semantics cross the boundary.
/// </para>
/// </summary>
/// <param name="ReportId">The report identifier.</param>
/// <param name="ReportNumber">The sequential report (round) number.</param>
/// <param name="InspectionDate">The inspection date.</param>
/// <param name="InspectorName">The inspector display name, if any.</param>
public sealed record LegacyInspectionReportDto(
    int ReportId, int ReportNumber, DateTime InspectionDate, string? InspectorName);
