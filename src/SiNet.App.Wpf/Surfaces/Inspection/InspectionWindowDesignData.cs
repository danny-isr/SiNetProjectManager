namespace SiNet.App.Wpf.Surfaces.Inspection;

/// <summary>
/// Lightweight, read-only row types used by <see cref="InspectionWindowViewModel"/> to populate the
/// visual clone of the legacy <c>FloatingInspectionView</c> with fake/design-time data ONLY.
/// <para>
/// These are deliberately simple presentation records — they are NOT domain entities, NOT EF models,
/// and carry no behavior. The visual-clone slice uses them so the window can render the same panels
/// (templates, report cards, notes) without touching the real database, report services, Gmail, or
/// ACC. Real data will replace them later via clean read-only Application ports.
/// </para>
/// </summary>
internal static class InspectionWindowDesignData
{
    /// <summary>A few fake inspection-report templates for the create-report strip.</summary>
    public static IReadOnlyList<InspectionTemplateRow> SampleTemplates { get; } =
    [
        new("\u05EA\u05D1\u05E0\u05D9\u05EA \u05D1\u05D9\u05E7\u05D5\u05E8\u05EA \u05E1\u05D8\u05E0\u05D3\u05E8\u05D8\u05D9\u05EA"),
        new("\u05EA\u05D1\u05E0\u05D9\u05EA \u05D1\u05D9\u05E7\u05D5\u05E8\u05EA \u05E7\u05D5\u05E0\u05E1\u05D8\u05E8\u05D5\u05E7\u05E6\u05D9\u05D4"),
        new("\u05EA\u05D1\u05E0\u05D9\u05EA \u05D1\u05D9\u05E7\u05D5\u05E8\u05EA \u05D0\u05D9\u05E0\u05E1\u05D8\u05DC\u05E6\u05D9\u05D4"),
    ];

    /// <summary>A small set of fake report cards for the bottom list.</summary>
    public static IReadOnlyList<InspectionReportRow> SampleReports { get; } =
    [
        new(101, "\u05D3\u05E0\u05D9 \u05D9\u05E9\u05E8\u05D0\u05DC", new DateTime(2026, 6, 18)),
        new(102, "\u05D3\u05E0\u05D9 \u05D9\u05E9\u05E8\u05D0\u05DC", new DateTime(2026, 6, 20)),
        new(103, "\u05E8\u05D5\u05EA \u05DB\u05D4\u05DF", new DateTime(2026, 6, 21)),
    ];

    /// <summary>Several fake notes for the selected-report notes area (legacy flat list, kept for compatibility).</summary>
    public static IReadOnlyList<InspectionNoteRow> SampleNotes { get; } =
    [
        new("1.1", "\u05D9\u05E9 \u05DC\u05D4\u05E9\u05DC\u05D9\u05DD \u05E4\u05E8\u05D8\u05D9 \u05D7\u05D9\u05D1\u05D5\u05E8 \u05D1\u05DE\u05E4\u05DC\u05E1 \u05D4\u05E7\u05E8\u05E7\u05E2", "\u05E4\u05EA\u05D5\u05D7\u05D4"),
        new("1.2", "\u05D7\u05D5\u05E1\u05E8 \u05E1\u05D9\u05DE\u05D5\u05DF \u05DE\u05D9\u05D3\u05D5\u05EA \u05D1\u05EA\u05D5\u05DB\u05E0\u05D9\u05EA", "\u05E4\u05EA\u05D5\u05D7\u05D4"),
        new("2.1", "\u05D4\u05E2\u05E8\u05D4 \u05DC\u05D3\u05D5\u05D2\u05DE\u05D4 \u2014 \u05EA\u05D2\u05D5\u05D1\u05EA \u05DE\u05EA\u05DB\u05E0\u05DF \u05E0\u05EA\u05E7\u05D1\u05DC\u05D4", "\u05D8\u05D5\u05E4\u05DC"),
    ];

    /// <summary>
    /// Fake hierarchical questionnaire tree (Chapter -> Section -> Note) mirroring the visual shape of
    /// the legacy <c>InspectionTree</c> (<c>ChapterTreeItem</c> -> <c>SectionTreeItem</c> -> <c>NoteTreeItem</c>).
    /// Design/visual data only; no EF entities, no DB mapping.
    /// </summary>
    public static IReadOnlyList<InspectionChapterItem> BuildSampleTree() =>
    [
        new InspectionChapterItem(
            "1",
            "\u05DE\u05D9\u05D3\u05E2 \u05DB\u05DC\u05DC\u05D9", // "General data"
            [
                new InspectionSectionItem(
                    "1.1",
                    "\u05E4\u05E8\u05D8\u05D9 \u05D4\u05E4\u05E8\u05D5\u05D9\u05E7\u05D8 \u05D5\u05D4\u05DE\u05D2\u05E8\u05E9", // "Project & lot details"
                    [
                        new InspectionNoteItem("1.1.1", "\u05D9\u05E9 \u05DC\u05D4\u05E9\u05DC\u05D9\u05DD \u05E4\u05E8\u05D8\u05D9 \u05D7\u05D9\u05D1\u05D5\u05E8 \u05D1\u05DE\u05E4\u05DC\u05E1 \u05D4\u05E7\u05E8\u05E7\u05E2", "\u05E4\u05EA\u05D5\u05D7\u05D4", HasLinkedFile: true, HasPlannerResponse: false),
                        new InspectionNoteItem("1.1.2", "\u05D7\u05D5\u05E1\u05E8 \u05E1\u05D9\u05DE\u05D5\u05DF \u05DE\u05D9\u05D3\u05D5\u05EA \u05D1\u05EA\u05D5\u05DB\u05E0\u05D9\u05EA", "\u05E4\u05EA\u05D5\u05D7\u05D4", HasLinkedFile: false, HasPlannerResponse: true),
                    ]),
                new InspectionSectionItem(
                    "1.2",
                    "\u05D2\u05D1\u05D5\u05DC\u05D5\u05EA \u05D5\u05E7\u05D5\u05D5\u05D9 \u05D1\u05E0\u05D9\u05DF", // "Borders & building lines"
                    [
                        new InspectionNoteItem("1.2.1", "\u05E7\u05D5 \u05D1\u05E0\u05D9\u05DF \u05E7\u05D9\u05D3\u05DE\u05D9 \u05D0\u05D9\u05E0\u05D5 \u05EA\u05D5\u05D0\u05DD \u05EA\u05D1\u05E2", "\u05D3\u05D5\u05E8\u05E9 \u05EA\u05D9\u05E7\u05D5\u05DF", HasLinkedFile: true, HasPlannerResponse: true),
                    ]),
            ]),
        new InspectionChapterItem(
            "2",
            "\u05E7\u05D5\u05E0\u05E1\u05D8\u05E8\u05D5\u05E7\u05E6\u05D9\u05D4", // "Structure"
            [
                new InspectionSectionItem(
                    "2.1",
                    "\u05D9\u05E1\u05D5\u05D3\u05D5\u05EA \u05D5\u05E2\u05DE\u05D5\u05D3\u05D9\u05DD", // "Foundations & columns"
                    [
                        new InspectionNoteItem("2.1.1", "\u05D4\u05E2\u05E8\u05D4 \u05DC\u05D3\u05D5\u05D2\u05DE\u05D4 \u2014 \u05EA\u05D2\u05D5\u05D1\u05EA \u05DE\u05EA\u05DB\u05E0\u05DF \u05E0\u05EA\u05E7\u05D1\u05DC\u05D4", "\u05D8\u05D5\u05E4\u05DC", HasLinkedFile: false, HasPlannerResponse: true),
                        new InspectionNoteItem("2.1.2", "\u05D7\u05EA\u05DA \u05E2\u05DE\u05D5\u05D3 P3 \u05D8\u05E2\u05D5\u05DF \u05D4\u05D1\u05D4\u05E8\u05D4", "\u05E4\u05EA\u05D5\u05D7\u05D4", HasLinkedFile: false, HasPlannerResponse: false),
                    ]),
            ]),
    ];
}

/// <summary>Read-only template row for the template picker (fake/design-time data).</summary>
public sealed record InspectionTemplateRow(string Name);

/// <summary>Read-only report card row for the report list (fake/design-time data).</summary>
public sealed record InspectionReportRow(int ReportNumber, string InspectorName, DateTime InspectionDate);

/// <summary>Read-only note row for the legacy flat notes area (fake/design-time data).</summary>
public sealed record InspectionNoteRow(string DisplayLabel, string NoteText, string NoteStatus);

/// <summary>
/// Fake/design-time questionnaire <b>chapter</b> (tree level 1). Mirrors the legacy
/// <c>ChapterTreeItem</c> visual shape (<c>DisplayTitle</c> + child <c>Sections</c>). Not an EF entity.
/// </summary>
public sealed record InspectionChapterItem(
    string ChapterNumber,
    string ChapterTitle,
    IReadOnlyList<InspectionSectionItem> Sections)
{
    /// <summary>Header shown on the chapter row, e.g. "1 \u2014 \u05DE\u05D9\u05D3\u05E2 \u05DB\u05DC\u05DC\u05D9".</summary>
    public string DisplayTitle => $"{ChapterNumber} \u2014 {ChapterTitle}";
}

/// <summary>
/// Fake/design-time questionnaire <b>section</b> (tree level 2). Mirrors the legacy
/// <c>SectionTreeItem</c> visual shape (<c>SectionHeaderText</c> + child <c>Notes</c>). Not an EF entity.
/// </summary>
public sealed record InspectionSectionItem(
    string SectionNumber,
    string SectionTitle,
    IReadOnlyList<InspectionNoteItem> Notes)
{
    /// <summary>Header shown on the section row, e.g. "1.1 \u05E4\u05E8\u05D8\u05D9 \u05D4\u05E4\u05E8\u05D5\u05D9\u05E7\u05D8".</summary>
    public string SectionHeaderText => $"{SectionNumber}  {SectionTitle}";
}

/// <summary>
/// Fake/design-time questionnaire <b>note/finding</b> (tree level 3). Mirrors the legacy
/// <c>NoteTreeItem</c> visual shape (index label, status, text, linked-file and planner-response
/// indicators). Not an EF entity; carries no behavior.
/// </summary>
public sealed record InspectionNoteItem(
    string NoteNumber,
    string NoteText,
    string StatusText,
    bool HasLinkedFile,
    bool HasPlannerResponse)
{
    /// <summary>Index label shown on the note row (matches legacy <c>DisplayLabel</c>).</summary>
    public string DisplayLabel => NoteNumber;
}
