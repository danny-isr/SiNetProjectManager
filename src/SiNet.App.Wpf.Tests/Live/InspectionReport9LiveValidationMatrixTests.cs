using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Inspection;
using SiNet.Infrastructure.Sql;
using Xunit;

namespace SiNet.App.Wpf.Tests.Live;

/// <summary>
/// Live product-path validation matrix against Report #9 on the DEV SiData database.
/// Exercises the same command service the UI uses (SaveNoteStatus/SaveNoteText), then reloads
/// via workspace and asserts questionnaire rules. Gated by vault SQL connection.
/// </summary>
[Collection(InspectionReport9LiveCollection.Name)]
public sealed class InspectionReport9LiveValidationMatrixTests
{
    private const int ReportId = 9;
    private const long ProbeNoteId = 53; // 1.1.1 — safe E2E numbered note

    [Fact]
    public async Task Validation_matrix_round_trips_through_note_commands_and_restores_exportable()
    {
        var cs = LiveEnvironment.TryResolveSqlConnectionString();
        if (string.IsNullOrWhiteSpace(cs))
        {
            return; // not a live DEV machine
        }

        var services = new ServiceCollection();
        services.AddSiNetSql(cs);
        services.AddSiNetInspectionSql();
        await using var sp = services.BuildServiceProvider();
        var notes = sp.GetRequiredService<IInspectionNoteCommandService>();
        var workspace = sp.GetRequiredService<IInspectionWorkspace>();

        async Task<(string? Status, string? Text)> ReadProbeAsync()
        {
            var tree = await workspace.GetQuestionnaireTreeAsync(ReportId).ConfigureAwait(true);
            var note = tree
                .SelectMany(c => c.Sections)
                .SelectMany(s => s.Notes)
                .First(n => n.NoteId == ProbeNoteId);
            return (note.Status, note.Text);
        }

        // Snapshot restore target at end: NotApplicable + empty is valid and keeps #9 exportable
        // when other notes stay NotApplicable / Failed+text.
        var cases = new (string? Status, string? Text, bool Invalid, bool ManagerBlocks)[]
        {
            (null, "", true, false),
            ("Passed", "", true, false),
            ("Passed", "E2E passed text", false, false),
            ("Failed", "", true, false),
            ("Failed", "E2E failed text", false, false),
            ("RecurringFailed", "", true, false),
            ("RecurringFailed", "E2E recurring text", false, false),
            ("NotApplicable", "", false, false),
            ("ManagerReview", "E2E manager text", false, true),
        };

        foreach (var (status, text, invalid, managerBlocks) in cases)
        {
            var saveText = await notes.SaveNoteTextAsync(ProbeNoteId, text).ConfigureAwait(true);
            Assert.True(saveText.Succeeded, saveText.ErrorMessage);
            var saveStatus = await notes.SaveNoteStatusAsync(ProbeNoteId, statusId: null, statusText: status)
                .ConfigureAwait(true);
            Assert.True(saveStatus.Succeeded, saveStatus.ErrorMessage);

            var (readStatus, readText) = await ReadProbeAsync().ConfigureAwait(true);
            Assert.Equal(status ?? string.Empty, readStatus ?? string.Empty);
            Assert.Equal(text ?? string.Empty, readText ?? string.Empty);

            var hasError = InspectionQuestionnaireRules.HasValidationError(readStatus, readText);
            Assert.Equal(invalid, hasError);
            if (managerBlocks)
                Assert.False(InspectionQuestionnaireRules.CanExportNotes([(readStatus, readText)]));
        }

        // Restore exportable probe note
        Assert.True((await notes.SaveNoteTextAsync(ProbeNoteId, "").ConfigureAwait(true)).Succeeded);
        Assert.True((await notes.SaveNoteStatusAsync(ProbeNoteId, null, "NotApplicable").ConfigureAwait(true)).Succeeded);

        var treeFinal = await workspace.GetQuestionnaireTreeAsync(ReportId).ConfigureAwait(true);
        var numbered = treeFinal
            .SelectMany(c => c.Sections)
            .SelectMany(s => s.Notes)
            .Where(n => InspectionQuestionnaireRules.IsNumberedSubNote(n.Number))
            .Select(n => (n.Status, n.Text))
            .ToList();

        // Ensure all numbered notes are individually valid and none ManagerReview
        foreach (var (st, tx) in numbered)
        {
            Assert.False(InspectionQuestionnaireRules.HasValidationError(st, tx));
            Assert.False(string.Equals(st, InspectionQuestionnaireRules.ManagerReview, StringComparison.Ordinal));
        }

        Assert.True(InspectionQuestionnaireRules.CanExportNotes(numbered));

        var detail = await workspace.GetReportDetailAsync(ReportId).ConfigureAwait(true);
        Assert.NotNull(detail);
        Assert.Null(detail!.SentAt);
        Assert.False(detail.IsLockedAfterSend);
    }
}
