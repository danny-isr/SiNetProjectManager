using SiNet.App.Wpf.Surfaces.Inspection;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Inspection;
using Xunit;

namespace SiNet.App.Wpf.Tests.Inspection;

public sealed class InspectionFillUxParityTests
{
    [Theory]
    [InlineData("1.1.1", true)]
    [InlineData("2.3.10", true)]
    [InlineData("1.1", false)]
    [InlineData("1", false)]
    [InlineData(null, false)]
    public void IsNumberedSubNote_requires_two_dots(string? index, bool expected) =>
        Assert.Equal(expected, InspectionQuestionnaireRules.IsNumberedSubNote(index));

    [Theory]
    [InlineData("1", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("1.1", false)]
    public void IsGeneralBaseNote_rejects_dotted_indexes(string? index, bool expected) =>
        Assert.Equal(expected, InspectionQuestionnaireRules.IsGeneralBaseNote(index));

    [Fact]
    public void HasValidationError_when_status_missing() =>
        Assert.True(InspectionQuestionnaireRules.HasValidationError(null, "text"));

    [Fact]
    public void HasValidationError_when_text_empty_and_not_na() =>
        Assert.True(InspectionQuestionnaireRules.HasValidationError(
            InspectionQuestionnaireRules.Failed, ""));

    [Fact]
    public void HasValidationError_false_for_not_applicable_empty_text() =>
        Assert.False(InspectionQuestionnaireRules.HasValidationError(
            InspectionQuestionnaireRules.NotApplicable, ""));

    [Fact]
    public void HasGeneralFieldValidationError_when_empty() =>
        Assert.True(InspectionQuestionnaireRules.HasGeneralFieldValidationError("  "));

    [Fact]
    public void HasGeneralFieldValidationError_false_when_filled() =>
        Assert.False(InspectionQuestionnaireRules.HasGeneralFieldValidationError("שם"));

    [Fact]
    public void CanExportNotes_blocks_manager_review() =>
        Assert.False(InspectionQuestionnaireRules.CanExportNotes(
        [
            (InspectionQuestionnaireRules.Failed, "ok"),
            (InspectionQuestionnaireRules.ManagerReview, "needs review"),
        ]));

    [Fact]
    public void CanExportNotes_allows_complete_notes() =>
        Assert.True(InspectionQuestionnaireRules.CanExportNotes(
        [
            (InspectionQuestionnaireRules.Failed, "finding"),
            (InspectionQuestionnaireRules.NotApplicable, ""),
        ]));

    [Fact]
    public void CanExport_blocks_empty_general_even_when_notes_ok() =>
        Assert.False(InspectionQuestionnaireRules.CanExport(
            generalValues: [null, "ממולא"],
            notes: [(InspectionQuestionnaireRules.Failed, "finding")]));

    [Fact]
    public void CanExport_allows_when_general_and_notes_complete() =>
        Assert.True(InspectionQuestionnaireRules.CanExport(
            generalValues: ["פרויקט", "תאריך"],
            notes: [(InspectionQuestionnaireRules.Failed, "finding")]));

    [Fact]
    public void BuildValidationSummary_mentions_general_and_notes()
    {
        var summary = InspectionQuestionnaireRules.BuildValidationSummary(2, 3);
        Assert.Contains("2 שדות כלליים ריקים", summary, StringComparison.Ordinal);
        Assert.Contains("3 הערות לא תקינות", summary, StringComparison.Ordinal);
        Assert.Contains("ייצוא", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void SyncStatusAfterTextChange_sets_failed_when_typing() =>
        Assert.Equal(
            InspectionQuestionnaireRules.Failed,
            InspectionQuestionnaireRules.SyncStatusAfterTextChange("", "hello"));

    [Fact]
    public void MapGeneralFields_applies_auto_values_when_not_manual()
    {
        var rows = new[]
        {
            new InspectionGeneralFieldRow(1, 10, "שם פרויקט", Text: null, IsManualOverride: false),
            new InspectionGeneralFieldRow(2, 11, "הערות", Text: "ידני", IsManualOverride: false),
        };
        var auto = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["שם פרויקט"] = "פרויקט בדיקה",
        };

        var chapter = InspectionQuestionnaireViewModel.MapGeneralFields(rows, auto);
        Assert.NotNull(chapter);
        Assert.Equal(2, chapter!.Fields.Count);
        Assert.True(chapter.Fields[0].IsAutomatic);
        Assert.Equal("פרויקט בדיקה", chapter.Fields[0].Value);
        Assert.False(chapter.Fields[0].HasValidationError);
        Assert.False(chapter.Fields[1].IsAutomatic);
        Assert.Equal("ידני", chapter.Fields[1].Value);
    }

    [Fact]
    public void MapGeneralFields_keeps_stored_text_when_manual_override()
    {
        var rows = new[]
        {
            new InspectionGeneralFieldRow(1, 10, "שם פרויקט", Text: "שם ידני", IsManualOverride: true),
        };
        var auto = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["שם פרויקט"] = "אוטומטי",
        };

        var chapter = InspectionQuestionnaireViewModel.MapGeneralFields(rows, auto);
        Assert.NotNull(chapter);
        Assert.True(chapter!.Fields[0].IsManualOverride);
        Assert.Equal("שם ידני", chapter.Fields[0].Value);
        Assert.Equal("אוטומטי", chapter.Fields[0].AutoValue);
    }

    [Fact]
    public void InspectionGeneralFieldItem_HasValidationError_when_empty()
    {
        var field = new InspectionGeneralFieldItem { Label = "הערות", Value = "" };
        Assert.True(field.HasValidationError);
        field.Value = "טקסט";
        Assert.False(field.HasValidationError);
    }

    [Fact]
    public void InspectionNoteItem_HasValidationError_tracks_status_and_text()
    {
        var note = new InspectionNoteItem
        {
            NoteNumber = "1.1.1",
            NoteId = 1,
            SectionId = 1,
            StatusText = "",
        };
        note.SetNoteTextWithoutStatusSync("");
        Assert.True(note.HasValidationError);

        note.StatusText = InspectionQuestionnaireRules.NotApplicable;
        Assert.False(note.HasValidationError);

        note.StatusText = InspectionQuestionnaireRules.Failed;
        Assert.True(note.HasValidationError);

        note.NoteText = "טקסט";
        Assert.False(note.HasValidationError);
    }

    [Fact]
    public void Questionnaire_CanExport_false_when_general_empty()
    {
        var general = new InspectionGeneralChapterItem();
        general.Fields.Add(new InspectionGeneralFieldItem { Label = "הערות", Value = "" });

        var chapter = new InspectionChapterItem("1", "כללי");
        var section = new InspectionSectionItem(1, "1.1", "סעיף");
        var note = new InspectionNoteItem
        {
            NoteNumber = "1.1.1",
            NoteId = 1,
            SectionId = 1,
            StatusText = InspectionQuestionnaireRules.Failed,
        };
        note.SetNoteTextWithoutStatusSync("טקסט");
        section.Notes.Add(note);
        chapter.Sections.Add(section);

        var q = new InspectionQuestionnaireViewModel();
        q.ReplaceTree(general, [chapter]);
        Assert.False(q.CanExport);
        Assert.Contains("שדות כלליים", q.ValidationSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void Questionnaire_CanExport_false_when_any_note_invalid()
    {
        var chapter = new InspectionChapterItem("1", "כללי");
        var section = new InspectionSectionItem(1, "1.1", "סעיף");
        section.Notes.Add(new InspectionNoteItem
        {
            NoteNumber = "1.1.1",
            NoteId = 1,
            SectionId = 1,
            StatusText = InspectionQuestionnaireRules.Failed,
        });
        chapter.Sections.Add(section);

        var q = new InspectionQuestionnaireViewModel();
        q.ReplaceTree(general: null, [chapter]);
        Assert.False(q.CanExport);
    }

    [Fact]
    public void Questionnaire_FindSectionContaining_and_move_renumber_shape()
    {
        var chapter = new InspectionChapterItem("1", "כללי");
        var section = new InspectionSectionItem(1, "1.1", "סעיף");
        var a = new InspectionNoteItem { NoteNumber = "1.1.1", NoteId = 1, SectionId = 1 };
        var b = new InspectionNoteItem { NoteNumber = "1.1.2", NoteId = 2, SectionId = 1 };
        section.Notes.Add(a);
        section.Notes.Add(b);
        chapter.Sections.Add(section);

        var q = new InspectionQuestionnaireViewModel();
        q.ReplaceTree(general: null, [chapter]);

        Assert.Same(section, q.FindSectionContaining(b));
        section.Notes.Move(1, 0);
        Assert.Same(b, section.Notes[0]);

        var ordinal = 1;
        foreach (var n in section.Notes)
        {
            n.NoteNumber = $"{section.SectionNumber}.{ordinal}";
            ordinal++;
        }

        Assert.Equal("1.1.1", b.NoteNumber);
        Assert.Equal("1.1.2", a.NoteNumber);
    }
}
