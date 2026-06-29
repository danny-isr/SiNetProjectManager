namespace SiNet.LegacyBridge.Inspection;

/// <summary>
/// Bridge-local projection of a legacy inspection note row, carrying only the read-only fields the
/// new Inspection screen's notes area needs.
/// <para>
/// Mirrors <see cref="LegacyInspectionReportDto"/>: the legacy WPF host projects the EF
/// <c>InspectionNote</c> entity into this shape so <c>SiNet.LegacyBridge</c> never references
/// <c>SiNetSQL</c>. Read-only — no editing/creation/deletion/reordering semantics cross the boundary.
/// </para>
/// </summary>
/// <param name="NoteId">The note identifier.</param>
/// <param name="Number">The note sub-index/number (e.g. 1.1.1), if any.</param>
/// <param name="Text">The note text, if any.</param>
/// <param name="Status">The note status, if any.</param>
public sealed record LegacyInspectionNoteDto(
    long NoteId, string? Number, string? Text, string? Status);
