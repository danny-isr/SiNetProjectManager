using System.Collections.ObjectModel;
using SiNet.App.Wpf.Inspection;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Inspection;

namespace SiNet.App.Wpf.Surfaces.Inspection;

/// <summary>Create-report strip (template / series) — presentation state for InspectionWindow.</summary>
public sealed class InspectionCreateReportStripViewModel : ObservableObject
{
    private InspectionTemplateRow? _selectedTemplate;

    public ObservableCollection<InspectionTemplateRow> AvailableTemplates { get; } = [];

    public InspectionTemplateRow? SelectedTemplate
    {
        get => _selectedTemplate;
        set => SetField(ref _selectedTemplate, value);
    }
}

/// <summary>Questionnaire tree + selected note.</summary>
public sealed class InspectionQuestionnaireViewModel : ObservableObject
{
    private InspectionNoteItem? _selectedNote;

    /// <summary>Mixed roots: optional <see cref="InspectionGeneralChapterItem"/> then numbered chapters.</summary>
    public ObservableCollection<object> RootItems { get; } = [];

    public InspectionNoteItem? SelectedNote
    {
        get => _selectedNote;
        set => SetField(ref _selectedNote, value);
    }

    public void ReplaceTree(
        InspectionGeneralChapterItem? general,
        IEnumerable<InspectionChapterItem> chapters)
    {
        RootItems.Clear();
        if (general is not null && general.Fields.Count > 0)
            RootItems.Add(general);

        foreach (var chapter in chapters)
            RootItems.Add(chapter);

        SelectedNote = null;
    }

    public void ReplaceTree(IEnumerable<object> roots)
    {
        RootItems.Clear();
        foreach (var root in roots)
            RootItems.Add(root);
        SelectedNote = null;
    }

    public IEnumerable<InspectionNoteItem> EnumerateNotes()
    {
        foreach (var root in RootItems)
        {
            if (root is not InspectionChapterItem chapter)
                continue;

            foreach (var section in chapter.Sections)
            {
                foreach (var note in section.Notes)
                    yield return note;
            }
        }
    }

    public bool CanExport =>
        InspectionQuestionnaireRules.CanExportNotes(
            EnumerateNotes().Select(n => (n.StatusText, (string?)n.NoteText)));

    public static InspectionGeneralChapterItem? MapGeneralFields(
        IReadOnlyList<InspectionGeneralFieldRow> fields,
        IReadOnlyDictionary<string, string> autoValues)
    {
        if (fields.Count == 0)
            return null;

        var chapter = new InspectionGeneralChapterItem();
        foreach (var row in fields)
        {
            var isAuto = InspectionQuestionnaireRules.AutoFieldLabels.Contains(row.Label);
            autoValues.TryGetValue(row.Label, out var autoValue);
            var field = new InspectionGeneralFieldItem
            {
                NoteId = row.NoteId,
                SectionId = row.SectionId,
                Label = row.Label,
                IsAutomatic = isAuto,
                AutoValue = autoValue,
                IsManualOverride = row.IsManualOverride,
                Value = isAuto && !row.IsManualOverride ? autoValue : row.Text,
            };
            field.ClearDirty();
            chapter.Fields.Add(field);
        }

        return chapter;
    }

    public static IReadOnlyList<InspectionChapterItem> MapFromWorkspace(
        IReadOnlyList<InspectionChapterNode> nodes) =>
        nodes.Select(ch =>
            {
                var chapter = new InspectionChapterItem(ch.ChapterNumber.ToString(), ch.Title);
                foreach (var sec in ch.Sections)
                {
                    var section = new InspectionSectionItem(
                        sec.SectionId,
                        $"{ch.ChapterNumber}.{sec.SectionCode}",
                        sec.Title);
                    foreach (var n in sec.Notes)
                    {
                        var note = new InspectionNoteItem
                        {
                            NoteNumber = n.Number ?? n.NoteId.ToString(),
                            StatusText = n.Status ?? string.Empty,
                            StatusId = n.StatusId,
                            HasLinkedFile = !string.IsNullOrWhiteSpace(n.LinkedFileName),
                            HasPlannerResponse = !string.IsNullOrWhiteSpace(n.PlannerResponseText),
                            NoteId = n.NoteId,
                            SectionId = sec.SectionId,
                        };
                        note.SetNoteTextWithoutStatusSync(n.Text ?? string.Empty);
                        note.ClearDirty();
                        section.Notes.Add(note);
                    }

                    chapter.Sections.Add(section);
                }

                return chapter;
            })
            .ToList();
}

/// <summary>Note editor state including optional AI suggestions (kept for AI review on selected note).</summary>
public sealed class InspectionNoteEditorViewModel : ObservableObject
{
    private string _noteText = string.Empty;
    private string? _grammarSuggestion;
    private string? _rephraseSuggestion;
    private bool _isAiBusy;
    private long? _noteId;

    public long? NoteId
    {
        get => _noteId;
        set => SetField(ref _noteId, value);
    }

    public string NoteText
    {
        get => _noteText;
        set => SetField(ref _noteText, value);
    }

    public string? GrammarSuggestion
    {
        get => _grammarSuggestion;
        set => SetField(ref _grammarSuggestion, value);
    }

    public string? RephraseSuggestion
    {
        get => _rephraseSuggestion;
        set => SetField(ref _rephraseSuggestion, value);
    }

    public bool IsAiBusy
    {
        get => _isAiBusy;
        set => SetField(ref _isAiBusy, value);
    }

    public void ClearAi()
    {
        GrammarSuggestion = null;
        RephraseSuggestion = null;
    }

    public void ApplyNote(InspectionNoteItem? note)
    {
        NoteId = note?.NoteId;
        NoteText = note?.NoteText ?? string.Empty;
        ClearAi();
    }
}

/// <summary>Drawings list for the selected report.</summary>
public sealed class InspectionDrawingsPanelViewModel : ObservableObject
{
    public ObservableCollection<InspectionDrawingRow> Drawings { get; } = [];

    public void Replace(IEnumerable<InspectionDrawingRow> rows)
    {
        Drawings.Clear();
        foreach (var row in rows)
            Drawings.Add(row);
    }
}

/// <summary>Report cards + export-related status.</summary>
public sealed class InspectionReportCardsViewModel : ObservableObject
{
    private InspectionReportRow? _selectedReport;

    public ObservableCollection<InspectionReportRow> Reports { get; } = [];

    public InspectionReportRow? SelectedReport
    {
        get => _selectedReport;
        set => SetField(ref _selectedReport, value);
    }

    public void Replace(IEnumerable<InspectionReportRow> rows, int? selectReportId = null)
    {
        Reports.Clear();
        foreach (var row in rows)
            Reports.Add(row);

        SelectedReport = selectReportId is int id
            ? Reports.FirstOrDefault(r => r.ReportId == id)
            : Reports.FirstOrDefault();
    }
}

/// <summary>Metadata / reviewed plan strip.</summary>
public sealed class InspectionMetadataViewModel : ObservableObject
{
    private string? _reviewedVersion;
    private bool _isLocked;
    private string? _inspectorName;

    public string? ReviewedVersion
    {
        get => _reviewedVersion;
        set => SetField(ref _reviewedVersion, value);
    }

    public bool IsLocked
    {
        get => _isLocked;
        set => SetField(ref _isLocked, value);
    }

    public string? InspectorName
    {
        get => _inspectorName;
        set => SetField(ref _inspectorName, value);
    }

    public ObservableCollection<InspectionReviewedFileRow> ReviewedFiles { get; } = [];

    public void ApplyDetail(InspectionReportDetail? detail)
    {
        ReviewedVersion = detail?.ReviewedVersion;
        IsLocked = detail?.IsLockedAfterSend ?? false;
        InspectorName = detail?.InspectorName;
    }

    public void ReplaceReviewedFiles(IEnumerable<InspectionReviewedFileRow> files)
    {
        ReviewedFiles.Clear();
        foreach (var file in files)
            ReviewedFiles.Add(file);
    }
}
