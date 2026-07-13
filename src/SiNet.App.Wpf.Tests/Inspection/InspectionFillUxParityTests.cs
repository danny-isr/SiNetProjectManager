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
    public void InspectionNoteItem_HasValidationError_tracks_status_and_text()
    {
        var note = new InspectionNoteItem
        {
            NoteNumber = "1.1.1",
            NoteId = 1,
            SectionId = 1,
            StatusText = "",
            NoteText = "",
        };
        Assert.True(note.HasValidationError);

        note.StatusText = InspectionQuestionnaireRules.NotApplicable;
        Assert.False(note.HasValidationError);

        note.StatusText = InspectionQuestionnaireRules.Failed;
        Assert.True(note.HasValidationError);

        note.NoteText = "טקסט";
        Assert.False(note.HasValidationError);
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
            NoteText = "",
        });
        chapter.Sections.Add(section);

        var q = new InspectionQuestionnaireViewModel();
        q.ReplaceTree(general: null, [chapter]);
        Assert.False(q.CanExport);
    }
}
