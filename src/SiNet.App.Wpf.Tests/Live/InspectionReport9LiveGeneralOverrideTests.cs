using Microsoft.Extensions.DependencyInjection;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Inspection;
using SiNet.Infrastructure.Sql;
using Xunit;

namespace SiNet.App.Wpf.Tests.Live;

/// <summary>
/// LIVE INTEGRATION (product-path services), NOT LIVE UI.
/// General-field auto → manual override → restore on Report #9 via
/// <see cref="IInspectionNoteCommandService"/> / <see cref="IInspectionWorkspace"/>.
/// Uses a non-identity field (never project name / place / report number).
/// Finally restores the original text and manual-override flag.
/// </summary>
[Collection(InspectionReport9LiveCollection.Name)]
public sealed class InspectionReport9LiveGeneralOverrideTests
{
    private const int ReportId = 9;

    [Fact]
    public async Task General_field_manual_override_round_trips_and_restores_auto()
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

        var fields = await workspace.GetGeneralFieldsAsync(ReportId).ConfigureAwait(true);
        // Prefer a harmless free-text general field; avoid project identity labels.
        var field = fields.FirstOrDefault(f =>
                       f.Label.Contains("הערות", StringComparison.OrdinalIgnoreCase)
                       || f.Label.Contains("Notes", StringComparison.OrdinalIgnoreCase)
                       || f.Label.Contains("מייל", StringComparison.OrdinalIgnoreCase)
                       || f.Label.Contains("Email", StringComparison.OrdinalIgnoreCase))
                   ?? fields.FirstOrDefault(f =>
                       !f.Label.Contains("פרויקט", StringComparison.OrdinalIgnoreCase)
                       && !f.Label.Contains("Project", StringComparison.OrdinalIgnoreCase)
                       && !f.Label.Contains("מקום", StringComparison.OrdinalIgnoreCase)
                       && !f.Label.Contains("Place", StringComparison.OrdinalIgnoreCase)
                       && !f.Label.Contains("מספר דוח", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(field);
        var noteId = field!.NoteId;
        var originalManual = field.IsManualOverride;
        var originalText = field.Text;

        try
        {
            // Ensure auto baseline (clear manual)
            Assert.True((await notes.SaveNoteTextAsync(noteId, null).ConfigureAwait(true)).Succeeded);
            Assert.True((await notes.SaveNoteStatusAsync(noteId, null, null).ConfigureAwait(true)).Succeeded);

            var auto = (await workspace.GetGeneralFieldsAsync(ReportId).ConfigureAwait(true))
                .First(f => f.NoteId == noteId);
            Assert.False(auto.IsManualOverride);

            const string manualValue = "E2E-GENERAL-OVERRIDE-TEMP";
            Assert.True((await notes.SaveNoteTextAsync(noteId, manualValue).ConfigureAwait(true)).Succeeded);
            Assert.True((await notes
                .SaveNoteStatusAsync(noteId, null, InspectionQuestionnaireRules.ManualStatus)
                .ConfigureAwait(true)).Succeeded);

            var overridden = (await workspace.GetGeneralFieldsAsync(ReportId).ConfigureAwait(true))
                .First(f => f.NoteId == noteId);
            Assert.True(overridden.IsManualOverride);
            Assert.Equal(manualValue, overridden.Text);

            // Restore auto (happy-path assertion); finally still re-applies original snapshot
            Assert.True((await notes.SaveNoteTextAsync(noteId, null).ConfigureAwait(true)).Succeeded);
            Assert.True((await notes.SaveNoteStatusAsync(noteId, null, null).ConfigureAwait(true)).Succeeded);

            var restored = (await workspace.GetGeneralFieldsAsync(ReportId).ConfigureAwait(true))
                .First(f => f.NoteId == noteId);
            Assert.False(restored.IsManualOverride);
        }
        finally
        {
            // Restore original text / manual flag so Report #9 stays as found
            await notes.SaveNoteTextAsync(noteId, originalText).ConfigureAwait(true);
            await notes.SaveNoteStatusAsync(
                    noteId,
                    null,
                    originalManual ? InspectionQuestionnaireRules.ManualStatus : null)
                .ConfigureAwait(true);
        }
    }
}
