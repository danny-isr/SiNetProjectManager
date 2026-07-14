using Moq;
using SiNet.App.Wpf.Surfaces.Inspection;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Projects;
using Xunit;

namespace SiNet.App.Wpf.Tests.Surfaces.Inspection;

public sealed class InspectionWindowViewModelNoteRowFeatureTests
{
    [Fact]
    public async Task ReviewNoteAiAsync_fills_cache_without_changing_note_text()
    {
        var ai = new Mock<IInspectionNoteAiReviewer>();
        ai.Setup(a => a.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        ai.Setup(a => a.ReviewAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InspectionNoteAiReviewResult(
                OriginalText: "טקסט מקורי",
                GrammarCorrected: "טקסט מתוקן",
                Rephrased: "טקסט מנוסח",
                ErrorMessage: null));

        var sut = new InspectionWindowViewModel(workspace: null, aiReviewer: ai.Object);
        var note = sut.Questionnaire.EnumerateNotes().First();
        note.SetNoteTextWithoutStatusSync("טקסט מקורי");
        note.ClearDirty();

        await sut.ReviewNoteAiAsync(note);

        Assert.Equal("טקסט מקורי", note.NoteText);
        Assert.Equal("טקסט מקורי", note.AiOriginalText);
        Assert.Equal("טקסט מתוקן", note.AiGrammarResult);
        Assert.Equal("טקסט מנוסח", note.AiRephraseResult);
        Assert.True(note.HasAiGrammarChanges);
        Assert.False(note.AiReviewInProgress);
    }

    [Fact]
    public async Task ApplyAiSuggestionAsync_writes_suggestion_and_saves()
    {
        var noteCommands = new Mock<IInspectionNoteCommandService>();
        noteCommands
            .Setup(c => c.SaveNoteTextAsync(It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InspectionNoteCommandResult.Ok());

        var ai = new Mock<IInspectionNoteAiReviewer>();
        ai.Setup(a => a.IsAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        ai.Setup(a => a.ReviewAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InspectionNoteAiReviewResult("חדש", "חדש", "חדש מנוסח", null));

        var sut = new InspectionWindowViewModel(
            workspace: null,
            noteCommands: noteCommands.Object,
            aiReviewer: ai.Object);
        var note = sut.Questionnaire.EnumerateNotes().First(n => n.NoteId is > 0);
        note.SetNoteTextWithoutStatusSync("ישן");
        note.AiGrammarResult = "חדש";
        note.AiOriginalText = "ישן";

        await sut.ApplyAiSuggestionAsync(note, "grammar", "חדש");

        Assert.Equal("חדש", note.NoteText);
        noteCommands.Verify(
            c => c.SaveNoteTextAsync(note.NoteId!.Value, "חדש", It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task SetNoteLinkedFileCommand_persists_pick_and_updates_note()
    {
        var picker = new Mock<IInspectionFileTreePickerHost>();
        picker
            .Setup(p => p.PickNoteLinkedFileAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InspectionFilePickResult("plan.dwg", "A", "3", null));

        var noteCommands = new Mock<IInspectionNoteCommandService>();
        noteCommands
            .Setup(c => c.SetNoteLinkedFileAsync(
                It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InspectionNoteCommandResult.Ok());

        var project = new FakeProjectContext(new ProjectSummaryDto(
            5, "1", "P", null, null, null, null, null, true));

        var sut = new InspectionWindowViewModel(
            workspace: null,
            noteCommands: noteCommands.Object,
            currentProject: project,
            fileTreePicker: picker.Object);

        var note = sut.Questionnaire.EnumerateNotes().First(n => n.NoteId is > 0);
        note.HasLinkedFile = false;

        sut.SetNoteLinkedFileCommand.Execute(note);
        await WaitUntilAsync(() => note.HasLinkedFile);

        Assert.Equal("plan.dwg", note.LinkedFileName);
        Assert.Equal("A", note.LinkedAlternative);
        Assert.Equal("3", note.LinkedVersion);
        noteCommands.Verify(
            c => c.SetNoteLinkedFileAsync(note.NoteId!.Value, "plan.dwg", "A", "3", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ScreenshotPrimary_uploads_and_updates_attachment_state()
    {
        var shot = new Mock<IInspectionNoteScreenshotHost>();
        shot
            .Setup(s => s.UploadFromClipboardAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InspectionScreenshotUploadResult.Ok("https://drive.example/shot.png"));

        var sut = new InspectionWindowViewModel(
            workspace: null,
            screenshotHost: shot.Object);

        // Unlock metadata so IsReportEditable is true for upload can-execute path used by primary.
        sut.Metadata.IsLocked = false;
        var note = sut.Questionnaire.EnumerateNotes().First(n => n.NoteId is > 0);
        note.AttachmentCount = 0;
        note.LastAttachmentUrl = null;

        sut.ScreenshotPrimaryCommand.Execute(note);
        await WaitUntilAsync(() => note.AttachmentCount > 0);

        Assert.Equal(1, note.AttachmentCount);
        Assert.Equal("https://drive.example/shot.png", note.LastAttachmentUrl);
        Assert.True(note.HasAttachments);
        shot.Verify(s => s.UploadFromClipboardAsync(note.NoteId!.Value, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var start = Environment.TickCount64;
        while (Environment.TickCount64 - start < timeoutMs)
        {
            if (condition())
                return;
            await Task.Delay(25);
        }

        Assert.True(condition(), "Timed out waiting for condition.");
    }

    private sealed class FakeProjectContext(ProjectSummaryDto? current) : ICurrentProjectContext
    {
        public ProjectSummaryDto? CurrentProject { get; private set; } = current;
        public event EventHandler<ProjectChangedEventArgs>? CurrentProjectChanged;

        public Task SetCurrentProjectAsync(ProjectSummaryDto? project, CancellationToken cancellationToken = default)
        {
            CurrentProject = project;
            CurrentProjectChanged?.Invoke(this, new ProjectChangedEventArgs(project));
            return Task.CompletedTask;
        }
    }
}
