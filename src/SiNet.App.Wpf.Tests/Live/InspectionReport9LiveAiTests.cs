using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Inspection;
using SiNet.Infrastructure.Sql;
using SiNet.Infrastructure.Sql.Services.Ai;
using Xunit;

namespace SiNet.App.Wpf.Tests.Live;

/// <summary>
/// LIVE INTEGRATION (product-path services), NOT LIVE UI.
/// AI grammar/rephrase against Ollama on Report #9 probe note text via
/// <see cref="IInspectionNoteAiReviewer"/> / <see cref="IInspectionNoteCommandService"/>.
/// Forced network failures remain unit-only elsewhere.
/// Finally restores probe note 53 (1.1.1) to NotApplicable + empty.
/// </summary>
[Collection(InspectionReport9LiveCollection.Name)]
public sealed class InspectionReport9LiveAiTests
{
    private const long ProbeNoteId = 53;

    [Fact]
    public async Task Ollama_grammar_and_rephrase_return_suggestions_without_mutating_until_save()
    {
        var cs = LiveEnvironment.TryResolveSqlConnectionString();
        if (string.IsNullOrWhiteSpace(cs))
            return;

        var services = new ServiceCollection();
        services.AddSiNetSql(cs);
        services.AddSiNetIdentitySql();
        services.AddSiNetAuthorizationSql();
        services.AddSiNetSystemSettingsSql();
        services.AddSiNetInspectionSql();
        services.AddSiNetAi();
        await using var sp = services.BuildServiceProvider();

        var ai = sp.GetRequiredService<IInspectionNoteAiReviewer>();
        var workspace = sp.GetRequiredService<IInspectionWorkspace>();
        var notes = sp.GetRequiredService<IInspectionNoteCommandService>();

        if (!await ai.IsAvailableAsync().ConfigureAwait(true))
            return; // Ollama down — not a product defect for this gate

        var tree = await workspace.GetQuestionnaireTreeAsync(9).ConfigureAwait(true);
        var probe = tree.SelectMany(c => c.Sections).SelectMany(s => s.Notes)
            .First(n => n.NoteId == ProbeNoteId);

        var original = string.IsNullOrWhiteSpace(probe.Text)
            ? "יש להשלים פרטי חיבור במפלס הקרקע"
            : probe.Text!;

        try
        {
            // Ensure known original in DB for reopen proof after Apply
            Assert.True((await notes.SaveNoteTextAsync(ProbeNoteId, original).ConfigureAwait(true)).Succeeded);
            Assert.True((await notes.SaveNoteStatusAsync(ProbeNoteId, null, "Failed").ConfigureAwait(true)).Succeeded);

            var result = await ai.ReviewAsync(original).ConfigureAwait(true);
            Assert.False(string.IsNullOrWhiteSpace(result.GrammarCorrected ?? result.OriginalText));
            Assert.False(string.IsNullOrWhiteSpace(result.Rephrased));

            // Non-apply: DB unchanged
            var afterSuggest = (await workspace.GetQuestionnaireTreeAsync(9).ConfigureAwait(true))
                .SelectMany(c => c.Sections).SelectMany(s => s.Notes)
                .First(n => n.NoteId == ProbeNoteId);
            Assert.Equal(original, afterSuggest.Text);

            // Apply grammar
            var grammar = result.GrammarCorrected ?? result.OriginalText;
            Assert.True((await notes.SaveNoteTextAsync(ProbeNoteId, grammar).ConfigureAwait(true)).Succeeded);
            var afterGrammar = (await workspace.GetQuestionnaireTreeAsync(9).ConfigureAwait(true))
                .SelectMany(c => c.Sections).SelectMany(s => s.Notes)
                .First(n => n.NoteId == ProbeNoteId);
            Assert.Equal(grammar, afterGrammar.Text);

            // Apply rephrase
            Assert.True((await notes.SaveNoteTextAsync(ProbeNoteId, result.Rephrased!).ConfigureAwait(true)).Succeeded);
            var afterRephrase = (await workspace.GetQuestionnaireTreeAsync(9).ConfigureAwait(true))
                .SelectMany(c => c.Sections).SelectMany(s => s.Notes)
                .First(n => n.NoteId == ProbeNoteId);
            Assert.Equal(result.Rephrased, afterRephrase.Text);
        }
        finally
        {
            // Restore probe 53 / 1.1.1 to exportable NotApplicable + empty
            await notes.SaveNoteTextAsync(ProbeNoteId, "").ConfigureAwait(true);
            await notes.SaveNoteStatusAsync(ProbeNoteId, null, InspectionQuestionnaireRules.NotApplicable)
                .ConfigureAwait(true);
        }
    }
}
