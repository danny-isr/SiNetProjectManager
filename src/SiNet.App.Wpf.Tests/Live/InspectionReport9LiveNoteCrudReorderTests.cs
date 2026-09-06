using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Inspection;
using SiNet.Infrastructure.Sql;
using Xunit;

namespace SiNet.App.Wpf.Tests.Live;

/// <summary>
/// LIVE INTEGRATION (product-path services), NOT LIVE UI.
/// Note add + renumber (Move Up/Down product path) on Report #9 section 1.1 via
/// <see cref="IInspectionNoteCommandService"/> / <see cref="IInspectionWorkspace"/>.
/// Creates temporary E2E sibling notes; finally neutralizes E2E / invalid numbered notes
/// to NotApplicable + empty so #9 stays exportable (SubIndex leftovers documented for ops).
/// </summary>
[Collection(InspectionReport9LiveCollection.Name)]
public sealed class InspectionReport9LiveNoteCrudReorderTests
{
    private const int ReportId = 9;

    [Fact]
    public async Task Add_two_sibling_notes_renumber_persists_unique_subindex()
    {
        var cs = LiveEnvironment.TryResolveSqlConnectionString();
        if (string.IsNullOrWhiteSpace(cs))
            return;

        var services = new ServiceCollection();
        services.AddSiNetSql(cs);
        services.AddSiNetInspectionSql();
        await using var sp = services.BuildServiceProvider();
        var notes = sp.GetRequiredService<IInspectionNoteCommandService>();
        var workspace = sp.GetRequiredService<IInspectionWorkspace>();

        var tree = await workspace.GetQuestionnaireTreeAsync(ReportId).ConfigureAwait(true);
        var section = tree
            .SelectMany(c => c.Sections)
            .First(s => s.Notes.Any(n => n.Number == "1.1.1"));
        var sectionId = section.SectionId;

        var before = section.Notes.Select(n => n.NoteId).ToHashSet();
        var createdNoteIds = new HashSet<long>();

        try
        {
            var a = await notes.AddNoteAsync(ReportId, sectionId, "E2E sibling A").ConfigureAwait(true);
            Assert.True(a.Succeeded, a.ErrorMessage);
            Assert.NotNull(a.NoteId);
            createdNoteIds.Add(a.NoteId!.Value);

            var b = await notes.AddNoteAsync(ReportId, sectionId, "E2E sibling B").ConfigureAwait(true);
            Assert.True(b.Succeeded, b.ErrorMessage);
            Assert.NotNull(b.NoteId);
            createdNoteIds.Add(b.NoteId!.Value);

            tree = await workspace.GetQuestionnaireTreeAsync(ReportId).ConfigureAwait(true);
            section = tree.SelectMany(c => c.Sections).First(s => s.SectionId == sectionId);
            var added = section.Notes.Where(n => !before.Contains(n.NoteId)).OrderBy(n => n.Number).ToList();
            Assert.True(added.Count >= 2, $"Expected ≥2 new siblings, got {added.Count}");
            foreach (var n in added)
                createdNoteIds.Add(n.NoteId);

            var subIndexes = section.Notes.Select(n => n.Number).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            Assert.Equal(subIndexes.Count, subIndexes.Distinct(StringComparer.Ordinal).Count());

            // Simulate Move Down of first added sibling: swap SubIndex with next sibling among added pair
            var first = added[0];
            var second = added[1];
            Assert.False(string.IsNullOrWhiteSpace(first.Number));
            Assert.False(string.IsNullOrWhiteSpace(second.Number));

            var renumber = await notes.RenumberNotesAsync(
                [
                    (first.NoteId, second.Number!),
                    (second.NoteId, first.Number!),
                ]).ConfigureAwait(true);
            Assert.True(renumber.Succeeded, renumber.ErrorMessage);

            tree = await workspace.GetQuestionnaireTreeAsync(ReportId).ConfigureAwait(true);
            section = tree.SelectMany(c => c.Sections).First(s => s.SectionId == sectionId);
            var afterFirst = section.Notes.First(n => n.NoteId == first.NoteId);
            var afterSecond = section.Notes.First(n => n.NoteId == second.NoteId);
            Assert.Equal(second.Number, afterFirst.Number);
            Assert.Equal(first.Number, afterSecond.Number);

            // Edit text + status on one sibling and reopen
            Assert.True((await notes.SaveNoteTextAsync(first.NoteId, "E2E edited after reorder").ConfigureAwait(true)).Succeeded);
            Assert.True((await notes.SaveNoteStatusAsync(first.NoteId, null, "Failed").ConfigureAwait(true)).Succeeded);

            tree = await workspace.GetQuestionnaireTreeAsync(ReportId).ConfigureAwait(true);
            var reopened = tree.SelectMany(c => c.Sections).SelectMany(s => s.Notes)
                .First(n => n.NoteId == first.NoteId);
            Assert.Equal("E2E edited after reorder", reopened.Text);
            Assert.Equal("Failed", reopened.Status);
        }
        finally
        {
            // Neutralize created E2E siblings + any invalid/ManagerReview numbered notes
            // to NotApplicable + empty so #9 stays exportable. SubIndex rows may remain.
            tree = await workspace.GetQuestionnaireTreeAsync(ReportId).ConfigureAwait(true);
            foreach (var n in tree.SelectMany(c => c.Sections).SelectMany(s => s.Notes))
            {
                var needsSanitize =
                    createdNoteIds.Contains(n.NoteId)
                    || string.Equals(n.Text, "E2E sibling A", StringComparison.Ordinal)
                    || string.Equals(n.Text, "E2E sibling B", StringComparison.Ordinal)
                    || string.Equals(n.Text, "E2E edited after reorder", StringComparison.Ordinal)
                    || (n.Text?.StartsWith("E2E ", StringComparison.Ordinal) == true)
                    || InspectionQuestionnaireRules.HasValidationError(n.Status, n.Text)
                    || string.Equals(
                        n.Status,
                        InspectionQuestionnaireRules.ManagerReview,
                        StringComparison.Ordinal);

                if (!needsSanitize)
                    continue;

                await notes.SaveNoteTextAsync(n.NoteId, "").ConfigureAwait(true);
                await notes.SaveNoteStatusAsync(n.NoteId, null, InspectionQuestionnaireRules.NotApplicable)
                    .ConfigureAwait(true);
            }
        }

        tree = await workspace.GetQuestionnaireTreeAsync(ReportId).ConfigureAwait(true);
        var numbered = tree.SelectMany(c => c.Sections).SelectMany(s => s.Notes).ToList();
        var stillBad = numbered
            .Where(n =>
                InspectionQuestionnaireRules.HasValidationError(n.Status, n.Text)
                || string.Equals(
                    n.Status,
                    InspectionQuestionnaireRules.ManagerReview,
                    StringComparison.Ordinal))
            .Select(n => $"{n.NoteId}:{n.Number}:{n.Status}:len={(n.Text?.Length ?? 0)}")
            .ToList();
        Assert.True(
            stillBad.Count == 0,
            "Report #9 still has invalid numbered notes after sanitize: " + string.Join("; ", stillBad));
        Assert.True(
            InspectionQuestionnaireRules.CanExportNotes(
                numbered.Select(n => (n.Status, n.Text))));
    }
}
