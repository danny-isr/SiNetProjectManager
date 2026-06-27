namespace SiNetSQL.Models;

/// <summary>
/// Dynamic inspection finding. Tracks status per section and supports
/// recurring-issue tracking via the self-referencing <see cref="PreviousNoteId"/>.
/// </summary>
public class InspectionNote
{
    public long NoteId { get; set; }

    public int ReportId { get; set; }

    public int SectionId { get; set; }

    public string? NoteSubIndex { get; set; }

    public string? NoteText { get; set; }

    /// <summary>
    /// Legacy string-based status. Will be removed after data migration to <see cref="NoteStatusId"/>.
    /// </summary>
    public string? NoteStatus { get; set; }

    /// <summary>
    /// FK to the normalized <see cref="InspectionNoteStatus"/> lookup table.
    /// Nullable during migration — will become required after data migration.
    /// </summary>
    public int? NoteStatusId { get; set; }

    public string? AccMarkupLink { get; set; }

    public long? PreviousNoteId { get; set; }

    // ── Planner response (filled when the planner answers our comment) ──
    public string? PlannerResponseText { get; set; }
    public DateTime? PlannerResponseReceivedAt { get; set; }
    public DateTime? PlannerResponseImportedAt { get; set; }
    /// <summary>"InSameReport" or "ExternalDocument".</summary>
    public string? PlannerResponseSourceType { get; set; }
    public string? PlannerResponseSourceUrl { get; set; }
    public string? PlannerResponseAttachmentFileId { get; set; }
    public string? PlannerResponseAttachmentUrl { get; set; }

    // ── Pull metadata (set every time we pull a planner response from the source sheet) ──
    /// <summary>UTC time when the planner response was last pulled from the source sheet.</summary>
    public DateTime? PlannerResponsePulledAt { get; set; }
    /// <summary>Spreadsheet id the response was pulled from.</summary>
    public string? PlannerResponseSourceSpreadsheetId { get; set; }
    /// <summary>Spreadsheet URL the response was pulled from.</summary>
    public string? PlannerResponseSourceSpreadsheetUrl { get; set; }
    /// <summary>Sheet/tab name within the spreadsheet the response was pulled from.</summary>
    public string? PlannerResponseSourceSheetName { get; set; }
    /// <summary>1-based row number of the matched row in the source sheet.</summary>
    public int? PlannerResponseSourceRow { get; set; }
    /// <summary>A1 address of the response cell (e.g. "D112").</summary>
    public string? PlannerResponseSourceCell { get; set; }

    /// <summary>Inspector's response to the planner's answer (free text).</summary>
    public string? OurResponseToPlanner { get; set; }

    /// <summary>"Pending" / "Accepted" / "Rejected" / "Recurring".</summary>
    public string? ResponseReviewStatus { get; set; }

    // ── Optional ACC linkage ──
    public string? AccProjectId { get; set; }
    public string? AccFileUrn { get; set; }
    public string? AccFileVersionUrn { get; set; }
    public string? AccFileName { get; set; }
    public string? AccIssueId { get; set; }
    public string? AccIssueUrl { get; set; }
    public string? AccMarkupId { get; set; }
    public string? AccMarkupUrl { get; set; }
    /// <summary>"Issue" / "Markup" / "File".</summary>
    public string? AccLinkedItemType { get; set; }

    // ── Logical link to a specific reviewed file (from Work Window 2 / IActiveFileQueryService) ──
    // When all three are null, the note inherits the report's reviewed plan default.
    // When set, the note is linked to a specific file (file/alternative/version).
    // We DO store version here because a note may reference an auxiliary file
    // (תב"ע / נספח / קובץ עזר) that does not advance with the report's main version.

    public string? LinkedFileName { get; set; }
    public string? LinkedAlternative { get; set; }
    public string? LinkedVersion { get; set; }

    // Navigation
    public virtual InspectionReport InspectionReport { get; set; } = null!;

    public virtual Section Section { get; set; } = null!;

    public virtual InspectionNoteStatus? NoteStatusLookup { get; set; }

    public virtual InspectionNote? PreviousNote { get; set; }

    public virtual ICollection<InspectionNote> FollowUpNotes { get; set; } = new List<InspectionNote>();

    public virtual ICollection<InspectionNoteAttachment> Attachments { get; set; } = new List<InspectionNoteAttachment>();
}
