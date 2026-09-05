namespace SiNetSQL.Services.InspectionSync;

/// <summary>
/// Represents a single tag discovered during template scanning.
/// Used for sync-on-load comparison between the Google Sheet template and the local database.
/// </summary>
public sealed class TemplateScanTag
{
    /// <summary>
    /// Numeric section code extracted from the tag (e.g. "2.4").
    /// Empty for general (non-numbered) tags.
    /// </summary>
    public required string SectionCode { get; init; }

    /// <summary>Title/description text captured from the tag (text between code and brackets/closing).</summary>
    public string? Title { get; init; }

    /// <summary>Default text extracted from inside the square brackets (e.g. "שילוט כניסה").</summary>
    public string? DefaultText { get; init; }

    /// <summary>
    /// <c>true</c> when the tag contains square brackets (<c>&lt;&lt;X.Y Title [...]&gt;&gt;</c>) — header/definition tag.
    /// <c>false</c> for note input tags and general tags.
    /// </summary>
    public bool IsStatusTag { get; init; }

    /// <summary>
    /// <c>true</c> when the tag is a note-input marker (<c>&lt;&lt;X.Y $&gt;&gt;</c>).
    /// The <c>$</c> sign indicates the cell where user notes are injected during export.
    /// </summary>
    public bool IsNoteInputTag { get; init; }

    /// <summary>
    /// <c>true</c> when the tag has no numeric section code prefix — a general data field
    /// (e.g. <c>&lt;&lt;שם פרויקט&gt;&gt;</c>).
    /// </summary>
    public bool IsGeneralTag { get; init; }

    /// <summary>
    /// Display label for general (non-numbered) tags (e.g. "שם פרויקט").
    /// <c>null</c> for numbered section tags.
    /// </summary>
    public string? GeneralTagLabel { get; init; }

    /// <summary>
    /// <c>true</c> when this is the dedicated mandatory planner-response column tag
    /// (e.g. <c>&lt;&lt;תגובת המתכנן&gt;&gt;</c>). The cell containing this tag determines
    /// the column used for writing/reading the planner response — no longer derived
    /// from a fixed offset relative to the note column.
    /// </summary>
    public bool IsPlannerResponseColumnTag { get; init; }

    /// <summary>Zero-based row index in the sheet.</summary>
    public int Row { get; init; }

    /// <summary>Zero-based column index in the sheet.</summary>
    public int Col { get; init; }
}
