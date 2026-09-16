using System.Net.Http;
using System.Text.Json;
using SiNet.Application.Ai;
using SiNet.Application.Configuration;
using SiNet.Application.Settings;
using SiNet.Infrastructure.Sql.Services.Ai;
using Xunit;

namespace SiNet.App.Wpf.Tests.Ai;

public sealed class SettingsAiCompletionServiceTests
{
    [Fact]
    public async Task CompleteAsync_resolves_Simple_provider_and_model_from_system_settings()
    {
        var transport = new FakeAiHttpTransport
        {
            Handler = (request, _) => Task.FromResult(OllamaOk("ok")),
        };
        var sut = CreateSut(
            transport,
            simpleProvider: AiProviderNames.Ollama,
            simpleModel: "settings-simple-model",
            ollamaModel: "fallback-should-not-win");

        var result = await sut.CompleteAsync(new AiCompletionRequest("hello", AiCompletionLevel.Simple));

        Assert.True(result.Succeeded);
        Assert.Equal(AiProviderNames.Ollama, result.ResolvedProvider);
        Assert.Equal("settings-simple-model", result.ResolvedModel);
        var request = Assert.Single(transport.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("http://127.0.0.1:11434/api/generate", request.Uri.ToString());
        using var body = JsonDocument.Parse(request.JsonBody!);
        Assert.Equal("settings-simple-model", body.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task CompleteAsync_ollama_success_returns_text()
    {
        var transport = new FakeAiHttpTransport
        {
            Handler = (_, _) => Task.FromResult(OllamaOk("  corrected text  ")),
        };
        var sut = CreateSut(transport);

        var result = await sut.CompleteAsync(new AiCompletionRequest("prompt"));

        Assert.True(result.Succeeded);
        Assert.Equal("corrected text", result.Text);
        Assert.Equal(AiCompletionFailureKind.None, result.Failure);
    }

    [Fact]
    public async Task CompleteAsync_ollama_unavailable_is_clean_failure()
    {
        var transport = new FakeAiHttpTransport
        {
            Handler = (_, _) => Task.FromResult(new AiHttpResponse(503, "down")),
        };
        var sut = CreateSut(transport);

        var result = await sut.CompleteAsync(new AiCompletionRequest("prompt"));

        Assert.False(result.Succeeded);
        Assert.Equal(AiCompletionFailureKind.Unavailable, result.Failure);
        Assert.False(string.IsNullOrWhiteSpace(result.UserMessageHe));
    }

    [Fact]
    public async Task CompleteAsync_ollama_http_exception_is_unavailable()
    {
        var transport = new FakeAiHttpTransport
        {
            Handler = (_, _) => throw new HttpRequestException("refused"),
        };
        var sut = CreateSut(transport);

        var result = await sut.CompleteAsync(new AiCompletionRequest("prompt"));

        Assert.False(result.Succeeded);
        Assert.Equal(AiCompletionFailureKind.Unavailable, result.Failure);
    }

    [Fact]
    public async Task CompleteAsync_gemini_success_uses_vault_key_and_settings_model()
    {
        var transport = new FakeAiHttpTransport
        {
            Handler = (_, _) => Task.FromResult(GeminiOk("{\"ok\":true}")),
        };
        var vault = new MemoryVault();
        vault.SetSecret(SecretCatalog.GeminiApiKey, "existing-gemini-key");
        var sut = CreateSut(
            transport,
            vault,
            simpleProvider: AiProviderNames.Gemini,
            simpleModel: "gemini-2.5-flash");

        var result = await sut.CompleteAsync(new AiCompletionRequest("prompt", ExpectJson: true));

        Assert.True(result.Succeeded);
        Assert.Equal("{\"ok\":true}", result.Text);
        Assert.Equal(AiProviderNames.Gemini, result.ResolvedProvider);
        Assert.Equal("gemini-2.5-flash", result.ResolvedModel);
        var request = Assert.Single(transport.Requests);
        Assert.Contains("gemini-2.5-flash", request.Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("existing-gemini-key", request.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("generativelanguage.googleapis.com", request.Uri.Host, StringComparison.Ordinal);
        Assert.Contains("application/json", request.JsonBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_missing_gemini_secret_is_unavailable_without_http()
    {
        var transport = new FakeAiHttpTransport
        {
            Handler = (_, _) => throw new InvalidOperationException("must not call transport"),
        };
        var sut = CreateSut(
            transport,
            new MemoryVault(),
            simpleProvider: AiProviderNames.Gemini,
            simpleModel: "gemini-2.5-flash");

        var result = await sut.CompleteAsync(new AiCompletionRequest("prompt"));

        Assert.False(result.Succeeded);
        Assert.Equal(AiCompletionFailureKind.Unavailable, result.Failure);
        Assert.Contains("Gemini", result.UserMessageHe, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task CompleteAsync_honours_cancellation()
    {
        using var cts = new CancellationTokenSource();
        var transport = new FakeAiHttpTransport
        {
            Handler = async (_, token) =>
            {
                cts.Cancel();
                await Task.Delay(Timeout.Infinite, token);
                return OllamaOk("late");
            },
        };
        var sut = CreateSut(transport);

        var result = await sut.CompleteAsync(new AiCompletionRequest("prompt"), cts.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(AiCompletionFailureKind.Cancelled, result.Failure);
    }

    [Fact]
    public async Task CompleteAsync_malformed_ollama_response_is_clean_failure()
    {
        var transport = new FakeAiHttpTransport
        {
            Handler = (_, _) => Task.FromResult(new AiHttpResponse(200, "{not-json")),
        };
        var sut = CreateSut(transport);

        var result = await sut.CompleteAsync(new AiCompletionRequest("prompt"));

        Assert.False(result.Succeeded);
        Assert.Equal(AiCompletionFailureKind.MalformedResponse, result.Failure);
    }

    [Fact]
    public async Task CompleteAsync_openai_compatible_is_not_configured_without_http()
    {
        var transport = new FakeAiHttpTransport
        {
            Handler = (_, _) => throw new InvalidOperationException("must not call transport"),
        };
        var sut = CreateSut(transport, simpleProvider: AiProviderNames.OpenAiCompatible, simpleModel: "gpt-x");

        var result = await sut.CompleteAsync(new AiCompletionRequest("prompt"));

        Assert.False(result.Succeeded);
        Assert.Equal(AiCompletionFailureKind.ProviderNotConfigured, result.Failure);
        Assert.Equal(AiProviderNames.OpenAiCompatible, result.ResolvedProvider);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task CompleteAsync_unknown_provider_is_not_configured_without_http()
    {
        var transport = new FakeAiHttpTransport
        {
            Handler = (_, _) => throw new InvalidOperationException("must not call transport"),
        };
        var sut = CreateSut(transport, simpleProvider: "UnknownCloud", simpleModel: "x");

        var result = await sut.CompleteAsync(new AiCompletionRequest("prompt"));

        Assert.False(result.Succeeded);
        Assert.Equal(AiCompletionFailureKind.ProviderNotConfigured, result.Failure);
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task IsAvailableAsync_gemini_is_true_only_when_vault_has_existing_key()
    {
        var transport = new FakeAiHttpTransport
        {
            Handler = (_, _) => throw new InvalidOperationException("gemini availability must not HTTP"),
        };
        var empty = CreateSut(transport, new MemoryVault(), AiProviderNames.Gemini, "gemini-2.5-flash");
        Assert.False(await empty.IsAvailableAsync());

        var vault = new MemoryVault();
        vault.SetSecret(SecretCatalog.GeminiApiKey, "existing-gemini-key");
        var ready = CreateSut(transport, vault, AiProviderNames.Gemini, "gemini-2.5-flash");
        Assert.True(await ready.IsAvailableAsync());
        Assert.Empty(transport.Requests);
    }

    private static SettingsAiCompletionService CreateSut(
        FakeAiHttpTransport transport,
        ISecretVaultStore? vault = null,
        string simpleProvider = AiProviderNames.Ollama,
        string simpleModel = "test-model",
        string ollamaModel = "fallback-model")
    {
        var settings = new StubSettings(new AiSystemSettingsDto(
            "http://127.0.0.1:11434",
            ollamaModel,
            new AiModelLevelSelectionDto(simpleModel, simpleProvider),
            new AiModelLevelSelectionDto("quality-model", AiProviderNames.Ollama),
            new AiModelLevelSelectionDto("writing-model", AiProviderNames.Ollama),
            new AiModelLevelSelectionDto("deep-model", AiProviderNames.Ollama),
            string.Empty));
        return new SettingsAiCompletionService(settings, transport, vault);
    }

    private static AiHttpResponse OllamaOk(string text) =>
        new(200, JsonSerializer.Serialize(new { response = text }));

    private static AiHttpResponse GeminiOk(string text) =>
        new(200, JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new { content = new { parts = new[] { new { text } } } },
            },
        }));

    private sealed class FakeAiHttpTransport : IAiHttpTransport
    {
        public List<AiHttpRequest> Requests { get; } = [];

        public Func<AiHttpRequest, CancellationToken, Task<AiHttpResponse>> Handler { get; set; } =
            (_, _) => Task.FromResult(new AiHttpResponse(500, string.Empty));

        public Task<AiHttpResponse> SendAsync(AiHttpRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Handler(request, cancellationToken);
        }
    }

    private sealed class MemoryVault : ISecretVaultStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public bool HasSecret(string key) => _values.ContainsKey(key);

        public string? GetSecret(string key) => _values.TryGetValue(key, out var value) ? value : null;

        public void SetSecret(string key, string value) => _values[key] = value;

        public void DeleteSecret(string key) => _values.Remove(key);

        public IReadOnlyDictionary<string, bool> GetVaultStatus() =>
            _values.ToDictionary(static x => x.Key, static _ => true, StringComparer.Ordinal);
    }

    private sealed class StubSettings(AiSystemSettingsDto ai) : ISystemSettingsQueryService
    {
        public Task<SystemSettingsDto> GetSystemSettingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(AiTestSettings.Create(ai));
    }
}
