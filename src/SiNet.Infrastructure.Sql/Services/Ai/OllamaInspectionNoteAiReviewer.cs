using Microsoft.Extensions.Logging;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Ai;

namespace SiNet.Infrastructure.Sql.Services.Ai;

/// <summary>
/// Inspection note reviewer on the generic AI port.
/// Grammar = <see cref="AiCompletionLevel.Simple"/>; rephrase = <see cref="AiCompletionLevel.QualityCheck"/>.
/// </summary>
internal sealed class OllamaInspectionNoteAiReviewer : IInspectionNoteAiReviewer
{
    private const string GrammarPrompt =
        """
        אתה עורך לשוני מקצועי בעברית.
        תקן את הטקסט הבא — תקן שגיאות כתיב, דקדוק ופיסוק בלבד.
        אל תשנה את המשמעות או את המבנה.
        החזר רק את הטקסט המתוקן, בלי הסברים, בלי מרכאות, בלי תוספות.
        הטקסט:
        """;

    private const string RephrasePrompt =
        """
        אתה כותב מקצועי בתחום הנדסה אזרחית ובדיקות ביקורת.
        נסח מחדש את הטקסט הבא בצורה מקצועית, ברורה ותמציתית.
        שמור על המשמעות המקורית אבל שפר את הניסוח.
        החזר רק את הטקסט המנוסח מחדש, בלי הסברים, בלי מרכאות, בלי תוספות.
        הטקסט:
        """;

    private readonly IAiCompletionService _completion;
    private readonly ILogger<OllamaInspectionNoteAiReviewer>? _logger;

    public OllamaInspectionNoteAiReviewer(
        IAiCompletionService completion,
        ILogger<OllamaInspectionNoteAiReviewer>? logger = null)
    {
        _completion = completion ?? throw new ArgumentNullException(nameof(completion));
        _logger = logger;
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        _completion.IsAvailableAsync(AiCompletionLevel.Simple, cancellationToken);

    public async Task<InspectionNoteAiReviewResult> ReviewAsync(
        string plainText, CancellationToken cancellationToken = default)
    {
        var original = plainText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(original))
        {
            return InspectionNoteAiReviewResult.Fail(original, "טקסט ריק — אין מה לבדוק.");
        }

        try
        {
            var grammar = await _completion
                .CompleteAsync(new AiCompletionRequest(GrammarPrompt + original, AiCompletionLevel.Simple), cancellationToken)
                .ConfigureAwait(false);
            if (!grammar.Succeeded)
            {
                return InspectionNoteAiReviewResult.Fail(
                    original,
                    string.IsNullOrWhiteSpace(grammar.UserMessageHe)
                        ? "בדיקת AI נכשלה."
                        : grammar.UserMessageHe!);
            }

            var rephrase = await _completion
                .CompleteAsync(new AiCompletionRequest(RephrasePrompt + original, AiCompletionLevel.QualityCheck), cancellationToken)
                .ConfigureAwait(false);
            if (!rephrase.Succeeded)
            {
                return InspectionNoteAiReviewResult.Fail(
                    original,
                    string.IsNullOrWhiteSpace(rephrase.UserMessageHe)
                        ? "בדיקת AI נכשלה."
                        : rephrase.UserMessageHe!);
            }

            return new InspectionNoteAiReviewResult(
                original,
                string.IsNullOrWhiteSpace(grammar.Text) ? null : grammar.Text.Trim(),
                string.IsNullOrWhiteSpace(rephrase.Text) ? null : rephrase.Text.Trim(),
                null);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[AI] Note review failed");
            return InspectionNoteAiReviewResult.Fail(original, ex.Message);
        }
    }
}
