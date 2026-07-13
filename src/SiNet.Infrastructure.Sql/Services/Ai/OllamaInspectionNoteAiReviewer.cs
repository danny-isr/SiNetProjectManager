using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SiNet.Application.Abstractions.Inspection;
using SiNet.Application.Settings;

namespace SiNet.Infrastructure.Sql.Services.Ai;

/// <summary>
/// Ollama-backed note reviewer. Reads model routing from <see cref="ISystemSettingsQueryService"/>.
/// </summary>
internal sealed class OllamaInspectionNoteAiReviewer : IInspectionNoteAiReviewer, IDisposable
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

    private readonly ISystemSettingsQueryService _settings;
    private readonly ILogger<OllamaInspectionNoteAiReviewer>? _logger;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(5) };

    public OllamaInspectionNoteAiReviewer(
        ISystemSettingsQueryService settings,
        ILogger<OllamaInspectionNoteAiReviewer>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var ai = (await _settings.GetSystemSettingsAsync(cancellationToken).ConfigureAwait(false)).Ai;
            var baseUrl = NormalizeBaseUrl(ai.OllamaBaseUrl);
            using var response = await _httpClient
                .GetAsync(new Uri(new Uri(baseUrl), "/api/tags"), cancellationToken)
                .ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "[AI] Ollama availability check failed");
            return false;
        }
    }

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
            var ai = (await _settings.GetSystemSettingsAsync(cancellationToken).ConfigureAwait(false)).Ai;
            var baseUrl = NormalizeBaseUrl(ai.OllamaBaseUrl);
            var grammarModel = ResolveModel(ai.Simple, ai.OllamaModel);
            var rephraseModel = ResolveModel(ai.QualityCheck, ai.OllamaModel);

            if (string.IsNullOrWhiteSpace(grammarModel) || string.IsNullOrWhiteSpace(rephraseModel))
            {
                return InspectionNoteAiReviewResult.Fail(
                    original,
                    "לא הוגדר מודל AI עבור בדיקת הערות. יש להגדיר מודלים בהגדרות המערכת.");
            }

            var grammar = await GenerateAsync(baseUrl, grammarModel, GrammarPrompt + original, cancellationToken)
                .ConfigureAwait(false);
            var rephrase = await GenerateAsync(baseUrl, rephraseModel, RephrasePrompt + original, cancellationToken)
                .ConfigureAwait(false);

            return new InspectionNoteAiReviewResult(original, grammar, rephrase, null);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[AI] Note review failed");
            return InspectionNoteAiReviewResult.Fail(original, ex.Message);
        }
    }

    private async Task<string?> GenerateAsync(
        string baseUrl, string model, string prompt, CancellationToken cancellationToken)
    {
        var payload = new
        {
            model,
            prompt,
            stream = false,
        };
        using var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");
        using var response = await _httpClient
            .PostAsync(new Uri(new Uri(baseUrl), "/api/generate"), content, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var parsed = await JsonSerializer
            .DeserializeAsync<OllamaGenerateResponse>(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(parsed?.Response) ? null : parsed!.Response.Trim();
    }

    private static string ResolveModel(AiModelLevelSelectionDto level, string fallback) =>
        !string.IsNullOrWhiteSpace(level.Model) ? level.Model.Trim() : fallback?.Trim() ?? string.Empty;

    private static string NormalizeBaseUrl(string? url)
    {
        var value = string.IsNullOrWhiteSpace(url) ? SystemSettingsDefaults.OllamaBaseUrl : url.Trim().TrimEnd('/');
        return value;
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed class OllamaGenerateResponse
    {
        [JsonPropertyName("response")]
        public string? Response { get; set; }
    }
}
