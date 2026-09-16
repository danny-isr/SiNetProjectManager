using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SiNet.Application.Ai;
using SiNet.Application.Configuration;
using SiNet.Application.Settings;

namespace SiNet.Infrastructure.Sql.Services.Ai;

/// <summary>
/// Routes <see cref="AiCompletionLevel"/> through System Settings. Ollama + Gemini only;
/// OpenAICompatible is not implemented (no base URL / auth settings exist).
/// </summary>
internal sealed class SettingsAiCompletionService(
    ISystemSettingsQueryService settings,
    IAiHttpTransport transport,
    ISecretVaultStore? vault = null,
    ILogger<SettingsAiCompletionService>? logger = null) : IAiCompletionService
{
    private static readonly Uri GeminiModelsRoot = new("https://generativelanguage.googleapis.com/v1beta/models/");

    private readonly ISystemSettingsQueryService _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly IAiHttpTransport _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    private readonly ISecretVaultStore? _vault = vault;
    private readonly ILogger<SettingsAiCompletionService>? _logger = logger;

    public async Task<AiCompletionResult> CompleteAsync(
        AiCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Prompt) && string.IsNullOrWhiteSpace(request.SystemInstructions))
        {
            return AiCompletionResult.Fail(AiCompletionFailureKind.MalformedResponse, "אין טקסט לשליחה ל-AI.");
        }

        try
        {
            var ai = (await _settings.GetSystemSettingsAsync(cancellationToken).ConfigureAwait(false)).Ai;
            var resolved = Resolve(ai, request.Level);
            if (resolved.Failure is { } fail)
            {
                return fail;
            }

            if (string.Equals(resolved.Provider, AiProviderNames.Ollama, StringComparison.OrdinalIgnoreCase))
            {
                return await CompleteOllamaAsync(ai, resolved.Model, request, cancellationToken).ConfigureAwait(false);
            }

            if (string.Equals(resolved.Provider, AiProviderNames.Gemini, StringComparison.OrdinalIgnoreCase))
            {
                return await CompleteGeminiAsync(resolved.Model, request, cancellationToken).ConfigureAwait(false);
            }

            if (string.Equals(resolved.Provider, AiProviderNames.OpenAiCompatible, StringComparison.OrdinalIgnoreCase))
            {
                return AiCompletionResult.Fail(
                    AiCompletionFailureKind.ProviderNotConfigured,
                    "ספק OpenAICompatible אינו מוגדר במערכת — אין כתובת בסיס או מפתח.",
                    AiProviderNames.OpenAiCompatible,
                    resolved.Model);
            }

            return AiCompletionResult.Fail(
                AiCompletionFailureKind.ProviderNotConfigured,
                $"ספק AI אינו נתמך: {resolved.Provider}.",
                resolved.Provider,
                resolved.Model);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return AiCompletionResult.Fail(AiCompletionFailureKind.Cancelled, "בקשת ה-AI בוטלה.");
        }
        catch (TimeoutException ex)
        {
            _logger?.LogWarning(ex, "[AI] Completion timed out");
            return AiCompletionResult.Fail(AiCompletionFailureKind.Timeout, "בקשת ה-AI חרגה מזמן ההמתנה.");
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogWarning(ex, "[AI] Provider HTTP unavailable");
            return AiCompletionResult.Fail(AiCompletionFailureKind.Unavailable, "שירות ה-AI אינו זמין כרגע.");
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "[AI] Malformed provider response");
            return AiCompletionResult.Fail(AiCompletionFailureKind.MalformedResponse, "תשובת ה-AI אינה תקינה.");
        }
    }

    public async Task<bool> IsAvailableAsync(
        AiCompletionLevel level = AiCompletionLevel.Simple,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var ai = (await _settings.GetSystemSettingsAsync(cancellationToken).ConfigureAwait(false)).Ai;
            var resolved = Resolve(ai, level);
            if (resolved.Failure is not null)
            {
                return false;
            }

            if (string.Equals(resolved.Provider, AiProviderNames.Ollama, StringComparison.OrdinalIgnoreCase))
            {
                var baseUrl = NormalizeOllamaBaseUrl(ai.OllamaBaseUrl);
                var response = await _transport
                    .SendAsync(new AiHttpRequest("GET", new Uri(new Uri(baseUrl + "/"), "api/tags"), null, TimeSpan.FromSeconds(5)), cancellationToken)
                    .ConfigureAwait(false);
                return response.StatusCode is >= 200 and < 300;
            }

            if (string.Equals(resolved.Provider, AiProviderNames.Gemini, StringComparison.OrdinalIgnoreCase))
            {
                return _vault is not null && _vault.HasSecret(SecretCatalog.GeminiApiKey);
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "[AI] Availability check failed");
            return false;
        }
    }

    internal static (string Provider, string Model, AiCompletionResult? Failure) Resolve(
        AiSystemSettingsDto ai,
        AiCompletionLevel level)
    {
        ArgumentNullException.ThrowIfNull(ai);
        var selection = level switch
        {
            AiCompletionLevel.Simple => ai.Simple,
            AiCompletionLevel.QualityCheck => ai.QualityCheck,
            AiCompletionLevel.Writing => ai.Writing,
            AiCompletionLevel.DeepAnalysis => ai.DeepAnalysis,
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown AI level."),
        };

        var provider = string.IsNullOrWhiteSpace(selection.Provider)
            ? AiProviderNames.Ollama
            : selection.Provider.Trim();
        var model = !string.IsNullOrWhiteSpace(selection.Model)
            ? selection.Model.Trim()
            : ai.OllamaModel?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(model))
        {
            return (provider, model, AiCompletionResult.Fail(
                AiCompletionFailureKind.ProviderNotConfigured,
                "לא הוגדר מודל AI. יש להגדיר מודלים בהגדרות המערכת.",
                provider,
                model));
        }

        return (provider, model, null);
    }

    private async Task<AiCompletionResult> CompleteOllamaAsync(
        AiSystemSettingsDto ai,
        string model,
        AiCompletionRequest request,
        CancellationToken cancellationToken)
    {
        var baseUrl = NormalizeOllamaBaseUrl(ai.OllamaBaseUrl);
        var prompt = ComposePrompt(request);
        var payload = request.ExpectJson
            ? JsonSerializer.Serialize(new { model, prompt, stream = false, format = "json" })
            : JsonSerializer.Serialize(new { model, prompt, stream = false });

        var response = await _transport
            .SendAsync(
                new AiHttpRequest(
                    "POST",
                    new Uri(new Uri(baseUrl + "/"), "api/generate"),
                    payload,
                    request.Timeout),
                cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is < 200 or >= 300)
        {
            return AiCompletionResult.Fail(
                AiCompletionFailureKind.Unavailable,
                "שירות Ollama אינו זמין כרגע.",
                AiProviderNames.Ollama,
                model);
        }

        using var doc = JsonDocument.Parse(response.Body);
        if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
        {
            return AiCompletionResult.Fail(
                AiCompletionFailureKind.ProviderError,
                "Ollama החזיר שגיאה.",
                AiProviderNames.Ollama,
                model);
        }

        if (!doc.RootElement.TryGetProperty("response", out var textEl) || textEl.ValueKind != JsonValueKind.String)
        {
            return AiCompletionResult.Fail(
                AiCompletionFailureKind.MalformedResponse,
                "תשובת Ollama אינה תקינה.",
                AiProviderNames.Ollama,
                model);
        }

        var text = textEl.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return AiCompletionResult.Fail(
                AiCompletionFailureKind.MalformedResponse,
                "תשובת Ollama ריקה.",
                AiProviderNames.Ollama,
                model);
        }

        return AiCompletionResult.Ok(text, AiProviderNames.Ollama, model);
    }

    private async Task<AiCompletionResult> CompleteGeminiAsync(
        string model,
        AiCompletionRequest request,
        CancellationToken cancellationToken)
    {
        if (_vault is null || !_vault.HasSecret(SecretCatalog.GeminiApiKey))
        {
            return AiCompletionResult.Fail(
                AiCompletionFailureKind.Unavailable,
                "מפתח Gemini אינו זמין במערכת.",
                AiProviderNames.Gemini,
                model);
        }

        var apiKey = _vault.GetSecret(SecretCatalog.GeminiApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return AiCompletionResult.Fail(
                AiCompletionFailureKind.Unavailable,
                "מפתח Gemini אינו זמין במערכת.",
                AiProviderNames.Gemini,
                model);
        }

        var prompt = ComposePrompt(request);
        object generationConfig = request.ExpectJson
            ? new { responseMimeType = "application/json", temperature = 0.2, maxOutputTokens = 2048 }
            : new { temperature = 0.2, maxOutputTokens = 2048 };
        var payload = JsonSerializer.Serialize(new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } },
            },
            generationConfig,
        });

        var uri = new Uri(
            $"{GeminiModelsRoot.AbsoluteUri}{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(apiKey)}");
        var response = await _transport
            .SendAsync(new AiHttpRequest("POST", uri, payload, request.Timeout), cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is 401 or 403)
        {
            return AiCompletionResult.Fail(
                AiCompletionFailureKind.Unavailable,
                "מפתח Gemini נדחה או חסר.",
                AiProviderNames.Gemini,
                model);
        }

        if (response.StatusCode is < 200 or >= 300)
        {
            return AiCompletionResult.Fail(
                AiCompletionFailureKind.ProviderError,
                "שירות Gemini החזיר שגיאה.",
                AiProviderNames.Gemini,
                model);
        }

        using var doc = JsonDocument.Parse(response.Body);
        if (!doc.RootElement.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0)
        {
            return AiCompletionResult.Fail(
                AiCompletionFailureKind.MalformedResponse,
                "תשובת Gemini אינה תקינה.",
                AiProviderNames.Gemini,
                model);
        }

        var first = candidates[0];
        if (!first.TryGetProperty("content", out var content)
            || !content.TryGetProperty("parts", out var parts)
            || parts.ValueKind != JsonValueKind.Array
            || parts.GetArrayLength() == 0
            || !parts[0].TryGetProperty("text", out var textEl))
        {
            return AiCompletionResult.Fail(
                AiCompletionFailureKind.MalformedResponse,
                "תשובת Gemini אינה תקינה.",
                AiProviderNames.Gemini,
                model);
        }

        var text = textEl.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return AiCompletionResult.Fail(
                AiCompletionFailureKind.MalformedResponse,
                "תשובת Gemini ריקה.",
                AiProviderNames.Gemini,
                model);
        }

        return AiCompletionResult.Ok(text, AiProviderNames.Gemini, model);
    }

    private static string ComposePrompt(AiCompletionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SystemInstructions))
        {
            return request.Prompt ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return request.SystemInstructions.Trim();
        }

        return request.SystemInstructions.Trim() + Environment.NewLine + Environment.NewLine + request.Prompt;
    }

    private static string NormalizeOllamaBaseUrl(string? url)
    {
        var value = string.IsNullOrWhiteSpace(url) ? SystemSettingsDefaults.OllamaBaseUrl : url.Trim().TrimEnd('/');
        return value;
    }
}
