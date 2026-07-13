using System.Collections.ObjectModel;
using SiNet.App.Wpf.Inspection;
using SiNet.Application.Inspection;

namespace SiNet.App.Wpf.Surfaces.Inspection;

/// <summary>
/// Lightweight row types used by <see cref="InspectionWindowViewModel"/> for the
/// visual clone of the legacy <c>FloatingInspectionView</c>.
/// </summary>
internal static class InspectionWindowDesignData
{
    public static IReadOnlyList<InspectionTemplateRow> SampleTemplates { get; } =
    [
        new("תבנית ביקורת סטנדרטית"),
        new("תבנית ביקורת קונסטרוקציה"),
        new("תבנית ביקורת אינסטלציה"),
    ];

    public static IReadOnlyList<InspectionReportRow> SampleReports { get; } =
    [
        new(101, 101, "דני ישראל", new DateTime(2026, 6, 18)),
        new(102, 102, "דני ישראל", new DateTime(2026, 6, 20)),
        new(103, 103, "רות כהן", new DateTime(2026, 6, 21)),
    ];

    public static IReadOnlyList<InspectionNoteRow> SampleNotes { get; } =
    [
        new("1.1.1", "יש להשלים פרטי חיבור במפלס הקרקע", "Failed"),
        new("1.1.2", "חוסר סימון מידות בתוכנית", "Failed"),
        new("2.1.1", "הערה לדוגמה — תגובת מתכנן נתקבלה", "Passed"),
    ];

    public static IReadOnlyList<InspectionStatusOption> DefaultStatusOptions { get; } =
    [
        new(InspectionQuestionnaireRules.Failed, "הערה"),
        new("Passed", "מקובל"),
        new("RecurringFailed", "הערה חוזרת"),
        new(InspectionQuestionnaireRules.NotApplicable, "לא רלוונטי"),
        new(InspectionQuestionnaireRules.ManagerReview, "הערה לבדיקת המנהל"),
    ];

    /// <summary>Fake tree: General chapter + numbered chapters (design-time only).</summary>
    public static IReadOnlyList<object> BuildSampleTree()
    {
        var general = new InspectionGeneralChapterItem();
        general.Fields.Add(new InspectionGeneralFieldItem
        {
            NoteId = 9001,
            SectionId = 90,
            Label = "שם פרויקט",
            IsAutomatic = true,
            AutoValue = "פרויקט לדוגמה",
            Value = "פרויקט לדוגמה",
            IsManualOverride = false,
        });
        general.Fields.Add(new InspectionGeneralFieldItem
        {
            NoteId = 9002,
            SectionId = 91,
            Label = "הערות כלליות",
            IsAutomatic = false,
            Value = "",
            IsManualOverride = false,
        });
        general.Fields[0].ClearDirty();
        general.Fields[1].ClearDirty();

        var chapters = new List<object> { general };

        var chapter1 = new InspectionChapterItem("1", "מידע כללי");
        var section11 = new InspectionSectionItem(11, "1.1", "פרטי הפרויקט והמגרש");
        section11.Notes.Add(CreateSampleNote("1.1.1", "יש להשלים פרטי חיבור במפלס הקרקע", "Failed", true, false, 1, 11));
        section11.Notes.Add(CreateSampleNote("1.1.2", "חוסר סימון מידות בתוכנית", "Failed", false, true, 2, 11));
        chapter1.Sections.Add(section11);

        var section12 = new InspectionSectionItem(12, "1.2", "גבולות וקווי בניין");
        section12.Notes.Add(CreateSampleNote("1.2.1", "קו בניין קידמי אינו תואם תבע", "RecurringFailed", true, true, 3, 12));
        chapter1.Sections.Add(section12);
        chapters.Add(chapter1);

        var chapter2 = new InspectionChapterItem("2", "קונסטרוקציה");
        var section21 = new InspectionSectionItem(21, "2.1", "יסודות ועמודים");
        section21.Notes.Add(CreateSampleNote("2.1.1", "הערה לדוגמה — תגובת מתכנן נתקבלה", "Passed", false, true, 4, 21));
        section21.Notes.Add(CreateSampleNote("2.1.2", "חתך עמוד P3 טעון הבהרה", "Failed", false, false, 5, 21));
        chapter2.Sections.Add(section21);
        chapters.Add(chapter2);

        return chapters;
    }

    private static InspectionNoteItem CreateSampleNote(
        string number,
        string text,
        string status,
        bool hasLinkedFile,
        bool hasPlannerResponse,
        long noteId,
        int sectionId)
    {
        var note = new InspectionNoteItem
        {
            NoteNumber = number,
            StatusText = status,
            HasLinkedFile = hasLinkedFile,
            HasPlannerResponse = hasPlannerResponse,
            NoteId = noteId,
            SectionId = sectionId,
        };
        note.SetNoteTextWithoutStatusSync(text);
        note.ClearDirty();
        return note;
    }
}

/// <summary>Read-only template row for the template picker.</summary>
public sealed record InspectionTemplateRow(string Name, string? SpreadsheetId = null, string? Url = null);

/// <summary>Read-only report card row for the report list.</summary>
public sealed record InspectionReportRow(int ReportId, int ReportNumber, string InspectorName, DateTime InspectionDate);

/// <summary>Read-only note row for the legacy flat notes area (fake/design-time data).</summary>
public sealed record InspectionNoteRow(string DisplayLabel, string NoteText, string NoteStatus);

/// <summary>Status ComboBox option (DbKey = persisted NoteStatus key).</summary>
public sealed record InspectionStatusOption(string DbKey, string Label, int? StatusId = null);

/// <summary>Questionnaire chapter (tree level 1).</summary>
public sealed class InspectionChapterItem
{
    public InspectionChapterItem(string chapterNumber, string chapterTitle)
    {
        ChapterNumber = chapterNumber;
        ChapterTitle = chapterTitle;
    }

    public string ChapterNumber { get; }
    public string ChapterTitle { get; }
    public ObservableCollection<InspectionSectionItem> Sections { get; } = [];
    public string DisplayTitle => $"{ChapterNumber} — {ChapterTitle}";
}

/// <summary>Questionnaire section (tree level 2).</summary>
public sealed class InspectionSectionItem
{
    public InspectionSectionItem(int sectionId, string sectionNumber, string sectionTitle)
    {
        SectionId = sectionId;
        SectionNumber = sectionNumber;
        SectionTitle = sectionTitle;
    }

    public int SectionId { get; }
    public string SectionNumber { get; }
    public string SectionTitle { get; }
    public ObservableCollection<InspectionNoteItem> Notes { get; } = [];
    public string SectionHeaderText => $"{SectionNumber}  {SectionTitle}";
}

/// <summary>Numbered note / finding (tree level 3) — editable inline.</summary>
public sealed class InspectionNoteItem : ObservableObject
{
    private string _noteText = string.Empty;
    private string _statusText = string.Empty;
    private bool _isDirty;
    private bool _suppressStatusSync;

    public long? NoteId { get; init; }
    public int SectionId { get; init; }
    public int? StatusId { get; set; }
    public string NoteNumber { get; init; } = string.Empty;
    public bool HasLinkedFile { get; init; }
    public bool HasPlannerResponse { get; init; }

    public string DisplayLabel => NoteNumber;

    public string NoteText
    {
        get => _noteText;
        set
        {
            if (_noteText == value)
                return;

            _noteText = value;
            _isDirty = true;
            OnPropertyChanged(nameof(NoteText));
            OnPropertyChanged(nameof(HasValidationError));
            OnPropertyChanged(nameof(IsDirty));

            if (!_suppressStatusSync)
            {
                _suppressStatusSync = true;
                try
                {
                    var synced = InspectionQuestionnaireRules.SyncStatusAfterTextChange(_statusText, value);
                    if (synced != _statusText)
                        StatusText = synced ?? string.Empty;
                }
                finally
                {
                    _suppressStatusSync = false;
                }
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText == value)
                return;

            _statusText = value ?? string.Empty;
            _isDirty = true;
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(HasValidationError));
            OnPropertyChanged(nameof(IsDirty));
        }
    }

    public bool HasValidationError =>
        InspectionQuestionnaireRules.HasValidationError(_statusText, _noteText);

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (_isDirty == value)
                return;
            _isDirty = value;
            OnPropertyChanged(nameof(IsDirty));
        }
    }

    public void ClearDirty() => IsDirty = false;

    public void SetNoteTextWithoutStatusSync(string text)
    {
        _suppressStatusSync = true;
        try
        {
            NoteText = text;
        }
        finally
        {
            _suppressStatusSync = false;
        }
    }
}

/// <summary>Top-level General (Chapter 0) branch.</summary>
public sealed class InspectionGeneralChapterItem
{
    public string DisplayTitle => "נתונים כלליים";
    public ObservableCollection<InspectionGeneralFieldItem> Fields { get; } = [];
}

/// <summary>General field: label + TextBox + auto/manual toggle (no status combo).</summary>
public sealed class InspectionGeneralFieldItem : ObservableObject
{
    private string? _value;
    private bool _isManualOverride;
    private bool _isDirty;

    public long NoteId { get; init; }
    public int SectionId { get; init; }
    public string Label { get; init; } = string.Empty;
    public bool IsAutomatic { get; init; }
    public string? AutoValue { get; init; }

    public bool IsManualOverride
    {
        get => _isManualOverride;
        set
        {
            if (_isManualOverride == value)
                return;

            _isManualOverride = value;
            _isDirty = true;
            OnPropertyChanged(nameof(IsManualOverride));
            OnPropertyChanged(nameof(IsEditable));
            OnPropertyChanged(nameof(IsDirty));

            if (!value && IsAutomatic)
                Value = AutoValue;
        }
    }

    public bool IsEditable => !IsAutomatic || IsManualOverride;

    public string? Value
    {
        get => _value;
        set
        {
            if (_value == value)
                return;

            _value = value;
            _isDirty = true;
            OnPropertyChanged(nameof(Value));
            OnPropertyChanged(nameof(IsDirty));
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (_isDirty == value)
                return;
            _isDirty = value;
            OnPropertyChanged(nameof(IsDirty));
        }
    }

    public void ClearDirty() => IsDirty = false;
}
