using Moq;
using SiNet.App.Wpf.Surfaces.Inspection;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Inspection;
using Xunit;
using UiReportRow = SiNet.App.Wpf.Surfaces.Inspection.InspectionReportRow;

namespace SiNet.App.Wpf.Tests.Surfaces.Inspection;

/// <summary>
/// Pure enablement + selection routing for note reorder and toolbar "+ הערה".
/// </summary>
public sealed class InspectionNoteReorderAndToolbarTests
{
    [Fact]
    public void EvaluateCanMoveNote_first_sibling_cannot_move_up()
    {
        var (section, n1, n2) = CreateSiblingSubNotes();
        Assert.False(InspectionWindowViewModel.EvaluateCanMoveNote(
            n1, -1, true, true, false, _ => section));
        Assert.True(InspectionWindowViewModel.EvaluateCanMoveNote(
            n2, -1, true, true, false, _ => section));
    }

    [Fact]
    public void EvaluateCanMoveNote_last_sibling_cannot_move_down()
    {
        var (section, n1, n2) = CreateSiblingSubNotes();
        Assert.True(InspectionWindowViewModel.EvaluateCanMoveNote(
            n1, 1, true, true, false, _ => section));
        Assert.False(InspectionWindowViewModel.EvaluateCanMoveNote(
            n2, 1, true, true, false, _ => section));
    }

    [Fact]
    public void EvaluateCanMoveNote_base_note_without_two_dots_cannot_move()
    {
        var section = new InspectionSectionItem(1, "1.1", "סעיף");
        var baseNote = new InspectionNoteItem
        {
            NoteId = 10,
            NoteNumber = "1.1",
            StatusText = "Passed",
            NoteText = "x",
        };
        section.Notes.Add(baseNote);

        Assert.False(InspectionWindowViewModel.EvaluateCanMoveNote(
            baseNote, 1, true, true, false, _ => section));
    }

    [Fact]
    public void EvaluateCanMoveNote_locked_or_busy_blocks_move()
    {
        var (section, _, n2) = CreateSiblingSubNotes();
        Assert.False(InspectionWindowViewModel.EvaluateCanMoveNote(
            n2, -1, false, true, false, _ => section));
        Assert.False(InspectionWindowViewModel.EvaluateCanMoveNote(
            n2, -1, true, true, true, _ => section));
        Assert.False(InspectionWindowViewModel.EvaluateCanMoveNote(
            n2, -1, true, false, false, _ => section));
    }

    [Fact]
    public void OnTreeSelectionChanged_note_sets_SelectedSection()
    {
        var sut = new InspectionWindowViewModel(workspace: null);
        var chapter = new InspectionChapterItem("1", "פרק");
        var section = new InspectionSectionItem(2, "1.1", "סעיף");
        var note = new InspectionNoteItem
        {
            NoteId = 3,
            NoteNumber = "1.1.1",
            StatusText = "Passed",
            NoteText = "טקסט",
        };
        section.Notes.Add(note);
        chapter.Sections.Add(section);
        sut.Questionnaire.ReplaceTree([chapter]);

        sut.OnTreeSelectionChanged(note);

        Assert.Same(note, sut.Questionnaire.SelectedNote);
        Assert.Same(section, sut.Questionnaire.SelectedSection);
    }

    [Fact]
    public void OnTreeSelectionChanged_section_clears_note_keeps_section()
    {
        var sut = new InspectionWindowViewModel(workspace: null);
        var chapter = new InspectionChapterItem("1", "פרק");
        var section = new InspectionSectionItem(2, "1.1", "סעיף");
        chapter.Sections.Add(section);
        sut.Questionnaire.ReplaceTree([chapter]);

        sut.OnTreeSelectionChanged(section);

        Assert.Null(sut.Questionnaire.SelectedNote);
        Assert.Same(section, sut.Questionnaire.SelectedSection);
    }

    [Fact]
    public void AddNoteFromSelectionCommand_without_section_sets_status_message()
    {
        var noteCommands = new Mock<IInspectionNoteCommandService>();
        var sut = new InspectionWindowViewModel(
            workspace: null,
            noteCommands: noteCommands.Object);
        sut.Metadata.IsLocked = false;
        sut.SelectedReport = new UiReportRow(9, 9, "E2E", DateTime.Today);
        sut.Questionnaire.SelectedSection = null;
        sut.Questionnaire.SelectedNote = null;

        Assert.True(sut.AddNoteFromSelectionCommand.CanExecute(null));
        sut.AddNoteFromSelectionCommand.Execute(null);

        Assert.Equal("יש לבחור סעיף או הערה לפני הוספת הערה.", sut.StatusMessage);
    }

    [Fact]
    public void InspectionWindow_toolbar_binds_AddNoteFromSelectionCommand()
    {
        var xaml = System.IO.File.ReadAllText(System.IO.Path.Combine(
            Boundary.RepoPaths.RepoRoot,
            "src",
            "SiNet.App.Wpf",
            "Surfaces",
            "Inspection",
            "InspectionWindowView.xaml"));

        Assert.Contains("AddNoteFromSelectionCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("Inspection.Toolbar.AddNote", xaml, StringComparison.Ordinal);
        Assert.Contains("Inspection.Window", xaml, StringComparison.Ordinal);
        Assert.Contains("Inspection.DrawingsPanel", xaml, StringComparison.Ordinal);
        Assert.Contains("Inspection.ReviewedFiles", xaml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("plan.dwg", null, null, "v1", new[] { "other.dwg" }, true, "plan.dwg")]
    [InlineData(null, null, null, "v1", new[] { "only.dwg" }, true, "only.dwg")]
    [InlineData(null, null, null, null, new[] { "only.dwg" }, true, null)]
    [InlineData(null, null, null, "v1", new[] { "a.dwg", "b.dwg" }, true, null)]
    public void LinkedFileOpenRules_decide_targets(
        string? linked,
        string? linkedAlt,
        string? linkedVer,
        string? reviewedVer,
        string[] reviewedNames,
        bool available,
        string? expectedFile)
    {
        var reviewed = reviewedNames.Select(n => (n, (string?)null)).ToList();
        var d = InspectionLinkedFileOpenRules.DecideOpen(
            linked, linkedAlt, linkedVer, reviewedVer, reviewed, available);

        if (expectedFile is null)
            Assert.False(d.IsEnabled);
        else
        {
            Assert.True(d.IsEnabled);
            Assert.Equal(expectedFile, d.FileName);
        }
    }

    private static (InspectionSectionItem Section, InspectionNoteItem First, InspectionNoteItem Second)
        CreateSiblingSubNotes()
    {
        var section = new InspectionSectionItem(1, "1.1", "סעיף");
        var n1 = new InspectionNoteItem
        {
            NoteId = 11,
            NoteNumber = "1.1.1",
            StatusText = "Passed",
            NoteText = "א",
        };
        var n2 = new InspectionNoteItem
        {
            NoteId = 12,
            NoteNumber = "1.1.2",
            StatusText = "Passed",
            NoteText = "ב",
        };
        section.Notes.Add(n1);
        section.Notes.Add(n2);
        return (section, n1, n2);
    }
}
