namespace SiNetSQL.Models;

/// <summary>
/// Lookup table for inspection note statuses (Passed, Failed, RecurringFailed, NotApplicable).
/// Replaces the hard-coded string constants in <see cref="InspectionNote.NoteStatus"/>.
/// Each status has a display label, sort order, and export symbol for Google Sheets output.
/// </summary>
public class InspectionNoteStatus
{
    public int StatusId { get; set; }

    /// <summary>
    /// Machine-readable key (e.g. "Passed", "Failed").
    /// Used in code comparisons — never shown to the user.
    /// </summary>
    public string StatusKey { get; set; } = string.Empty;

    /// <summary>
    /// Hebrew display label for the UI (e.g. "מקובל", "הערה").
    /// </summary>
    public string HebrewLabel { get; set; } = string.Empty;

    /// <summary>
    /// Controls the display order in ComboBoxes and reports.
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Symbol written into the Google Sheet during export (e.g. "V", "X", "!").
    /// </summary>
    public string? ExportSymbol { get; set; }

    /// <summary>
    /// Soft-delete flag. Inactive statuses are hidden from new selections
    /// but preserved for historical data integrity.
    /// </summary>
    public bool IsActive { get; set; } = true;

    // Navigation
    public virtual ICollection<InspectionNote> InspectionNotes { get; set; } = new List<InspectionNote>();
}
