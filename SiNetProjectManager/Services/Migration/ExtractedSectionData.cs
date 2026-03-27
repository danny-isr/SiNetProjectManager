namespace SiNetProjectManager.Services.Migration;

/// <summary>
/// DTO for a single section extracted from a FINAL (filled) inspection report.
/// Contains the section code, the determined status (from background color), and the note text.
/// </summary>
public sealed class ExtractedSectionData
{
    /// <summary>Section code from the template (e.g. "1.1", "2.3").</summary>
    public required string SectionCode { get; init; }

    /// <summary>Chapter title from the template tag (e.g. "כללי", "תנועה").</summary>
    public string ChapterTitle { get; init; } = string.Empty;

    /// <summary>Section title from the template tag bracket content (e.g. "תרשים סביבה, חץ צפון").</summary>
    public string SectionTitle { get; init; } = string.Empty;

    /// <summary>Hebrew status label text read from the final report cell (e.g. "מקובל", "הערה").</summary>
    public string StatusText { get; init; } = string.Empty;

    /// <summary>Machine-readable status key derived from background color (Passed/Failed/RecurringFailed/NotApplicable).</summary>
    public string StatusKey { get; init; } = string.Empty;

    /// <summary>Background color of the status cell as #RRGGBB hex.</summary>
    public string StatusColorHex { get; init; } = string.Empty;

    /// <summary>Note/comment text extracted from the note cell in the final report.</summary>
    public string NoteText { get; init; } = string.Empty;

    /// <summary>Designer response text (תגובת המתכנן) from the column after the status/note column.</summary>
    public string DesignerResponse { get; init; } = string.Empty;

    /// <summary>NoteSubIndex value found in the column adjacent to the note (e.g. "1.1.1").</summary>
    public string NoteSubIndex { get; init; } = string.Empty;

    /// <summary>Zero-based row index in the final report where this section was found.</summary>
    public int ReportRow { get; init; }

    /// <summary>How the status was determined.</summary>
    public string DetectionMethod { get; init; } = string.Empty;

    // ── Smart Extraction Fields ──

    /// <summary>Original cell reference in the final report (e.g. "C15").</summary>
    public string OriginalCellRef { get; init; } = string.Empty;

    /// <summary>True if the note text was split from a single merged cell into multiple sub-notes.</summary>
    public bool WasSplit { get; init; }

    /// <summary>1-based index of this segment within a split (0 if not split).</summary>
    public int SplitIndex { get; init; }

    /// <summary>Original full text of the merged cell before splitting.</summary>
    public string SplitSourceText { get; init; } = string.Empty;

    /// <summary>Closure/resolution date extracted from the note text (e.g. from "בוצע 25/12/2024").</summary>
    public DateTime? ClosedDate { get; init; }

    /// <summary>True if the cell had a gray background — indicating the item was resolved/closed in historical reports.</summary>
    public bool IsResolved { get; init; }

    /// <summary>Section number or title found in the header column at this row (header-first validation).</summary>
    public string HeaderValidation { get; init; } = string.Empty;

    // ── Template Tag Traceability ──

    /// <summary>Reconstructed status tag from the template (e.g. "&lt;&lt;Status_3.6 חניה [גישה לחניות]&gt;&gt;").</summary>
    public string TemplateStatusTag { get; init; } = string.Empty;

    /// <summary>Reconstructed note tag from the template (e.g. "&lt;&lt;3.6 $&gt;&gt;").</summary>
    public string TemplateNoteTag { get; init; } = string.Empty;
}

/// <summary>
/// Full result of extracting content from a single FINAL report sheet.
/// </summary>
public sealed class ReportExtractionResult
{
    /// <summary>The template spreadsheet ID used for mapping.</summary>
    public string TemplateSpreadsheetId { get; init; } = string.Empty;

    /// <summary>The final report spreadsheet ID that was read.</summary>
    public string ReportSpreadsheetId { get; init; } = string.Empty;

    /// <summary>All sections successfully extracted.</summary>
    public List<ExtractedSectionData> Sections { get; init; } = [];

    /// <summary>General field values extracted (tag label → value).</summary>
    public Dictionary<string, string> GeneralFields { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Warnings or diagnostic messages generated during extraction.</summary>
    public List<string> Warnings { get; init; } = [];

    /// <summary>True if extraction completed without critical failures.</summary>
    public bool IsSuccess { get; init; }

    /// <summary>Error message if extraction failed completely.</summary>
    public string? ErrorMessage { get; init; }
}
